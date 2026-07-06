using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fogos.Domain.Alerts;
using Fogos.Domain.Auth;
using Fogos.Domain.Locations;
using Fogos.Domain.Users;
using Fogos.Domain.Webhooks;
using Fogos.Infrastructure.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.Integration.Tests;

/// <summary>
/// The self-service account surface, driven end-to-end through the Clerk Bearer path (via
/// <see cref="FakeClerk"/>): minting API keys that then authenticate as Registered over <c>X-API-Key</c>,
/// the per-user key cap, owner-scoped revoke, and owned-subscription CRUD (including the owner guard that
/// stops anonymous callers deleting user-owned subscriptions).
/// </summary>
[Collection("fogos")]
public sealed class AccountTests(ContainerFixture fixture)
{
    private const string Azp = "https://app.fogos.pt";

    private const string CreateKeyMutation = """
        mutation($name: String!) {
          createApiKey(name: $name) {
            plaintextKey
            apiKey { id name keyPrefix tier createdAt revokedAt }
          }
        }
        """;

    private const string RevokeKeyMutation = "mutation($id: ID!) { revokeApiKey(id: $id) }";

    private const string CreateSubMutation = """
        mutation($input: CreateAlertSubscriptionInput!) {
          createAlertSubscription(input: $input) { id kind dico }
        }
        """;

    private const string UpdateSubMutation = """
        mutation($id: ID!, $input: CreateAlertSubscriptionInput!) {
          updateAlertSubscription(id: $id, input: $input) {
            id kind dico radiusKm riskThreshold point { latitude longitude }
          }
        }
        """;

    private const string DeleteSubMutation = "mutation($id: ID!) { deleteAlertSubscription(id: $id) }";

    [SkippableFact]
    public async Task Created_key_shows_plaintext_once_and_authenticates_as_registered()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        await using var clerk = await FakeClerk.StartAsync();
        var factory = ClerkFactory(clerk);
        var token = clerk.Mint(sub: "user_alice", email: "alice@fogos.pt", name: "Alice", azp: Azp);

        // Mint a key.
        using var created = await AsUserAsync(factory, token, CreateKeyMutation, new { name = "CLI key" });
        var node = created.RootElement.GetProperty("data").GetProperty("createApiKey");
        var plaintext = node.GetProperty("plaintextKey").GetString()!;
        var apiKey = node.GetProperty("apiKey");

        Assert.StartsWith("fgs_live_", plaintext);
        Assert.Equal(plaintext[..12], apiKey.GetProperty("keyPrefix").GetString());
        Assert.Equal("REGISTERED", apiKey.GetProperty("tier").GetString());
        Assert.Equal(JsonValueKind.Null, apiKey.GetProperty("revokedAt").ValueKind);
        var keyId = apiKey.GetProperty("id").GetString();

        // `me` lists the new key (with the display prefix, not revoked).
        using var me = await AsUserAsync(factory, token, "{ me { apiKeys { id keyPrefix revokedAt } } }");
        var keys = me.RootElement.GetProperty("data").GetProperty("me").GetProperty("apiKeys");
        Assert.Equal(1, keys.GetArrayLength());
        Assert.Equal(keyId, keys[0].GetProperty("id").GetString());

