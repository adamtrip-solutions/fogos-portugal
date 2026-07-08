using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fogos.Domain.Alerts;
using Fogos.Domain.Auth;
using Fogos.Domain.Devices;
using Fogos.Domain.Locations;
using Fogos.Infrastructure.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Alerts;

/// <summary>
/// The mobile app device-credential surface: <c>registerAppDevice</c> mints a device-bound credential that
/// authenticates the App rate-limit tier via <c>X-Device-Key</c>; invalid/garbage/revoked credentials hard-401
/// (never downgrading to anonymous); a device caller may mutate (proving the read-only-API-key guard leaves it
/// alone) but only its own subscriptions; the registration gate is IP-limited; and an X-API-Key wins over a
/// simultaneously-presented X-Device-Key.
/// </summary>
[Collection("fogos")]
public sealed class AppDeviceTests(ContainerFixture fixture)
{
    private const string RegisterMutation = """
        mutation($input: RegisterAppDeviceInput!) {
          registerAppDevice(input: $input) { deviceId deviceSecret }
        }
        """;

    private const string CreateSubMutation = """
        mutation($input: CreateAlertSubscriptionInput!) {
          createAlertSubscription(input: $input) { id deviceId }
        }
        """;

    private const string DeleteSubMutation = "mutation($id: ID!) { deleteAlertSubscription(id: $id) }";

    [SkippableFact]
    public async Task Register_returns_credential_that_authenticates_a_query_as_app_tier()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        var (deviceId, secret) = await RegisterAsync(fixture.Factory, "IOS", model: "iPhone15,2", appVersion: "1.0.0");
        Assert.False(string.IsNullOrEmpty(deviceId));
        Assert.False(string.IsNullOrEmpty(secret));

        // The secret is never stored in plaintext; the device is an app device with no web-push endpoint.
        var ctx = SeedData.Context(fixture);
        var device = await ctx.Devices.Find(Builders<Device>.Filter.Eq(x => x.Id, deviceId)).SingleAsync();
        Assert.Equal(DevicePlatform.Ios, device.Platform);
        Assert.Null(device.PushEndpoint);
        Assert.False(string.IsNullOrEmpty(device.SecretHash));
        Assert.NotEqual(secret, device.SecretHash);
        Assert.Equal("iPhone15,2", device.Model);

        // The returned credential immediately authenticates a query over X-Device-Key.
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Key", DeviceKey(deviceId, secret));
        var response = await client.PostAsJsonAsync("/graphql", new { query = "{ __typename }" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        Assert.Equal("Query", doc.RootElement.GetProperty("data").GetProperty("__typename").GetString());
    }

    [SkippableFact]
    public async Task Invalid_garbage_or_revoked_device_key_is_401_and_not_anonymous()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        var (deviceId, _) = await RegisterAsync(fixture.Factory, "ANDROID");

        // Garbage / malformed header → hard 401 (a presented credential never becomes anonymous).
        await AssertDeviceUnauthenticated("not-a-device-key");
        await AssertDeviceUnauthenticated("fdv1.deadbeefdeadbeef.wrongsecret");
        // Well-formed for a real device but the WRONG secret → 401.
        await AssertDeviceUnauthenticated(DeviceKey(deviceId, "fgs_live_totally_wrong_secret_value_here"));

        // Revoked device → 401. Use a fresh device that has never been presented (so the resolver's ≤60s
        // cache has no pre-revocation entry): revoke it before its first presentation.
        var (revokedId, revokedSecret) = await RegisterAsync(fixture.Factory, "IOS");
        var ctx = SeedData.Context(fixture);
        await ctx.Devices.UpdateOneAsync(
            Builders<Device>.Filter.Eq(x => x.Id, revokedId),
            Builders<Device>.Update.Set(x => x.Revoked, true));
        await AssertDeviceUnauthenticated(DeviceKey(revokedId, revokedSecret));
    }

    [SkippableFact]
    public async Task Device_caller_can_mutate_but_only_its_own_subscriptions()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        var (deviceA, secretA) = await RegisterAsync(fixture.Factory, "IOS");
        var (deviceB, secretB) = await RegisterAsync(fixture.Factory, "ANDROID");

        // Device A creates a subscription — proves the read-only-API-key guard does NOT hit device callers
        // (ClientId is null), and the subscription is bound to A's device id.
        var created = await PostWithDeviceKeyAsync(CreateSubMutation,
            new { input = new { kind = "CONCELHO", dico = "1106" } }, DeviceKey(deviceA, secretA));
        var node = created.RootElement.GetProperty("data").GetProperty("createAlertSubscription");
        var subId = node.GetProperty("id").GetString()!;
        Assert.Equal(deviceA, node.GetProperty("deviceId").GetString());

        // Device B cannot delete device A's subscription.
        var byB = await PostWithDeviceKeyAsync(DeleteSubMutation, new { id = subId }, DeviceKey(deviceB, secretB));
        Assert.False(byB.RootElement.GetProperty("data").GetProperty("deleteAlertSubscription").GetBoolean());

