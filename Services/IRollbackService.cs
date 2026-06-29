namespace QualityGateService.Services;

public interface IRollbackService
{
    Task<RollbackResult> RollbackAsync(string deploymentId, CancellationToken cancellationToken = default);
}

public sealed record RollbackResult(
    string DeploymentId,
    bool RolledBack,
    string Message,
    DateTime Timestamp);
