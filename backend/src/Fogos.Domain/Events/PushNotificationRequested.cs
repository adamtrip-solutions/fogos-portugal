namespace Fogos.Domain.Events;

/// <summary>
/// A push notification the scheduler wants delivered after the standard delay (the legacy
/// 3-minute FCM debounce). Carries the fully-resolved topic list so the delayed pump and the
/// worker handler need no further lookup. <c>Kind</c> mirrors the legacy notification kinds
/// (nearby / new-incident / status-change / big-incident).
/// </summary>
public sealed record PushNotificationRequested(
    string Kind,
    string? IncidentId,
    string Title,
    string Body,
    string[] Topics) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}
