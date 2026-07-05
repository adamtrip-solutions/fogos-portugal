using Fogos.Domain.Events;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Queue;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Notifications;

/// <summary>
/// Enqueues a <see cref="PushNotificationRequested"/> onto the delayed dispatcher (default 3-minute
/// delay, the legacy FCM debounce) so a burst of rapid updates collapses into one late push. The
/// Worker's handler resolves the topics and delivers via <see cref="FcmNotifier"/>.
/// </summary>
public sealed class NotificationScheduler(IDelayedDispatcher delayed, IOptions<FcmOptions> fcmOptions)
{
    public Task ScheduleAsync(
        string kind,
        string? incidentId,
        string title,
        string body,
        string[] topics,
        string stream = "default",
        CancellationToken ct = default) =>
        delayed.DispatchAsync(
            new PushNotificationRequested(kind, incidentId, title, body, topics),
            fcmOptions.Value.PushDelay,
            stream,
            ct);
}
