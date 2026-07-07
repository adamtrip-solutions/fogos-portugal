using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fogos.Domain.Alerts;
using Fogos.Domain.Devices;
using Fogos.Domain.Locations;
using Fogos.Domain.Users;
using Fogos.Infrastructure.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Alerts;

/// <summary>
/// The Web Push device surface driven through GraphQL: registration (happy path + key-gated upsert), the
/// WEB_PUSH_DISABLED guard when unconfigured, binding a subscription to a device (including the owner guard),
/// the deviceSubscriptions capability query, and delete-with-cascade.
/// </summary>
[Collection("fogos")]
public sealed class WebPushDeviceTests(ContainerFixture fixture)
{
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/happy-path";
    private const string Azp = "https://app.fogos.pt";

    // Spec-shaped keys: p256dh = 0x04 ‖ 64 bytes (uncompressed P-256 point), auth = 16 bytes.
    private static readonly string P256 = B64Url([0x04, .. Enumerable.Range(1, 64).Select(i => (byte)i)]);
    private static readonly string OtherP256 = B64Url([0x04, .. Enumerable.Range(101, 64).Select(i => (byte)i)]);
    private static readonly string Auth = B64Url([.. Enumerable.Range(1, 16).Select(i => (byte)i)]);
    private static readonly string OtherAuth = B64Url([.. Enumerable.Range(51, 16).Select(i => (byte)i)]);

    private const string RegisterMutation = """
        mutation($input: RegisterWebPushDeviceInput!) {
          registerWebPushDevice(input: $input) { id }
        }
        """;

    private const string DeleteMutation = "mutation($e: String!) { deleteWebPushDevice(endpoint: $e) }";

    private const string CreateSubMutation = """
        mutation($input: CreateAlertSubscriptionInput!) {
          createAlertSubscription(input: $input) { id deviceId }
        }
        """;

    [SkippableFact]
    public async Task Register_upsert_is_key_gated()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var factory = ConfiguredFactory();

