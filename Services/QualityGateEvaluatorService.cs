using QualityGateService.Data;
using QualityGateService.Models;
using QualityGateService.Rules;

namespace QualityGateService.Services;

public sealed class QualityGateEvaluatorService(
    CvssRulesEngine rulesEngine,
    IRollbackService rollbackService,
    AppDbContext dbContext,
    ILogger<QualityGateEvaluatorService> logger) : IQualityGateEvaluatorService
{
    public async Task<QualityGateResult> EvaluateAsync(ScanEvent scanEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scanEvent);

        if (scanEvent.ScanId == Guid.Empty)
        {
            throw new ArgumentException("ScanId is required.", nameof(scanEvent));
        }

        scanEvent.Findings ??= [];
        foreach (var finding in scanEvent.Findings)
        {
            finding.ScanId = scanEvent.ScanId;
        }

        var result = rulesEngine.Evaluate(scanEvent);
        logger.LogInformation(
            "Quality gate evaluated for scan {ScanId}. Passed: {Passed}. Action: {Action}",
            result.ScanId,
            result.Passed,
            result.Action);

        if (result.Action == QualityGateAction.Rollback && !string.IsNullOrWhiteSpace(result.DeploymentId))
        {
            var rollbackResult = await rollbackService.RollbackAsync(result.DeploymentId, cancellationToken);
            logger.LogWarning(
                "Rollback result for deployment {DeploymentId}: {RolledBack}. {Message}",
                rollbackResult.DeploymentId,
                rollbackResult.RolledBack,
                rollbackResult.Message);
        }

        dbContext.Findings.AddRange(scanEvent.Findings);
        dbContext.QualityGateResults.Add(result);
        await dbContext.SaveChangesAsync(cancellationToken);

        return result;
    }
}
