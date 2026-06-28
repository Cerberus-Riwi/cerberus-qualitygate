using QualityGateService.Models;

namespace QualityGateService.Services;

public interface IQualityGateEvaluatorService
{
    Task<QualityGateResult> EvaluateAsync(ScanEvent scanEvent, CancellationToken cancellationToken = default);
    Task<QualityGateResult> EvaluateAsync(IReadOnlyCollection<ScanResult> scanResults, string? deploymentId = null, CancellationToken cancellationToken = default);
}
