using System.Net;
using System.Security.Cryptography;
using Fogos.Domain.Devices;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Notifications;

/// <summary>
/// WebPushSender's two failure-mode paths: DryRun captures the payload to the ops channel and sends no HTTP;
/// a live 410 from the push service deletes the device and cascades its anonymous subscriptions.
/// </summary>
[Collection("fogos")]
public sealed class WebPushSenderTests(ContainerFixture fixture)
{
    [Fact]
    public async Task DryRun_captures_the_payload_and_sends_no_http()
    {
        var ops = new CapturingOps();
        var mongo = OfflineMongo();
        var sender = new WebPushSender(
            new ThrowingHttpClientFactory(), // any HTTP use would throw — proves DryRun is offline
            Options.Create(new WebPushOptions { Mode = WebPushMode.DryRun }),
            ops,
            new DeviceStore(mongo),
            mongo,
            NullLogger<WebPushSender>.Instance);

        var device = new Device
        {
            Id = "dev1",
            Platform = DevicePlatform.Web,
            PushEndpoint = "https://fcm.googleapis.com/fcm/send/abc",
            PushAuth = "auth",
        };
        await sender.SendAsync(device, new WebPushPayload("Novo incêndio", "corpo", "/?incident=X", "inc:X"));

        var (channel, payload) = Assert.Single(ops.Captured);
        Assert.Equal("webpush", channel);
        Assert.Contains("fcm.googleapis.com", payload);
        Assert.Contains("Novo incêndio", payload);
        Assert.Contains("inc:X", payload);
    }

    [SkippableFact]
    public async Task Live_410_deletes_the_device_and_cascades_anonymous_subs()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        var mongo = fixture.Factory.Services.GetRequiredService<MongoContext>();
        await mongo.Devices.DeleteManyAsync(FilterDefinition<Device>.Empty);

        var (p256dh, auth) = NewRecipientKeys();
        var device = new Device
        {
            Id = Guid.NewGuid().ToString("N"),
            Platform = DevicePlatform.Web,
            PushEndpoint = "https://fcm.googleapis.com/fcm/send/gone",
            PushP256dh = p256dh,
            PushAuth = auth,
        };
        await mongo.Devices.InsertOneAsync(device);

        // An anonymous subscription bound to the device (must be cascade-deleted on 410).
        var anonSub = new Fogos.Domain.Alerts.AlertSubscription
        {
            Kind = Fogos.Domain.Alerts.AlertSubscriptionKind.Concelho,
            Dico = "1106",
            DeviceId = device.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await mongo.AlertSubscriptions.InsertOneAsync(anonSub);

        var (vapidPublic, vapidPrivate) = NewVapidKeys();
        var sender = new WebPushSender(
            new StubHttpClientFactory(HttpStatusCode.Gone),
            Options.Create(new WebPushOptions
            {
                Mode = WebPushMode.Live,
                Subject = "mailto:test@fogos.pt",
                PublicKey = vapidPublic,
                PrivateKey = vapidPrivate,
            }),
            new CapturingOps(),
            new DeviceStore(mongo),
            mongo,
            NullLogger<WebPushSender>.Instance);

        await sender.SendAsync(device, new WebPushPayload("t", "b", "/x", "inc:X"));

        Assert.Null(await mongo.Devices.Find(Builders<Device>.Filter.Eq(x => x.Id, device.Id)).FirstOrDefaultAsync());
        Assert.Empty(await mongo.AlertSubscriptions
            .Find(Builders<Fogos.Domain.Alerts.AlertSubscription>.Filter.Eq(x => x.Id, anonSub.Id)).ToListAsync());
    }

    private static MongoContext OfflineMongo() =>
        new(new MongoClient("mongodb://localhost:1/?connectTimeoutMS=1"), Options.Create(new MongoOptions { Database = "unused" }));

    private static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A valid P-256 recipient public point (65-byte uncompressed) + a 16-byte auth secret.</summary>
    private static (string P256dh, string Auth) NewRecipientKeys()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdh.ExportParameters(false);
        var point = new byte[65];
        point[0] = 0x04;
        Buffer.BlockCopy(p.Q.X!, 0, point, 1, 32);
        Buffer.BlockCopy(p.Q.Y!, 0, point, 33, 32);
        return (Base64Url(point), Base64Url(RandomNumberGenerator.GetBytes(16)));
    }

    private static (string Public, string Private) NewVapidKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(true);
        var point = new byte[65];
        point[0] = 0x04;
        Buffer.BlockCopy(p.Q.X!, 0, point, 1, 32);
        Buffer.BlockCopy(p.Q.Y!, 0, point, 33, 32);
        return (Base64Url(point), Base64Url(p.D!));
    }

    private sealed class CapturingOps : IOpsNotifier
    {
        public List<(string Channel, string Payload)> Captured { get; } = [];
        public Task InfoAsync(string message, CancellationToken ct = default) => Task.CompletedTask;
        public Task ErrorAsync(string message, CancellationToken ct = default) => Task.CompletedTask;
        public Task DryRunCaptureAsync(string channel, string payload, CancellationToken ct = default)
        {
            Captured.Add((channel, payload));
            return Task.CompletedTask;
        }
    }

    private sealed class StubHttpClientFactory(HttpStatusCode status) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(status));
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("HTTP must not be used in DryRun.");
    }
}
