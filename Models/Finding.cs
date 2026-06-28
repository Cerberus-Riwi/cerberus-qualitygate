using System.Text.Json.Serialization;

namespace QualityGateService.Models;

public sealed class Finding
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonIgnore]
    public Guid ScanId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FindingSeverity Severity { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int? LineStart { get; set; }
    public int? LineEnd { get; set; }
    public string Recommendation { get; set; } = string.Empty;

    [JsonIgnore]
    public decimal CvssScore { get; set; }

    [JsonIgnore]
    public string CvssVector { get; set; } = string.Empty;

    [JsonIgnore]
    public string CweId { get; set; } = string.Empty;

    [JsonIgnore]
    public string CveId { get; set; } = string.Empty;

    [JsonIgnore]
    public SecurityTool Tool { get; set; }

    [JsonIgnore]
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

public enum FindingSeverity
{
    Critical,
    High,
    Medium,
    Low,
    Info
}

public enum SecurityTool
{
    Semgrep,
    OWASP_ZAP,
    Trivy,
    Gitleaks
}
