namespace QualityGateService.Models;

public sealed class ScanEvent
{
    public Guid ScanId { get; set; }
    public string? DeploymentId { get; set; }
    public List<Finding> Findings { get; set; } = [];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
