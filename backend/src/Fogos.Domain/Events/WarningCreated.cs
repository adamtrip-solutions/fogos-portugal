using Fogos.Domain.Warnings;

namespace Fogos.Domain.Events;

/// <summary>A broadcast warning was issued. The kind selects the fan-out (manual / AGIF / site).</summary>
public sealed record WarningCreated(string WarningId, WarningKind Kind) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}
