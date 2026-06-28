using QualityGateService.Models;

namespace QualityGateService.Rules;

public sealed class CvssRulesEngine
{
    public QualityGateResult Evaluate(IReadOnlyCollection<ScanResult> scanResults, string? deploymentId = null)
    {
        ArgumentNullException.ThrowIfNull(scanResults);

        if (scanResults.Count == 0)
        {
            throw new ArgumentException("At least one scan-result is required.", nameof(scanResults));
        }

        var scanId = scanResults.First().ScanId;
        if (scanResults.Any(result => result.ScanId != scanId))
        {
            throw new ArgumentException("All scan-results must share the same scanId.", nameof(scanResults));
        }

        foreach (var finding in scanResults.SelectMany(result => result.Findings))
        {
            finding.ScanId = scanId;
        }

        var summary = BuildSummary(scanResults);
        var verdict = EvaluateVerdict(summary);
        var shouldRollback = verdict == QualityGateVerdicts.Fail;

        return new QualityGateResult
        {
            ScanId = scanId,
            Verdict = verdict,
            Summary = summary,
            Results = scanResults.ToList(),
            RollbackTriggered = shouldRollback && !string.IsNullOrWhiteSpace(deploymentId),
            RollbackMessage = shouldRollback && string.IsNullOrWhiteSpace(deploymentId)
                ? "Rollback skipped: deploymentId is required."
                : string.Empty,
            Action = shouldRollback ? QualityGateActions.Rollback : QualityGateActions.None,
            IssuedAt = DateTime.UtcNow,
            DeploymentId = deploymentId ?? string.Empty
        };
    }

    private static SeveritySummary BuildSummary(IEnumerable<ScanResult> scanResults)
    {
        var findings = scanResults.SelectMany(result => result.Findings);
        return new SeveritySummary
        {
            Critical = findings.Count(finding => finding.Severity == FindingSeverity.Critical),
            High = findings.Count(finding => finding.Severity == FindingSeverity.High),
            Medium = findings.Count(finding => finding.Severity == FindingSeverity.Medium),
            Low = findings.Count(finding => finding.Severity == FindingSeverity.Low),
            Info = findings.Count(finding => finding.Severity == FindingSeverity.Info)
        };
    }

    private static string EvaluateVerdict(SeveritySummary summary)
    {
        if (summary.Critical > 0 || summary.High > 0)
        {
            return QualityGateVerdicts.Fail;
        }

        if (summary.Medium > 0 || summary.Low > 0)
        {
            return QualityGateVerdicts.Warning;
        }

        return QualityGateVerdicts.Pass;
    }
}
