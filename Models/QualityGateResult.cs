namespace QualityGateService.Models;

public sealed class QualityGateResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScanId { get; set; }
    public bool Passed { get; set; }
    public string? BlockedBy { get; set; }
    public QualityGateAction Action { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
    public string? DeploymentId { get; set; }
}

public enum QualityGateAction
{
    None,
    Rollback
}
