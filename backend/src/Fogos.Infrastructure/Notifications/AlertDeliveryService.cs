using Fogos.Domain.Alerts;
using Fogos.Domain.Devices;
using Fogos.Infrastructure.Reads;
using Microsoft.Extensions.Logging;

namespace Fogos.Infrastructure.Notifications;

/// <summary>
/// The push side of the alert pipeline. Called by the matcher handlers ONLY after
/// <c>AlertEventStore.TryAppendAsync</c> won the dedupe insert — so a subscriber is pushed exactly once per
/// alert. Resolves the subscription's device and dispatches to the platform channel (today: Web Push).
/// Never throws — delivery failures must not fail the handler that recorded the alert.
/// </summary>
public sealed class AlertDeliveryService(
    DeviceReads devices,
    WebPushSender webPush,
    ILogger<AlertDeliveryService> logger)
{
    /// <summary>
    /// Delivers one alert to the device behind <paramref name="sub"/>, if any. No-op when the subscription
    /// has no device or the device is disabled/unknown.
    /// </summary>
    public async Task DeliverAsync(
        AlertSubscription sub, string dedupeKey, string kind, string message, string url, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(sub.DeviceId))
                return;

            var device = await devices.GetByIdAsync(sub.DeviceId, ct);
            if (device is null || device.Disabled)
                return;

            var payload = new WebPushPayload(WebPushCopy.Title(kind), message, url, dedupeKey);

            switch (device.Platform)
            {
                case DevicePlatform.Web:
                    await webPush.SendAsync(device, payload, ct);
                    break;
                // Ios/Android are reserved for the Expo mobile plan (N1) — no channel wired yet.
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Alert delivery failed for subscription {SubscriptionId}", sub.Id);
        }
    }
}
