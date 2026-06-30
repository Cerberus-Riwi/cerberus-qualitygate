using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QualityGateService.Config;
using QualityGateService.Controllers;
using QualityGateService.Models;
using QualityGateService.Services;

namespace QualityGateService.Tests;

public sealed class QualityGateControllerTests
{
    [Fact]
    public async Task ReceiveScanWebhookResult_AcceptsDirectFindingPayload_WithTokenHeader_ReturnsOk()
    {
        var controller = CreateController("shared-secret");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.ControllerContext.HttpContext.Request.Headers["X-Internal-Token"] = "shared-secret";

        var body = JsonSerializer.SerializeToElement(new
        {
            scanId = "11111111-1111-1111-1111-111111111111",
            serviceId = "orders-api",
            status = "success",
            findings = new[]
            {
                new
                {
                    title = "SQL Injection",
                    severity = "critical"
                }
            },
            completedAt = "2026-06-25T10:00:00Z"
        });

        var result = await controller.ReceiveScanWebhookResult(body, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var value = Assert.IsType<QualityGateResult>(okResult.Value);
        Assert.Equal(QualityGateVerdicts.Pass, value.Verdict);
    }

    [Fact]
    public async Task ReceiveScanWebhookResult_ReturnsBadRequest_WhenPayloadHasNoFindings()
    {
        var controller = CreateController("shared-secret");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.ControllerContext.HttpContext.Request.Headers["X-Internal-Token"] = "shared-secret";

        var body = JsonSerializer.SerializeToElement(new { foo = "bar" });

        var result = await controller.ReceiveScanWebhookResult(body, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
    }

    private static QualityGateController CreateController(string internalToken)
    {
        var evaluator = new FakeQualityGateEvaluatorService();
        var rollbackService = new FakeRollbackService();
        var options = Options.Create(new QualityGateSettings
        {
            InternalToken = internalToken
        });

        return new QualityGateController(evaluator, rollbackService, options, NullLogger<QualityGateController>.Instance);
    }

    private sealed class FakeQualityGateEvaluatorService : IQualityGateEvaluatorService
    {
        public Task<QualityGateResult> EvaluateAsync(ScanEvent scanEvent, CancellationToken cancellationToken = default)
            => Task.FromResult(new QualityGateResult
            {
                ScanId = scanEvent.ScanId,
                Verdict = QualityGateVerdicts.Pass,
                Summary = new SeveritySummary(),
                Results = scanEvent.ToScanResults().ToList(),
                IssuedAt = DateTime.UtcNow
            });

        public Task<QualityGateResult> EvaluateAsync(IReadOnlyCollection<ScanResult> scanResults, string? deploymentId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new QualityGateResult
            {
                ScanId = scanResults.FirstOrDefault()?.ScanId ?? Guid.Empty,
                Verdict = QualityGateVerdicts.Pass,
                Summary = new SeveritySummary(),
                Results = scanResults.ToList(),
                IssuedAt = DateTime.UtcNow,
                DeploymentId = deploymentId ?? string.Empty
            });
    }

    private sealed class FakeRollbackService : IRollbackService
    {
        public Task<RollbackResult> RollbackAsync(string deploymentId, CancellationToken cancellationToken = default)
            => Task.FromResult(new RollbackResult(deploymentId, false, "not used", DateTime.UtcNow));
    }
}