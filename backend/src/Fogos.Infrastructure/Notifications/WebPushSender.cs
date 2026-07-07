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
/// Sends Web Push messages (aes128gcm, VAPID) via <c>Lib.Net.Http.WebPush</c>. Mirrors the webhook delivery
/// posture: DryRun captures ONE aggregated summary per batch to the ops channel (a popular incident must not
/// flood Discord) and sends no HTTP; Live POSTs with bounded parallelism and treats failures like webhooks —
/// a 404/410 means the subscription is gone at the push service (delete the device and cascade its anonymous
/// subs), any other failure bumps <c>FailureCount</c> and disables the device at the threshold with an ops
/// notice. Never throws out of a send: a bad device must not fail the matcher handler. Logs and ops notices
/// reference devices by endpoint host + a short id prefix, never the full capability id.
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

    private const int MaxParallelSends = 8;

    // Relaxed escaping so European-Portuguese copy ("incêndio") stays legible on the wire and in the
    // dry-run ops capture; the payload is our own trusted copy rendered by our service worker.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Single-send convenience over <see cref="SendManyAsync"/>.</summary>
    public Task SendAsync(Device device, WebPushPayload payload, CancellationToken ct = default) =>
        SendManyAsync([(device, payload)], ct);

    /// <summary>
    /// Delivers a batch: DryRun → one aggregated ops capture, no HTTP; Live → parallel sends bounded at
    /// <see cref="MaxParallelSends"/>, each isolated (one bad device never affects its siblings).
    /// </summary>
    public async Task SendManyAsync(IReadOnlyList<(Device Device, WebPushPayload Payload)> sends, CancellationToken ct = default)
    {
        if (sends.Count == 0)
            return;

        var o = options.Value;
        if (o.Mode == WebPushMode.DryRun)
        {
            await CaptureDryRunAsync(sends, ct);
            return;
        }

        if (!o.IsConfigured || string.IsNullOrWhiteSpace(o.PrivateKey))
        {
            logger.LogWarning("WebPush mode is Live but VAPID keys are not configured; skipping delivery.");
            return;
        }

        await Parallel.ForEachAsync(
            sends,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelSends, CancellationToken = ct },
            async (item, token) => await SendOneAsync(item.Device, item.Payload, o, token));
    }

    /// <summary>One ops post per batch: a single payload verbatim, or a count + host histogram + sample.</summary>
    private async Task CaptureDryRunAsync(IReadOnlyList<(Device Device, WebPushPayload Payload)> sends, CancellationToken ct)
    {
        var sample = SerializeBody(sends[0].Payload);
        string message;
        if (sends.Count == 1)
        {
            message = $"{Host(sends[0].Device.PushEndpoint)} · {sample}";
        }
        else
        {
            var hosts = string.Join(", ", sends
                .GroupBy(s => Host(s.Device.PushEndpoint), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} ×{g.Count()}"));
            message = $"×{sends.Count} · {sends[0].Payload.Title} · hosts: {hosts} · sample: {sample}";
        }

        await ops.DryRunCaptureAsync("webpush", message, ct);
    }

    private async Task SendOneAsync(Device device, WebPushPayload payload, WebPushOptions o, CancellationToken ct)
    {
        try
        {
            var subscription = new PushSubscription { Endpoint = device.PushEndpoint };
            subscription.SetKey(PushEncryptionKeyName.P256DH, device.PushP256dh ?? "");
            subscription.SetKey(PushEncryptionKeyName.Auth, device.PushAuth);

            using var auth = new VapidAuthentication(o.PublicKey!, o.PrivateKey!)
            {
                Subject = o.Subject ?? "mailto:admin@fogos.pt",
            };
            var client = new PushServiceClient(httpFactory.CreateClient(HttpClientName))
            {
                DefaultAuthentication = auth,
            };

            await client.RequestPushMessageDeliveryAsync(subscription, new PushMessage(SerializeBody(payload)), ct);

            // Success: clear a lingering failure counter.
            if (device.FailureCount > 0)
                await mongo.Devices.UpdateOneAsync(
                    Builders<Device>.Filter.Eq(x => x.Id, device.Id),
                    Builders<Device>.Update.Set(x => x.FailureCount, 0), cancellationToken: ct);
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // The subscription no longer exists at the push service — drop the device and its anonymous subs.
            logger.LogInformation(
                "WebPush endpoint gone ({Status}); removing device {Device}", (int)ex.StatusCode, Describe(device));
            await deviceStore.DeleteWithCascadeAsync(device.Id, ct);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown / handler cancellation — not a device failure; don't count it against the device.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WebPush delivery failed for device {Device}", Describe(device));
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
                $"🔕 Dispositivo Web Push desativado após {updated.FailureCount} falhas consecutivas ({Describe(device)}).", ct);
        }
    }

    private static string SerializeBody(WebPushPayload payload) =>
        JsonSerializer.Serialize(
            new { title = payload.Title, body = payload.Body, url = payload.Url, tag = payload.Tag }, Json);

    private static string Host(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : "?";

    /// <summary>
    /// Log/ops-safe device reference: endpoint host plus a 6-char id prefix for correlation — the device id
    /// is a capability, so it must never appear in full in logs or the ops channel.
    /// </summary>
    private static string Describe(Device device) =>
        $"{Host(device.PushEndpoint)}·{device.Id[..Math.Min(6, device.Id.Length)]}";
}
