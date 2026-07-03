namespace Fogos.Infrastructure.Notifications;

/// <summary>One FCM message: either a topic-condition or a single topic, notification or data-only.</summary>
public sealed record FcmSend(
    string? Condition,
    string? Topic,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data,
    bool DataOnly);

/// <summary>
/// Thin seam over the FirebaseAdmin SDK so the notifier is testable and dry-run-able without real
/// credentials. Returns the provider message id.
/// </summary>
public interface IFcmSender
{
    Task<string> SendAsync(FcmSend message, CancellationToken ct = default);
}
