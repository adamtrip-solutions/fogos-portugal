using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Fogos.Domain.Devices;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Options;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Notifications;

/// <summary>The click-through payload delivered to a browser: title + body, a deep link, and a collapse tag.</summary>
public sealed record WebPushPayload(string Title, string Body, string Url, string Tag);

/// <summary>
/// Sends a single Web Push (aes128gcm, VAPID) via <c>Lib.Net.Http.WebPush</c>. Mirrors the webhook delivery
/// posture: DryRun captures the payload to the ops channel and sends no HTTP; Live POSTs and treats
/// failures like webhooks — a 404/410 means the subscription is gone at the push service (delete the device
/// and cascade its anonymous subs), any other failure bumps <c>FailureCount</c> and disables the device at
/// the threshold with an ops notice. Never throws: a bad device must not fail the matcher handler.
/// </summary>
public sealed class WebPushSender(
    IHttpClientFactory httpFactory,
    IOptions<WebPushOptions> options,
    IOpsNotifier ops,
    DeviceStore deviceStore,
    MongoContext mongo,
    ILogger<WebPushSender> logger)
{
    public const string HttpClientName = "web-push";

    // Relaxed escaping so European-Portuguese copy ("incêndio") stays legible on the wire and in the
    // dry-run ops capture; the payload is our own trusted copy rendered by our service worker.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task SendAsync(Device device, WebPushPayload payload, CancellationToken ct = default)
    {
        var o = options.Value;
        var body = JsonSerializer.Serialize(
            new { title = payload.Title, body = payload.Body, url = payload.Url, tag = payload.Tag }, Json);

        if (o.Mode == WebPushMode.DryRun)
        {
            var host = Uri.TryCreate(device.PushEndpoint, UriKind.Absolute, out var uri) ? uri.Host : "?";
            await ops.DryRunCaptureAsync("webpush", $"{host} · {body}", ct);
            return;
        }

        if (!o.IsConfigured || string.IsNullOrWhiteSpace(o.PrivateKey))
        {
            logger.LogWarning("WebPush mode is Live but VAPID keys are not configured; skipping delivery.");
            return;
        }

        try
        {
            var subscription = new PushSubscription { Endpoint = device.PushEndpoint };
            subscription.SetKey(PushEncryptionKeyName.P256DH, device.PushP256dh ?? "");
            subscription.SetKey(PushEncryptionKeyName.Auth, device.PushAuth);

            var client = new PushServiceClient(httpFactory.CreateClient(HttpClientName))
            {
                DefaultAuthentication = new VapidAuthentication(o.PublicKey!, o.PrivateKey!)
                {
                    Subject = o.Subject ?? "mailto:admin@fogos.pt",
                },
            };

            await client.RequestPushMessageDeliveryAsync(subscription, new PushMessage(body), ct);

            // Success: clear a lingering failure counter.
            if (device.FailureCount > 0)
                await mongo.Devices.UpdateOneAsync(
                    Builders<Device>.Filter.Eq(x => x.Id, device.Id),
                    Builders<Device>.Update.Set(x => x.FailureCount, 0), cancellationToken: ct);
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // The subscription no longer exists at the push service — drop the device and its anonymous subs.
            logger.LogInformation("WebPush endpoint gone ({Status}); removing device {DeviceId}", (int)ex.StatusCode, device.Id);
            await deviceStore.DeleteWithCascadeAsync(device.Id, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WebPush delivery failed for device {DeviceId}", device.Id);
            await RegisterFailureAsync(device, o, ct);
        }
    }

    private async Task RegisterFailureAsync(Device device, WebPushOptions o, CancellationToken ct)
    {
        var updated = await mongo.Devices.FindOneAndUpdateAsync(
            Builders<Device>.Filter.Eq(x => x.Id, device.Id),
            Builders<Device>.Update.Inc(x => x.FailureCount, 1),
            new FindOneAndUpdateOptions<Device> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is not null && !updated.Disabled && updated.FailureCount >= o.DisableThreshold)
        {
            await mongo.Devices.UpdateOneAsync(
                Builders<Device>.Filter.Eq(x => x.Id, device.Id),
                Builders<Device>.Update.Set(x => x.Disabled, true), cancellationToken: ct);
            await ops.InfoAsync(
                $"🔕 Dispositivo Web Push desativado após {updated.FailureCount} falhas consecutivas (device {device.Id}).", ct);
        }
    }
}
