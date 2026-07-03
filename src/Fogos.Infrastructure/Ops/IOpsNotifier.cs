namespace Fogos.Infrastructure.Ops;

/// <summary>Operational alerting channel (Discord webhooks in practice).</summary>
public interface IOpsNotifier
{
    /// <summary>General ops channel (feed freshness, parser drift, job notices).</summary>
    Task InfoAsync(string message, CancellationToken ct = default);

    /// <summary>Error channel. Always delivered when configured — errors don't respect dry-run.</summary>
    Task ErrorAsync(string message, CancellationToken ct = default);

    /// <summary>
    /// Dry-run capture channel: what a social/push publisher *would have sent*.
    /// Humans compare this against what the live platform actually posts.
    /// </summary>
    Task DryRunCaptureAsync(string channel, string payload, CancellationToken ct = default);
}
