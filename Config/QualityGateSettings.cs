using System.ComponentModel.DataAnnotations;

namespace QualityGateService.Config;

public sealed class QualityGateSettings
{
    public const string SectionName = "QualityGate";

    [Range(0, int.MaxValue)]
    public int MaxCriticalAllowed { get; set; } = 0;

    [Range(0, int.MaxValue)]
    public int MaxHighAllowed { get; set; } = 3;

    [Range(typeof(decimal), "0", "10")]
    public decimal MinCvssScoreToBlock { get; set; } = 7.0m;

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
}
