using System.Text.Json.Serialization;

namespace QualityGateService.Models;

public sealed class QualityGateResult
{
    [JsonIgnore]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ScanId { get; set; }
    public string Verdict { get; set; } = QualityGateVerdicts.Pass;
    public SeveritySummary Summary { get; set; } = new();
    public List<ScanResult> Results { get; set; } = [];
    public bool RollbackTriggered { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string? DeploymentId { get; set; }
}

public sealed class SeveritySummary
{
    public int Critical { get; set; }
    public int High { get; set; }
    public int Medium { get; set; }
    public int Low { get; set; }
    public int Info { get; set; }
}

public static class QualityGateVerdicts
{
    public const string Fail = "fail";
    public const string Warning = "warning";
    public const string Pass = "pass";
}
