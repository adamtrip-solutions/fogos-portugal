namespace Fogos.Domain.Events;

/// <summary>
/// The RCM (IPMA fire-risk) daily ingest finished and <c>rcm_daily</c> is up to date for
/// <paramref name="ForecastDate"/>. Drives risk-threshold alert matching for subscriptions.
/// </summary>
public sealed record RcmProcessed(DateOnly ForecastDate) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}
