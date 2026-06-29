using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using QualityGateService.Config;
using QualityGateService.Models;

namespace QualityGateService.Services;

public sealed class QualityGateAggregationService(IOptions<QualityGateSettings> options)
{
    private readonly QualityGateSettings _settings = options.Value;
    private readonly ConcurrentDictionary<Guid, PendingScan> _pendingScans = new();

    public PendingScanState Add(ScanEvent scanEvent)
    {
        ArgumentNullException.ThrowIfNull(scanEvent);

        var scanResults = scanEvent.ToScanResults();
        if (scanResults.Count == 0)
        {
            throw new ArgumentException("At least one scan-result is required.", nameof(scanEvent));
        }

        var pending = _pendingScans.GetOrAdd(
            scanEvent.ScanId,
            scanId => new PendingScan(scanId, scanEvent.DeploymentId, DateTime.UtcNow));

        lock (pending)
        {
            pending.DeploymentId ??= scanEvent.DeploymentId;

            foreach (var scanResult in scanResults)
            {
                pending.Upsert(scanResult);
            }

            if (IsReady(pending))
            {
                _pendingScans.TryRemove(scanEvent.ScanId, out _);
                return PendingScanState.Ready(pending.ToScanEvent());
            }

            return PendingScanState.Waiting(scanEvent.ScanId, pending.Results.Count);
        }
    }

    public IReadOnlyCollection<ScanEvent> TakeExpired()
    {
        var expired = new List<ScanEvent>();
        var now = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(_settings.PendingScanTimeoutSeconds);

        foreach (var pair in _pendingScans)
        {
            var pending = pair.Value;
            lock (pending)
            {
                if (now - pending.FirstReceivedAt < timeout)
                {
                    continue;
                }

                if (_pendingScans.TryRemove(pair.Key, out _))
                {
                    expired.Add(pending.ToScanEvent());
                }
            }
        }

        return expired;
    }

    private bool IsReady(PendingScan pending)
    {
        return pending.Results.Count >= _settings.ExpectedScanResultsPerScan
            || pending.Results.Any(result => result.Status is ScanResultStatuses.Failed or ScanResultStatuses.Timeout);
    }
}

public sealed record PendingScanState(bool IsReady, ScanEvent? ScanEvent, Guid ScanId, int ResultCount)
{
    public static PendingScanState Ready(ScanEvent scanEvent)
    {
        return new PendingScanState(true, scanEvent, scanEvent.ScanId, scanEvent.Results.Count);
    }

    public static PendingScanState Waiting(Guid scanId, int resultCount)
    {
        return new PendingScanState(false, null, scanId, resultCount);
    }
}

internal sealed class PendingScan(Guid scanId, string? deploymentId, DateTime firstReceivedAt)
{
    public Guid ScanId { get; } = scanId;
    public string? DeploymentId { get; set; } = deploymentId;
    public DateTime FirstReceivedAt { get; } = firstReceivedAt;
    public List<ScanResult> Results { get; } = [];

    public void Upsert(ScanResult scanResult)
    {
        scanResult.ScanId = ScanId;

        var existingIndex = Results.FindIndex(result =>
            string.Equals(result.ServiceId, scanResult.ServiceId, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            Results[existingIndex] = scanResult;
            return;
        }

        Results.Add(scanResult);
    }

    public ScanEvent ToScanEvent()
    {
        return new ScanEvent
        {
            ScanId = ScanId,
            DeploymentId = DeploymentId,
            Results = Results.ToList(),
            Findings = Results.SelectMany(result => result.Findings).ToList(),
            Timestamp = FirstReceivedAt
        };
    }
}
