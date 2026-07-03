namespace Fogos.Infrastructure.Queue;

/// <summary>
/// Redis Streams queue configuration. Mirrors the legacy platform's two Laravel queues:
/// a <c>default</c> stream for the hot ingestion path and a dedicated <c>icnf</c> stream for
/// the slower ICNF enrichment fan-out.
/// </summary>
public sealed class QueueOptions
{
    public const string SectionName = "Queue";

    /// <summary>Consumer group every worker joins (legacy: the queue-worker pool).</summary>
    public string ConsumerGroup { get; set; } = "workers";

    /// <summary>Streams consumed. Handlers dispatch to whichever stream an event was enqueued on.</summary>
    public string[] Streams { get; set; } = ["default", "icnf"];

    /// <summary>Delivery attempts before a message is dead-lettered (legacy retry ×3).</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// A pending (delivered-but-unacked) message older than this is reclaimed via XCLAIM and retried.
    /// Also the poll interval ceiling for the delayed-dispatch pump.
    /// </summary>
    public TimeSpan PendingReclaimAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long an idempotency marker (<see cref="IProcessedMarker"/>) is remembered.</summary>
    public TimeSpan ProcessedMarkerTtl { get; set; } = TimeSpan.FromHours(6);
}
