using Fogos.Domain.Events;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Queue;

namespace Fogos.Worker.Notifications;

/// <summary>
/// Delivers a delayed <see cref="PushNotificationRequested"/> via <see cref="FcmNotifier"/>. The
/// delayed dispatcher already applied the debounce window, so by the time this runs the push is due.
/// </summary>
public sealed class PushNotificationHandler(FcmNotifier fcm) : IEventHandler<PushNotificationRequested>
{
    public Task HandleAsync(PushNotificationRequested evt, CancellationToken ct)
    {
        var data = new Dictionary<string, string>
        {
            ["click_action"] = "FLUTTER_NOTIFICATION_CLICK",
            ["kind"] = evt.Kind,
        };
        if (!string.IsNullOrEmpty(evt.IncidentId))
            data["fireId"] = evt.IncidentId;

        return fcm.SendNotificationAsync(evt.Title, evt.Body, evt.Topics, data, ct);
    }
}
