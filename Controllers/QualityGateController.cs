using System.Security.Cryptography;
using System.Text;
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
    public async Task<IActionResult> Evaluate([FromBody] ScanEvent scanEvent, CancellationToken cancellationToken)
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

    private IActionResult? ValidateInternalToken()
    {
        if (string.IsNullOrWhiteSpace(_settings.InternalToken))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Internal token is not configured." });
        }

        if (!Request.Headers.TryGetValue("X-Internal-Token", out var token)
            || !TokenMatches(token.ToString(), _settings.InternalToken))
        {
            return Unauthorized(new { error = "Invalid internal token." });
        }

        return null;
    }

    private static bool TokenMatches(string providedToken, string expectedToken)
    {
        var providedBytes = Encoding.UTF8.GetBytes(providedToken);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);

        return providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
