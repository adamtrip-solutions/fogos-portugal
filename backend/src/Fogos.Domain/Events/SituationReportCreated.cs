namespace Fogos.Domain.Events;

/// <summary>A situation report was composed and persisted. Drives its social fan-out and webhook delivery.</summary>
public sealed record SituationReportCreated(string ReportId) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}
