namespace QualityGateService.Models;

public sealed class ScanResult
{
    public Guid ScanId { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public string Status { get; set; } = ScanResultStatuses.Success;
    public List<Finding> Findings { get; set; } = [];
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}

public static class ScanResultStatuses
{
    public const string Success = "success";
    public const string Failed = "failed";
    public const string Timeout = "timeout";
}
