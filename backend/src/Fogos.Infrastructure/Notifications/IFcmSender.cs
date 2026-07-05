namespace Fogos.Infrastructure.Notifications;

/// <summary>
/// One FCM message: exactly one of a topic-condition, a single topic, or a device token; notification
/// or data-only. <see cref="Token"/> drives direct-to-device sends (alert subscriptions).
/// </summary>
public sealed record FcmSend(
    string? Condition,
    string? Topic,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data,
    bool DataOnly,
    string? Token = null);

/// <summary>
/// Thin seam over the FirebaseAdmin SDK so the notifier is testable and dry-run-able without real
/// credentials. Returns the provider message id.
/// </summary>
public interface IFcmSender
{
    Task<string> SendAsync(FcmSend message, CancellationToken ct = default);
}
