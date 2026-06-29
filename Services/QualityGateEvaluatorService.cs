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

        return await EvaluateAsync(scanEvent.ToScanResults().ToList(), scanEvent.DeploymentId, cancellationToken);
    }

    public async Task<QualityGateResult> EvaluateAsync(
        IReadOnlyCollection<ScanResult> scanResults,
        string? deploymentId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scanResults);

        if (scanResults.Count == 0)
        {
            throw new ArgumentException("At least one scan-result is required.", nameof(scanResults));
        }

        var scanId = scanResults.First().ScanId;
        if (scanId == Guid.Empty)
        {
            throw new ArgumentException("ScanId is required.", nameof(scanResults));
        }

        foreach (var finding in scanResults.SelectMany(result => result.Findings))
        {
            finding.ScanId = scanId;
        }

        var result = rulesEngine.Evaluate(scanResults, deploymentId);
        logger.LogInformation(
            "Quality gate evaluated for scan {ScanId}. Verdict: {Verdict}. Rollback triggered: {RollbackTriggered}",
            result.ScanId,
            result.Verdict,
            result.RollbackTriggered);

        if (result.RollbackTriggered && !string.IsNullOrWhiteSpace(result.DeploymentId))
        {
            var rollbackResult = await rollbackService.RollbackAsync(result.DeploymentId, cancellationToken);
            result.RollbackExecuted = rollbackResult.RolledBack;
            result.RollbackMessage = rollbackResult.Message;

            logger.LogWarning(
                "Rollback result for deployment {DeploymentId}: {RolledBack}. {Message}",
                rollbackResult.DeploymentId,
                rollbackResult.RolledBack,
                rollbackResult.Message);
        }
        else if (result.Verdict == QualityGateVerdicts.Fail)
        {
            logger.LogError(
                "Quality gate failed for scan {ScanId}, but rollback was not executed. Reason: {Reason}",
                result.ScanId,
                result.RollbackMessage);
        }

        dbContext.Findings.AddRange(scanResults.SelectMany(scanResult => scanResult.Findings));
        dbContext.QualityGateResults.Add(result);
        await dbContext.SaveChangesAsync(cancellationToken);

        return result;
    }
}
