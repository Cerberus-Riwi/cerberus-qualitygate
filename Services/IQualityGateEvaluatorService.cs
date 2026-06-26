using QualityGateService.Models;

namespace QualityGateService.Services;

public interface IQualityGateEvaluatorService
{
    Task<QualityGateResult> EvaluateAsync(ScanEvent scanEvent, CancellationToken cancellationToken = default);
}
