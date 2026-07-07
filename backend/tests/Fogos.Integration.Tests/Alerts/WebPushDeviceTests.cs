using System.Net.Http.Json;
using System.Text.Json;
using Fogos.Domain.Alerts;
using Fogos.Domain.Devices;
using Fogos.Domain.Locations;
using Fogos.Infrastructure.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Alerts;

/// <summary>
/// The Web Push device surface driven through GraphQL: registration (happy path + upsert), the
/// WEB_PUSH_DISABLED guard when unconfigured, binding a subscription to a device, the deviceSubscriptions
/// capability query, and delete-with-cascade.
/// </summary>
[Collection("fogos")]
public sealed class WebPushDeviceTests(ContainerFixture fixture)
{
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/happy-path";
    private const string P256 = "BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkTrsT5Y6Yg";
    private const string Auth = "k8JV6sjdbhAiUYNH";

    private const string RegisterMutation = """
        mutation($input: RegisterWebPushDeviceInput!) {
          registerWebPushDevice(input: $input) { id }
        }
        """;

    private const string DeleteMutation = "mutation($e: String!) { deleteWebPushDevice(endpoint: $e) }";

    [SkippableFact]
    public async Task Register_is_idempotent_by_endpoint_and_returns_the_same_id()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var factory = ConfiguredFactory();

        var first = await PostAsync(factory, RegisterMutation, new { input = new { endpoint = Endpoint, p256dh = P256, auth = Auth } });
        var id1 = first.RootElement.GetProperty("data").GetProperty("registerWebPushDevice").GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id1));

        // Same endpoint, refreshed keys → same device id (upsert).
        var second = await PostAsync(factory, RegisterMutation, new { input = new { endpoint = Endpoint, p256dh = P256, auth = "k8JV6sjdbhAiUYNHrefresh" } });
        var id2 = second.RootElement.GetProperty("data").GetProperty("registerWebPushDevice").GetProperty("id").GetString();
        Assert.Equal(id1, id2);

        var ctx = fixture.Factory.Services.GetRequiredService<MongoContext>();
        Assert.Equal(1, await ctx.Devices.CountDocumentsAsync(FilterDefinition<Device>.Empty));
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

        const string createSub = """
            mutation($input: CreateAlertSubscriptionInput!) {
              createAlertSubscription(input: $input) { id deviceId }
            }
            """;
        var created = await PostAsync(factory, createSub,
            new { input = new { kind = "CONCELHO", dico = "1106", deviceId } });
        var node = created.RootElement.GetProperty("data").GetProperty("createAlertSubscription");
        Assert.Equal(deviceId, node.GetProperty("deviceId").GetString());

        const string deviceSubs = "query($id: ID!) { deviceSubscriptions(deviceId: $id) { id deviceId } }";
        var listed = await PostAsync(factory, deviceSubs, new { id = deviceId });
        var subs = listed.RootElement.GetProperty("data").GetProperty("deviceSubscriptions");
        Assert.Equal(1, subs.GetArrayLength());
        Assert.Equal(deviceId, subs[0].GetProperty("deviceId").GetString());

        // Unknown / disabled device is rejected.
        var bad = await PostAsync(factory, createSub, new { input = new { kind = "CONCELHO", dico = "1106", deviceId = "deadbeef" } });
        Assert.Equal("DEVICE_NOT_FOUND", ErrorCode(bad));
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

    private WebApplicationFactory<Program> ConfiguredFactory() =>
        fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["WebPush:PublicKey"] = "BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkTrsT5Y6YpXY1234567890abcdefABCDEF-_",
            ["WebPush:Subject"] = "mailto:test@fogos.pt",
            ["WebPush:Mode"] = "DryRun",
            ["WebPush:RegisterPerIpPerMinute"] = "100000",
            ["WebPush:RegisterPerIpPerDay"] = "100000",
        });

    private static async Task<JsonDocument> PostAsync(WebApplicationFactory<Program> factory, string query, object variables)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static string? ErrorCode(JsonDocument doc) =>
        doc.RootElement.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString();

    private async Task ResetAsync()
    {
        var ctx = fixture.Factory.Services.GetRequiredService<MongoContext>();
        await ctx.Devices.DeleteManyAsync(FilterDefinition<Device>.Empty);
        await ctx.AlertSubscriptions.DeleteManyAsync(FilterDefinition<AlertSubscription>.Empty);
        await ctx.Locations.DeleteManyAsync(FilterDefinition<Location>.Empty);
        await ctx.Locations.InsertOneAsync(new Location { Level = LocationLevel.Concelho, Code = "1106", Name = "LISBOA", Dico = "1106" });
        // Ensure the unique pushEndpoint index exists (idempotent; same name as app init).
        await ctx.Devices.Indexes.CreateOneAsync(new CreateIndexModel<Device>(
            Builders<Device>.IndexKeys.Ascending("pushEndpoint"),
            new CreateIndexOptions { Unique = true, Name = "pushEndpoint" }));
    }
}