        // It still exists.
        var ctx = SeedData.Context(fixture);
        Assert.NotNull(await ctx.AlertSubscriptions.Find(Builders<AlertSubscription>.Filter.Eq(x => x.Id, subId)).FirstOrDefaultAsync());

        // Device A can delete its own.
        var byA = await PostWithDeviceKeyAsync(DeleteSubMutation, new { id = subId }, DeviceKey(deviceA, secretA));
        Assert.True(byA.RootElement.GetProperty("data").GetProperty("deleteAlertSubscription").GetBoolean());
    }

    [SkippableFact]
    public async Task Device_key_resolves_the_app_tier_rate_limit()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        await fixture.FlushRedisAsync();

        // App tier request limit of 3; anonymous effectively unlimited. If the caller were resolved as
        // anonymous the 4th request would still pass — a 429 proves it is the App tier that applied.
        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimit:Enabled"] = "true",
            ["RateLimit:App:Requests"] = "3",
            ["RateLimit:Anonymous:Requests"] = "1000000",
        });

        var (deviceId, secret) = await RegisterAsync(factory, "IOS");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Key", DeviceKey(deviceId, secret));

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v3/incidents/active.geojson")).StatusCode);

        var blocked = await client.GetAsync("/v3/incidents/active.geojson");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [SkippableFact]
    public async Task Registration_is_ip_gated()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        await fixture.FlushRedisAsync();

        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["WebPush:RegisterPerIpPerMinute"] = "2",
            ["WebPush:RegisterPerIpPerDay"] = "100000",
        });

        // Two registrations within the per-minute window succeed; the third trips the gate.
        Assert.Null(RegisterErrorCode(await PostAsync(factory, RegisterMutation, Input("IOS"))));
        Assert.Null(RegisterErrorCode(await PostAsync(factory, RegisterMutation, Input("IOS"))));
        Assert.Equal("RATE_LIMITED", RegisterErrorCode(await PostAsync(factory, RegisterMutation, Input("IOS"))));
    }

    [SkippableFact]
    public async Task ApiKey_wins_when_both_an_api_key_and_a_device_key_are_presented()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        var (deviceId, secret) = await RegisterAsync(fixture.Factory, "IOS");

        // A read-only (Registered, scope-less) API key: its mutations are rejected with API_KEY_READ_ONLY.
        const string apiKey = "fgs_live_readonly_precedence_key";
        await SeedData.InsertApiKeyAsync(fixture, apiKey, ApiTier.Registered, name: "precedence probe");

        // Present BOTH headers on a mutation. If the device key won, the mutation would succeed (App tier is
        // not read-only). It is rejected as read-only → the API key took precedence.
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        client.DefaultRequestHeaders.Add("X-Device-Key", DeviceKey(deviceId, secret));
        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = CreateSubMutation,
            variables = new { input = new { kind = "CONCELHO", dico = "1106" } },
        });
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("API_KEY_READ_ONLY", ErrorCode(doc));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static object Input(string platform) => new { input = new { platform } };

    private static string DeviceKey(string deviceId, string secret) => $"fdv1.{deviceId}.{secret}";

    private async Task<(string DeviceId, string Secret)> RegisterAsync(
        WebApplicationFactory<Program> factory, string platform, string? model = null, string? appVersion = null)
    {
        var doc = await PostAsync(factory, RegisterMutation,
            new { input = new { platform, model, appVersion } });
        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        var node = doc.RootElement.GetProperty("data").GetProperty("registerAppDevice");
        return (node.GetProperty("deviceId").GetString()!, node.GetProperty("deviceSecret").GetString()!);
    }

    private async Task AssertDeviceUnauthenticated(string deviceKey)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Key", deviceKey);
        var response = await client.PostAsJsonAsync("/graphql", new { query = "{ __typename }" });

        // A hard 401 — not a 200 that silently fell through to the anonymous tier.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DEVICE_UNAUTHENTICATED", doc.RootElement.GetProperty("error").GetString());
    }

    private async Task<JsonDocument> PostWithDeviceKeyAsync(string query, object variables, string deviceKey)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Key", deviceKey);
        var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

    private static async Task<JsonDocument> PostAsync(
        WebApplicationFactory<Program> factory, string query, object variables)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

    private static string? ErrorCode(JsonDocument doc) =>
        doc.RootElement.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString();

    private static string? RegisterErrorCode(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("errors", out var errors)
            ? errors[0].GetProperty("extensions").GetProperty("code").GetString()
            : null;

    private async Task ResetAsync()
    {
        var ctx = SeedData.Context(fixture);
        await ctx.Devices.DeleteManyAsync(FilterDefinition<Device>.Empty);
        await ctx.AlertSubscriptions.DeleteManyAsync(FilterDefinition<AlertSubscription>.Empty);
        await ctx.ApiClients.DeleteManyAsync(FilterDefinition<ApiClient>.Empty);
        await ctx.Locations.DeleteManyAsync(FilterDefinition<Location>.Empty);
        await ctx.Locations.InsertOneAsync(new Location { Level = LocationLevel.Concelho, Code = "1106", Name = "LISBOA", Dico = "1106" });
    }
}