        // The plaintext authenticates as a Registered client over X-API-Key: register a webhook (client-gated).
        const string registerWebhook = """
            mutation($url: String!, $events: [String!]!) {
              registerWebhook(url: $url, events: $events) { id url }
            }
            """;
        using var wh = await fixture.GraphQLAsync(plaintext, registerWebhook,
            new { url = "https://example.com/hook", events = new[] { "incident.created" } });
        var webhookId = wh.RootElement.GetProperty("data").GetProperty("registerWebhook").GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(webhookId));

        // The webhook (joined by the key's ClientId) surfaces on `me.webhooks`, without the secret.
        using var me2 = await AsUserAsync(factory, token,
            "{ me { webhooks { id url events active consecutiveFailures createdAt } } }");
        var webhooks = me2.RootElement.GetProperty("data").GetProperty("me").GetProperty("webhooks");
        Assert.Equal(1, webhooks.GetArrayLength());
        Assert.Equal("https://example.com/hook", webhooks[0].GetProperty("url").GetString());
        Assert.False(webhooks[0].GetProperty("active").ValueKind == JsonValueKind.Null);
    }

    [SkippableFact]
    public async Task Api_key_cap_is_enforced()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        await using var clerk = await FakeClerk.StartAsync();
        var factory = ClerkFactory(clerk, maxApiKeysPerUser: 2);
        var token = clerk.Mint(sub: "user_cap", azp: Azp);

        using var first = await AsUserAsync(factory, token, CreateKeyMutation, new { name = "one" });
        Assert.False(first.RootElement.TryGetProperty("errors", out _));
        using var second = await AsUserAsync(factory, token, CreateKeyMutation, new { name = "two" });
        Assert.False(second.RootElement.TryGetProperty("errors", out _));

        using var third = await AsUserAsync(factory, token, CreateKeyMutation, new { name = "three" });
        Assert.Equal("API_KEY_LIMIT", ErrorCode(third));
    }

    [SkippableFact]
    public async Task Revoke_is_owner_scoped_and_idempotent()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        await using var clerk = await FakeClerk.StartAsync();
        var factory = ClerkFactory(clerk);
        var alice = clerk.Mint(sub: "user_alice", azp: Azp);
        var bob = clerk.Mint(sub: "user_bob", azp: Azp);

        using var created = await AsUserAsync(factory, alice, CreateKeyMutation, new { name = "alice key" });
        var keyId = created.RootElement.GetProperty("data").GetProperty("createApiKey")
            .GetProperty("apiKey").GetProperty("id").GetString();

        // Bob cannot revoke Alice's key.
        using var byBob = await AsUserAsync(factory, bob, RevokeKeyMutation, new { id = keyId });
        Assert.False(byBob.RootElement.GetProperty("data").GetProperty("revokeApiKey").GetBoolean());

        // Alice can — and a second revoke is a no-op (already revoked).
        using var byAlice = await AsUserAsync(factory, alice, RevokeKeyMutation, new { id = keyId });
        Assert.True(byAlice.RootElement.GetProperty("data").GetProperty("revokeApiKey").GetBoolean());

        using var again = await AsUserAsync(factory, alice, RevokeKeyMutation, new { id = keyId });
        Assert.False(again.RootElement.GetProperty("data").GetProperty("revokeApiKey").GetBoolean());
    }

    [SkippableFact]
    public async Task Owned_subscription_create_update_delete()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        await using var clerk = await FakeClerk.StartAsync();
        var factory = ClerkFactory(clerk);
        var token = clerk.Mint(sub: "user_alice", azp: Azp);

        using var created = await AsUserAsync(factory, token, CreateSubMutation,
            new { input = new { kind = "CONCELHO", dico = "1106" } });
        var subId = created.RootElement.GetProperty("data").GetProperty("createAlertSubscription")
            .GetProperty("id").GetString();

        // It is owned → listed under `me`.
        using var me = await AsUserAsync(factory, token, "{ me { alertSubscriptions { id kind dico } } }");
        var subs = me.RootElement.GetProperty("data").GetProperty("me").GetProperty("alertSubscriptions");
        Assert.Equal(1, subs.GetArrayLength());
        Assert.Equal(subId, subs[0].GetProperty("id").GetString());

        // Update it from Concelho to Point (re-runs full validation; the dico is cleared).
        using var updated = await AsUserAsync(factory, token, UpdateSubMutation,
            new { id = subId, input = new { kind = "POINT", latitude = 38.72, longitude = -9.14, radiusKm = 10.0 } });
        var node = updated.RootElement.GetProperty("data").GetProperty("updateAlertSubscription");
        Assert.Equal("POINT", node.GetProperty("kind").GetString());
        Assert.Equal(10.0, node.GetProperty("radiusKm").GetDouble());
        Assert.Equal(JsonValueKind.Null, node.GetProperty("dico").ValueKind);

        // The owner can delete it.
        using var del = await AsUserAsync(factory, token, DeleteSubMutation, new { id = subId });
        Assert.True(del.RootElement.GetProperty("data").GetProperty("deleteAlertSubscription").GetBoolean());
    }

    [SkippableFact]
    public async Task Anonymous_cannot_delete_an_owned_subscription()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        await using var clerk = await FakeClerk.StartAsync();
        var factory = ClerkFactory(clerk);
        var token = clerk.Mint(sub: "user_alice", azp: Azp);

        using var created = await AsUserAsync(factory, token, CreateSubMutation,
            new { input = new { kind = "CONCELHO", dico = "1106" } });
        var subId = created.RootElement.GetProperty("data").GetProperty("createAlertSubscription")
            .GetProperty("id").GetString();

        // Anonymous delete of an owned subscription is a no-op.
        using var anon = await fixture.GraphQLAsync(DeleteSubMutation, new { id = subId });
        Assert.False(anon.RootElement.GetProperty("data").GetProperty("deleteAlertSubscription").GetBoolean());

        // It still exists for its owner.
        using var me = await AsUserAsync(factory, token, "{ me { alertSubscriptions { id } } }");
        Assert.Equal(1, me.RootElement.GetProperty("data").GetProperty("me").GetProperty("alertSubscriptions").GetArrayLength());

        // The owner can still delete it.
        using var owner = await AsUserAsync(factory, token, DeleteSubMutation, new { id = subId });
        Assert.True(owner.RootElement.GetProperty("data").GetProperty("deleteAlertSubscription").GetBoolean());
    }

    private WebApplicationFactory<Program> ClerkFactory(FakeClerk clerk, int? maxApiKeysPerUser = null)
    {
        var overrides = new Dictionary<string, string?>
        {
            ["Clerk:Authority"] = clerk.Authority,
            ["Clerk:JwksUrl"] = clerk.JwksUrl,
            ["Clerk:AuthorizedParties:0"] = Azp,
            ["Clerk:JwksCacheMinutes"] = "60",
        };
        if (maxApiKeysPerUser is { } cap)
            overrides["Auth:MaxApiKeysPerUser"] = cap.ToString();
        return fixture.CreateFactory(overrides);
    }

    private static async Task<JsonDocument> AsUserAsync(
        WebApplicationFactory<Program> factory, string token, string query, object? variables = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static string? ErrorCode(JsonDocument doc) =>
        doc.RootElement.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString();

    private async Task ResetAsync()
    {
        var ctx = fixture.Factory.Services.GetRequiredService<MongoContext>();
        await ctx.ApiClients.DeleteManyAsync(FilterDefinition<ApiClient>.Empty);
        await ctx.Users.DeleteManyAsync(FilterDefinition<User>.Empty);
        await ctx.AlertSubscriptions.DeleteManyAsync(FilterDefinition<AlertSubscription>.Empty);
        await ctx.WebhookEndpoints.DeleteManyAsync(FilterDefinition<WebhookEndpoint>.Empty);
        await ctx.Locations.DeleteManyAsync(FilterDefinition<Location>.Empty);
        await ctx.Locations.InsertOneAsync(new Location { Level = LocationLevel.Concelho, Code = "1106", Name = "LISBOA", Dico = "1106" });
    }
}
