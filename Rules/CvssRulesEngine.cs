using Microsoft.Extensions.Options;
using QualityGateService.Config;
using QualityGateService.Models;

namespace QualityGateService.Rules;

public sealed class CvssRulesEngine(IOptions<QualityGateSettings> options)
{
    private readonly QualityGateSettings _settings = options.Value;

    public QualityGateResult Evaluate(ScanEvent scanEvent)
    {
        var criticalCount = scanEvent.Findings.Count(finding => finding.Severity == FindingSeverity.Critical);
        var highCount = scanEvent.Findings.Count(finding => finding.Severity == FindingSeverity.High);
        var result = new QualityGateResult
        {
            ScanId = scanEvent.ScanId,
            DeploymentId = scanEvent.DeploymentId,
            CriticalCount = criticalCount,
            HighCount = highCount,
            EvaluatedAt = DateTime.UtcNow
        };

        if (criticalCount > _settings.MaxCriticalAllowed)
        {
            return Fail(result, $"{criticalCount} critical vulnerabilities detected (maximum allowed: {_settings.MaxCriticalAllowed})");
        }

        if (highCount > _settings.MaxHighAllowed)
        {
            return Fail(result, $"{highCount} high vulnerabilities detected (maximum allowed: {_settings.MaxHighAllowed})");
        }

        var blockingFinding = scanEvent.Findings
            .OrderByDescending(finding => finding.CvssScore)
            .FirstOrDefault(finding => finding.CvssScore >= _settings.MinCvssScoreToBlock);

        if (blockingFinding is not null)
        {
            return Fail(result, $"Finding '{blockingFinding.Title}' has CVSS score {blockingFinding.CvssScore:0.0} (minimum to block: {_settings.MinCvssScoreToBlock:0.0})");
        }

        result.Passed = true;
        result.Action = QualityGateAction.None;
        return result;
    }

    private static QualityGateResult Fail(QualityGateResult result, string blockedBy)
    {
        result.Passed = false;
        result.BlockedBy = blockedBy;
        result.Action = string.IsNullOrWhiteSpace(result.DeploymentId)
            ? QualityGateAction.None
            : QualityGateAction.Rollback;

        return result;
    }
}
