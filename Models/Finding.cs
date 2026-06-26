namespace QualityGateService.Models;

public sealed class Finding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScanId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FindingSeverity Severity { get; set; }
    public decimal CvssScore { get; set; }
    public string CvssVector { get; set; } = string.Empty;
    public string CweId { get; set; } = string.Empty;
    public string CveId { get; set; } = string.Empty;
    public SecurityTool Tool { get; set; }
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
