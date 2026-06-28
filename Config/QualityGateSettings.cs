using System.ComponentModel.DataAnnotations;

namespace QualityGateService.Config;

public sealed class QualityGateSettings
{
    public const string SectionName = "QualityGate";

    [Required]
    public string RabbitMQHost { get; set; } = "localhost";

    [Required]
    public string RabbitMQQueue { get; set; } = "cerberus.quality-gate.queue";

    [Required]
    public string RabbitMQExchange { get; set; } = "cerberus.findings.ready";

    [Required]
    public string RabbitMQResultsExchange { get; set; } = "cerberus.gate.results";

    [Required]
    public string KubernetesNamespace { get; set; } = "cerberus";

    [Required]
    public string KubernetesApiUrl { get; set; } = "https://kubernetes.default.svc";

    public string? KubernetesServiceAccountToken { get; set; }

    [Range(1, int.MaxValue)]
    public int ExpectedScanResultsPerScan { get; set; } = 2;

    [Range(1, int.MaxValue)]
    public int PendingScanTimeoutSeconds { get; set; } = 30;

    public string? InternalToken { get; set; }
}
