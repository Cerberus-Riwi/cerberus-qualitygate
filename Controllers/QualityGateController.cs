using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QualityGateService.Config;
using QualityGateService.Models;
using QualityGateService.Services;

namespace QualityGateService.Controllers;

[ApiController]
[Route("api/quality-gate")]
public sealed class QualityGateController(
    IQualityGateEvaluatorService evaluatorService,
    IRollbackService rollbackService,
    IOptions<QualityGateSettings> options,
    ILogger<QualityGateController> logger) : ControllerBase
{
    private readonly QualityGateSettings _settings = options.Value;

    /// <summary>
    /// Evaluates normalized findings and returns the quality gate decision.
    /// </summary>
    [HttpPost("evaluate")]
    [ProducesResponseType(typeof(QualityGateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> Evaluate([FromBody] ScanEvent scanEvent, CancellationToken cancellationToken)
        => EvaluateScanAsync(scanEvent, cancellationToken);

    /// <summary>
    /// Receives scan results from the external scanner webhook and evaluates the quality gate.
    /// </summary>
    [HttpPost("~/api/scan/webhook/result")]
    [ProducesResponseType(typeof(QualityGateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ReceiveScanWebhookResult([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var tokenValidation = ValidateInternalToken();
        if (tokenValidation is not null)
        {
            return tokenValidation;
        }

        if (!TryBuildScanEvent(body, out var scanEvent, out var errorMessage))
        {
            logger.LogWarning("Invalid webhook payload received: {ErrorMessage}", errorMessage);
            return BadRequest(new { error = errorMessage ?? "Invalid webhook payload." });
        }

        return await EvaluateScanAsync(scanEvent!, cancellationToken);
    }

    /// <summary>
    /// Triggers rollback for the specified Kubernetes deployment.
    /// </summary>
    [HttpPost("rollback/{deploymentId}")]
    [ProducesResponseType(typeof(RollbackResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Rollback([FromRoute] string deploymentId, CancellationToken cancellationToken)
    {
        var tokenValidation = ValidateInternalToken();
        if (tokenValidation is not null)
        {
            return tokenValidation;
        }

        try
        {
            var result = await rollbackService.RollbackAsync(deploymentId, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid rollback request.");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rollback request failed for deployment {DeploymentId}", deploymentId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Rollback request failed." });
        }
    }

    private async Task<IActionResult> EvaluateScanAsync(ScanEvent scanEvent, CancellationToken cancellationToken)
    {
        var tokenValidation = ValidateInternalToken();
        if (tokenValidation is not null)
        {
            return tokenValidation;
        }

        try
        {
            var result = await evaluatorService.EvaluateAsync(scanEvent, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid quality gate evaluation request.");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Quality gate evaluation failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Quality gate evaluation failed." });
        }
    }

    private IActionResult? ValidateInternalToken()
    {
        if (string.IsNullOrWhiteSpace(_settings.InternalToken))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Internal token is not configured." });
        }

        var providedToken = GetProvidedToken();
        if (string.IsNullOrWhiteSpace(providedToken)
            || !TokenMatches(providedToken, _settings.InternalToken))
        {
            return Unauthorized(new { error = "Invalid internal token." });
        }

        return null;
    }

    private string? GetProvidedToken()
    {
        if (Request.Headers.TryGetValue("X-Internal-Token", out var headerToken))
        {
            return headerToken.ToString();
        }

        if (Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
        {
            var headerValue = authorizationHeader.ToString();
            if (headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return headerValue[7..].Trim();
            }

            return headerValue;
        }

        return Request.Query["token"].ToString();
    }

    private static bool TryBuildScanEvent(JsonElement body, out ScanEvent? scanEvent, out string? errorMessage)
    {
        scanEvent = null;
        errorMessage = null;

        if (body.ValueKind == JsonValueKind.Undefined || body.ValueKind == JsonValueKind.Null)
        {
            errorMessage = "Request body is required.";
            return false;
        }

        if (body.ValueKind == JsonValueKind.Array)
        {
            var results = JsonSerializer.Deserialize<List<ScanResult>>(body.GetRawText());
            if (results is null || results.Count == 0)
            {
                errorMessage = "At least one scan result is required.";
                return false;
            }

            scanEvent = new ScanEvent
            {
                Results = results
            };
            return true;
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "Request body must be a JSON object or array.";
            return false;
        }

        try
        {
            if (body.TryGetProperty("results", out var resultsProperty) && resultsProperty.ValueKind == JsonValueKind.Array)
            {
                var results = JsonSerializer.Deserialize<List<ScanResult>>(resultsProperty.GetRawText());
                if (results is not null && results.Count > 0)
                {
                    scanEvent = new ScanEvent
                    {
                        ScanId = body.TryGetProperty("scanId", out var scanIdProperty) && scanIdProperty.ValueKind == JsonValueKind.String && Guid.TryParse(scanIdProperty.GetString(), out var parsedScanId) ? parsedScanId : Guid.NewGuid(),
                        DeploymentId = body.TryGetProperty("deploymentId", out var deploymentIdProperty) && deploymentIdProperty.ValueKind == JsonValueKind.String ? deploymentIdProperty.GetString() : null,
                        Status = body.TryGetProperty("status", out var statusProperty) && statusProperty.ValueKind == JsonValueKind.String ? statusProperty.GetString() ?? ScanResultStatuses.Success : ScanResultStatuses.Success,
                        Results = results,
                        CompletedAt = body.TryGetProperty("completedAt", out var completedAtProperty) && completedAtProperty.ValueKind == JsonValueKind.String && DateTime.TryParse(completedAtProperty.GetString(), out var parsedCompletedAt) ? parsedCompletedAt : DateTime.UtcNow,
                        ErrorMessage = body.TryGetProperty("errorMessage", out var errorMessageProperty) && errorMessageProperty.ValueKind == JsonValueKind.String ? errorMessageProperty.GetString() : null
                    };
                    return true;
                }
            }

            if (body.TryGetProperty("findings", out var findingsProperty) && findingsProperty.ValueKind == JsonValueKind.Array)
            {
                var findings = JsonSerializer.Deserialize<List<Finding>>(findingsProperty.GetRawText());
                if (findings is not null && findings.Count > 0)
                {
                    var scanId = Guid.NewGuid();
                    if (body.TryGetProperty("scanId", out var scanIdProperty) && scanIdProperty.ValueKind == JsonValueKind.String && Guid.TryParse(scanIdProperty.GetString(), out var parsedScanId))
                    {
                        scanId = parsedScanId;
                    }

                    var completedAt = DateTime.UtcNow;
                    if (body.TryGetProperty("completedAt", out var completedAtProperty) && completedAtProperty.ValueKind == JsonValueKind.String && DateTime.TryParse(completedAtProperty.GetString(), out var parsedCompletedAt))
                    {
                        completedAt = parsedCompletedAt;
                    }

                    scanEvent = new ScanEvent
                    {
                        ScanId = scanId,
                        ServiceId = body.TryGetProperty("serviceId", out var serviceIdProperty) && serviceIdProperty.ValueKind == JsonValueKind.String ? serviceIdProperty.GetString() ?? string.Empty : string.Empty,
                        Status = body.TryGetProperty("status", out var statusProperty) && statusProperty.ValueKind == JsonValueKind.String ? statusProperty.GetString() ?? ScanResultStatuses.Success : ScanResultStatuses.Success,
                        Findings = findings,
                        CompletedAt = completedAt,
                        ErrorMessage = body.TryGetProperty("errorMessage", out var errorMessageProperty) && errorMessageProperty.ValueKind == JsonValueKind.String ? errorMessageProperty.GetString() : null
                    };
                    return true;
                }
            }

            var parsed = JsonSerializer.Deserialize<ScanEvent>(body.GetRawText());
            if (parsed is not null && (parsed.Results.Count > 0 || parsed.Findings.Count > 0))
            {
                scanEvent = parsed;
                return true;
            }

            errorMessage = "The webhook payload does not contain any scan results or findings.";
            return false;
        }
        catch (JsonException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static bool TokenMatches(string providedToken, string expectedToken)
    {
        var providedBytes = Encoding.UTF8.GetBytes(providedToken);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);

        return providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
