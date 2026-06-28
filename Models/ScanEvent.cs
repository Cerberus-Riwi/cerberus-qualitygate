namespace QualityGateService.Models;

public sealed class ScanEvent
{
    public Guid ScanId { get; set; }
    public string ServiceId { get; set; } = "legacy-quality-gate-input";
    public string Status { get; set; } = ScanResultStatuses.Success;
    public string? DeploymentId { get; set; }
    public List<Finding> Findings { get; set; } = [];
    public List<ScanResult> Results { get; set; } = [];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public IReadOnlyCollection<ScanResult> ToScanResults()
    {
        if (Results.Count > 0)
        {
            return Results;
        }

        return
        [
            new ScanResult
            {
                ScanId = ScanId,
                ServiceId = ServiceId,
                Status = Status,
                Findings = Findings,
                CompletedAt = CompletedAt ?? Timestamp,
                ErrorMessage = ErrorMessage
            }
        ];
    }
}
