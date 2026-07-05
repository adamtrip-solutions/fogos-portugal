namespace Fogos.Domain.Events;

/// <summary>
/// A fire's committed means started growing rapidly (false→true escalation transition). Carries the
/// current and window-baseline asset counts so consumers (push, alerts) can size the response.
/// </summary>
public sealed record IncidentEscalating(string IncidentId, int Assets, int PreviousAssets) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// A rekindle was detected for a fire. <paramref name="Kind"/> is <c>STATUS_REGRESSION</c> (the fire
/// dropped back to "Em Curso") or <c>PROXIMITY</c> (a new fire near a recently closed one — then
/// <paramref name="PriorIncidentId"/> references that prior incident).
/// </summary>
public sealed record RekindleDetected(string IncidentId, string? PriorIncidentId, string Kind) : IDomainEvent
{
    public const string StatusRegression = "STATUS_REGRESSION";
    public const string Proximity = "PROXIMITY";

    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// A new ignition cluster crossed the minimum size (a fresh grouping of nearby fires ignited within the
/// window). Carries the cluster id and its member count so consumers can size the situation.
/// </summary>
public sealed record ClusterDetected(string ClusterId, int Count) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}
