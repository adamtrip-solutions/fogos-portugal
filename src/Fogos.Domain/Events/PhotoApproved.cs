namespace Fogos.Domain.Events;

/// <summary>
/// A moderator approved a photo for publication (<c>publish = true</c>). Triggers the social/push fan-out
/// in the Worker (mirrors the legacy <c>PhotoModerationController::approve</c> broadcast). The handler
/// re-fetches the photo and incident before acting.
/// </summary>
public sealed record PhotoApproved(string PhotoId, string IncidentId) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
}
