using Fogos.Domain.Alerts;
using Fogos.Domain.Devices;
using Fogos.Infrastructure.Reads;
using Microsoft.Extensions.Logging;

namespace Fogos.Infrastructure.Notifications;

/// <summary>One push to make: a subscription that just won its alert_event insert, plus the alert copy.</summary>
public sealed record AlertDelivery(AlertSubscription Subscription, string DedupeKey, string Kind, string Message, string Url);

/// <summary>
/// The push side of the alert pipeline. The matcher handlers collect one <see cref="AlertDelivery"/> per
/// subscription whose <c>AlertEventStore.TryAppendAsync</c> won the dedupe insert (exactly-once) and hand the
/// whole batch here per handled event: devices are resolved in one query and sends go out as one batch (one
/// aggregated dry-run capture; bounded parallelism when live). Never throws — delivery failures must not fail
/// the handler that recorded the alerts.
/// </summary>
public sealed class AlertDeliveryService(
    DeviceReads devices,
    WebPushSender webPush,
    ILogger<AlertDeliveryService> logger)
{
    /// <summary>
    /// Delivers a batch of alerts to the devices behind their subscriptions. Subscriptions without a device,
    /// and unknown/disabled devices, are skipped.
    /// </summary>
    public async Task DeliverManyAsync(IReadOnlyList<AlertDelivery> deliveries, CancellationToken ct = default)
    {
        try
        {
            var ids = deliveries
                .Where(d => !string.IsNullOrEmpty(d.Subscription.DeviceId))
                .Select(d => d.Subscription.DeviceId!)
                .Distinct()
                .ToList();
            if (ids.Count == 0)
                return;

            var byId = await devices.GetByIdsAsync(ids, ct);

            var sends = new List<(Device Device, WebPushPayload Payload)>();
            foreach (var d in deliveries)
            {
                if (d.Subscription.DeviceId is not { Length: > 0 } id
                    || !byId.TryGetValue(id, out var device) || device.Disabled)
                    continue;
                if (device.Platform != DevicePlatform.Web)
                    continue; // Ios/Android reserved for the Expo mobile plan (N1) — no channel wired yet.

                sends.Add((device, new WebPushPayload(WebPushCopy.Title(d.Kind), d.Message, d.Url, d.DedupeKey)));
            }

            if (sends.Count > 0)
                await webPush.SendManyAsync(sends, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Alert delivery batch failed ({Count} deliveries)", deliveries.Count);
        }
    }
}