        var first = await PostAsync(factory, RegisterMutation, new { input = new { endpoint = Endpoint, p256dh = P256, auth = Auth } });
        var id1 = first.RootElement.GetProperty("data").GetProperty("registerWebPushDevice").GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id1));

        // Same endpoint, same p256dh (a legitimate browser re-registration) → same device id.
        var second = await PostAsync(factory, RegisterMutation, new { input = new { endpoint = Endpoint, p256dh = P256, auth = OtherAuth } });
        var id2 = second.RootElement.GetProperty("data").GetProperty("registerWebPushDevice").GetProperty("id").GetString();
        Assert.Equal(id1, id2);

        // Same endpoint but a DIFFERENT p256dh: an endpoint-only caller must get a generic error —
        // no device id leaked, nothing overwritten.
        var hijack = await PostAsync(factory, RegisterMutation, new { input = new { endpoint = Endpoint, p256dh = OtherP256, auth = Auth } });
        Assert.Equal("INVALID_PUSH_SUBSCRIPTION", ErrorCode(hijack));

        var ctx = fixture.Factory.Services.GetRequiredService<MongoContext>();
        var device = await ctx.Devices.Find(FilterDefinition<Device>.Empty).SingleAsync();
        Assert.Equal(P256, device.PushP256dh); // keys untouched by the rejected attempt
    }

    [SkippableFact]
    public async Task Register_when_web_push_unconfigured_errors_disabled()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        // The default fixture factory has no WebPush:PublicKey configured.
        using var doc = await fixture.GraphQLAsync(RegisterMutation,
            new { input = new { endpoint = Endpoint, p256dh = P256, auth = Auth } });
        Assert.Equal("WEB_PUSH_DISABLED", ErrorCode(doc));
    }

    [SkippableFact]
    public async Task Register_rejects_an_off_allowlist_endpoint()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var factory = ConfiguredFactory();

        var doc = await PostAsync(factory, RegisterMutation,
            new { input = new { endpoint = "https://push.evil.example/x", p256dh = P256, auth = Auth } });
        Assert.Equal("WEB_PUSH_ENDPOINT_NOT_ALLOWED", ErrorCode(doc));
    }

    [SkippableFact]
    public async Task CreateAlertSubscription_binds_a_device_and_deviceSubscriptions_lists_it()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var factory = ConfiguredFactory();

        var reg = await PostAsync(factory, RegisterMutation, new { input = new { endpoint = Endpoint, p256dh = P256, auth = Auth } });
        var deviceId = reg.RootElement.GetProperty("data").GetProperty("registerWebPushDevice").GetProperty("id").GetString();

        var created = await PostAsync(factory, CreateSubMutation,
            new { input = new { kind = "CONCELHO", dico = "1106", deviceId } });
        var node = created.RootElement.GetProperty("data").GetProperty("createAlertSubscription");
        Assert.Equal(deviceId, node.GetProperty("deviceId").GetString());

        const string deviceSubs = "query($id: ID!) { deviceSubscriptions(deviceId: $id) { id deviceId } }";
        var listed = await PostAsync(factory, deviceSubs, new { id = deviceId });
        var subs = listed.RootElement.GetProperty("data").GetProperty("deviceSubscriptions");
        Assert.Equal(1, subs.GetArrayLength());
        Assert.Equal(deviceId, subs[0].GetProperty("deviceId").GetString());

        // Unknown / disabled device is rejected.
        var bad = await PostAsync(factory, CreateSubMutation, new { input = new { kind = "CONCELHO", dico = "1106", deviceId = "deadbeef" } });
        Assert.Equal("DEVICE_NOT_FOUND", ErrorCode(bad));
    }

    [SkippableFact]
    public async Task Owned_device_binds_only_for_its_owner_and_mismatch_reads_as_not_found()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        await using var clerk = await FakeClerk.StartAsync();
        var factory = ClerkConfiguredFactory(clerk);
        var alice = clerk.Mint(sub: "user_alice", email: "alice@fogos.pt", name: "Alice", azp: Azp);
        var bob = clerk.Mint(sub: "user_bob", email: "bob@fogos.pt", name: "Bob", azp: Azp);

        // Alice registers signed-in → the device is owned by her local user.
        var reg = await PostAsync(factory, RegisterMutation,
            new { input = new { endpoint = Endpoint, p256dh = P256, auth = Auth } }, alice);
        var deviceId = reg.RootElement.GetProperty("data").GetProperty("registerWebPushDevice").GetProperty("id").GetString();

        var ctx = fixture.Factory.Services.GetRequiredService<MongoContext>();
        var device = await ctx.Devices.Find(FilterDefinition<Device>.Empty).SingleAsync();
        Assert.False(string.IsNullOrEmpty(device.OwnerUserId));

        var input = new { input = new { kind = "CONCELHO", dico = "1106", deviceId } };

        // Bob presenting Alice's device id gets exactly the not-found error (no existence oracle).
        var byBob = await PostAsync(factory, CreateSubMutation, input, bob);
        Assert.Equal("DEVICE_NOT_FOUND", ErrorCode(byBob));

        // Anonymous callers too.
        var byAnon = await PostAsync(factory, CreateSubMutation, input);
        Assert.Equal("DEVICE_NOT_FOUND", ErrorCode(byAnon));

        // The owner binds fine.
        var byAlice = await PostAsync(factory, CreateSubMutation, input, alice);
        Assert.Equal(deviceId, byAlice.RootElement.GetProperty("data").GetProperty("createAlertSubscription")
            .GetProperty("deviceId").GetString());
    }

    [SkippableFact]
    public async Task Delete_cascades_anonymous_subs_and_nulls_owned_ones()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var factory = ConfiguredFactory();
        var ctx = fixture.Factory.Services.GetRequiredService<MongoContext>();

        var reg = await PostAsync(factory, RegisterMutation, new { input = new { endpoint = Endpoint, p256dh = P256, auth = Auth } });
        var deviceId = reg.RootElement.GetProperty("data").GetProperty("registerWebPushDevice").GetProperty("id").GetString()!;

        // Anonymous sub bound to the device → cascade-deleted.
        var anon = new AlertSubscription { Kind = AlertSubscriptionKind.Concelho, Dico = "1106", DeviceId = deviceId, CreatedAt = DateTimeOffset.UtcNow };
        // Owned sub bound to the device → survives with DeviceId cleared.
        var owned = new AlertSubscription { Kind = AlertSubscriptionKind.Concelho, Dico = "1106", DeviceId = deviceId, OwnerUserId = "user_x", CreatedAt = DateTimeOffset.UtcNow };
        await ctx.AlertSubscriptions.InsertManyAsync([anon, owned]);

        var del = await PostAsync(factory, DeleteMutation, new { e = Endpoint });
        Assert.True(del.RootElement.GetProperty("data").GetProperty("deleteWebPushDevice").GetBoolean());

        // Device gone.
        Assert.Equal(0, await ctx.Devices.CountDocumentsAsync(FilterDefinition<Device>.Empty));
        // Anonymous sub gone.
        Assert.Null(await ctx.AlertSubscriptions.Find(Builders<AlertSubscription>.Filter.Eq(x => x.Id, anon.Id)).FirstOrDefaultAsync());
        // Owned sub survives, DeviceId cleared.
        var survivor = await ctx.AlertSubscriptions.Find(Builders<AlertSubscription>.Filter.Eq(x => x.Id, owned.Id)).FirstOrDefaultAsync();
        Assert.NotNull(survivor);
        Assert.Null(survivor!.DeviceId);

        // Deleting an unknown endpoint returns false.
        var again = await PostAsync(factory, DeleteMutation, new { e = Endpoint });
        Assert.False(again.RootElement.GetProperty("data").GetProperty("deleteWebPushDevice").GetBoolean());
    }

    private static Dictionary<string, string?> WebPushConfig() => new()
    {
        ["WebPush:PublicKey"] = "BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkTrsT5Y6YpXY1234567890abcdefABCDEF-_",
        ["WebPush:Subject"] = "mailto:test@fogos.pt",
        ["WebPush:Mode"] = "DryRun",
        ["WebPush:RegisterPerIpPerMinute"] = "100000",
        ["WebPush:RegisterPerIpPerDay"] = "100000",
    };

    private WebApplicationFactory<Program> ConfiguredFactory() =>
        fixture.CreateFactory(WebPushConfig());

    private WebApplicationFactory<Program> ClerkConfiguredFactory(FakeClerk clerk)
    {
        var config = WebPushConfig();
        config["Clerk:Authority"] = clerk.Authority;
        config["Clerk:JwksUrl"] = clerk.JwksUrl;
        config["Clerk:AuthorizedParties:0"] = Azp;
        config["Clerk:JwksCacheMinutes"] = "60";
        return fixture.CreateFactory(config);
    }

    private static async Task<JsonDocument> PostAsync(
        WebApplicationFactory<Program> factory, string query, object variables, string? bearer = null)
    {
        var client = factory.CreateClient();
        if (bearer is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static string? ErrorCode(JsonDocument doc) =>
        doc.RootElement.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString();

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task ResetAsync()
    {
        var ctx = fixture.Factory.Services.GetRequiredService<MongoContext>();
        await ctx.Devices.DeleteManyAsync(FilterDefinition<Device>.Empty);
        await ctx.AlertSubscriptions.DeleteManyAsync(FilterDefinition<AlertSubscription>.Empty);
        await ctx.Users.DeleteManyAsync(FilterDefinition<User>.Empty);
        await ctx.Locations.DeleteManyAsync(FilterDefinition<Location>.Empty);
        await ctx.Locations.InsertOneAsync(new Location { Level = LocationLevel.Concelho, Code = "1106", Name = "LISBOA", Dico = "1106" });
        // Ensure the unique pushEndpoint index exists (idempotent; same name as app init).
        await ctx.Devices.Indexes.CreateOneAsync(new CreateIndexModel<Device>(
            Builders<Device>.IndexKeys.Ascending("pushEndpoint"),
            new CreateIndexOptions { Unique = true, Name = "pushEndpoint" }));
    }
}
