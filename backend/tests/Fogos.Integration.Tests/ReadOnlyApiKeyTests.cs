using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
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
/// Issued API keys are read-only: a machine credential without scopes (and below FirstParty tier) can
/// query freely but every mutation operation is rejected centrally with <c>API_KEY_READ_ONLY</c> — over
/// HTTP and over graphql-ws alike, since the guard sits in the HotChocolate request pipeline. Anonymous
/// callers, the first-party web key, scoped operator keys and signed-in users are unaffected.
/// </summary>
[Collection("fogos")]
public sealed class ReadOnlyApiKeyTests(ContainerFixture fixture)
{
    private const string Azp = "https://app.fogos.pt";

    private const string CreateSubMutation = """
        mutation($input: CreateAlertSubscriptionInput!) {
          createAlertSubscription(input: $input) { id kind dico }
        }
        """;

    private const string RegisterWebhookMutation = """
        mutation($url: String!, $events: [String!]!) {
          registerWebhook(url: $url, events: $events) { id url }
        }
        """;

    private static readonly object ConcelhoInput = new { input = new { kind = "CONCELHO", dico = "1106" } };

    [SkippableFact]
    public async Task Readonly_key_can_query_but_not_mutate()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        const string key = "fgs_live_readonly_key";
        await SeedData.InsertApiKeyAsync(fixture, key, ApiTier.Registered, name: "self-service key");

        // Queries keep working.
        using var query = await fixture.GraphQLAsync(key, "{ activeIncidents { id } }");
        Assert.False(query.RootElement.TryGetProperty("errors", out _), query.RootElement.ToString());
        Assert.NotEqual(JsonValueKind.Null, query.RootElement.GetProperty("data").GetProperty("activeIncidents").ValueKind);

        // A valid anonymous-allowed mutation is rejected centrally.
        using var sub = await fixture.GraphQLAsync(key, CreateSubMutation, ConcelhoInput);
        AssertReadOnlyError(sub.RootElement);

        using var hook = await fixture.GraphQLAsync(key, RegisterWebhookMutation,
            new { url = "https://example.com/hook", events = new[] { "incident.created" } });
        AssertReadOnlyError(hook.RootElement);

        // Nothing was written.
        var ctx = SeedData.Context(fixture);
        Assert.Equal(0, await ctx.AlertSubscriptions.CountDocumentsAsync(FilterDefinition<AlertSubscription>.Empty));
        Assert.Equal(0, await ctx.WebhookEndpoints.CountDocumentsAsync(FilterDefinition<WebhookEndpoint>.Empty));
    }

    [SkippableFact]
    public async Task Anonymous_caller_can_still_mutate()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        using var doc = await fixture.GraphQLAsync(CreateSubMutation, ConcelhoInput);
        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        Assert.False(string.IsNullOrEmpty(
            doc.RootElement.GetProperty("data").GetProperty("createAlertSubscription").GetProperty("id").GetString()));
    }

    [SkippableFact]
    public async Task FirstParty_key_can_still_mutate()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        const string key = "fgs_live_first_party_web";
        await SeedData.InsertApiKeyAsync(fixture, key, ApiTier.FirstParty, name: "web ssr key");

        using var doc = await fixture.GraphQLAsync(key, CreateSubMutation, ConcelhoInput);
        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        Assert.Equal("1106", doc.RootElement.GetProperty("data").GetProperty("createAlertSubscription")
            .GetProperty("dico").GetString());
    }

    [SkippableFact]
    public async Task Scoped_key_can_still_use_its_scoped_mutation()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.DeleteManyAsync(FilterDefinition<Fogos.Domain.Incidents.Incident>.Empty);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("ROK1"));
        const string key = "fgs_live_operator_readonly_probe";
        await SeedData.InsertApiKeyAsync(fixture, key, ApiTier.Operator,
            name: "posit operator", scopes: [ApiScopes.WriteIncidents]);

        using var doc = await fixture.GraphQLAsync(key,
            "mutation($id:ID!,$input:PositInput!){ addPosit(incidentId:$id, input:$input){ id resources { man } } }",
            new { id = "ROK1", input = new { man = 7 } });

        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        Assert.Equal(7, doc.RootElement.GetProperty("data").GetProperty("addPosit")
            .GetProperty("resources").GetProperty("man").GetInt32());
    }

    [SkippableFact]
    public async Task Clerk_user_can_still_create_api_keys()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        await using var clerk = await FakeClerk.StartAsync();
        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["Clerk:Authority"] = clerk.Authority,
            ["Clerk:JwksUrl"] = clerk.JwksUrl,
            ["Clerk:AuthorizedParties:0"] = Azp,
            ["Clerk:JwksCacheMinutes"] = "60",
        });
        var token = clerk.Mint(sub: "user_alice", email: "alice@fogos.pt", name: "Alice", azp: Azp);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = "mutation($name: String!) { createApiKey(name: $name) { plaintextKey apiKey { id tier } } }",
            variables = new { name = "CLI key" },
        });
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        Assert.StartsWith("fgs_live_", doc.RootElement.GetProperty("data").GetProperty("createApiKey")
            .GetProperty("plaintextKey").GetString());
    }

    [SkippableFact]
    public async Task Readonly_key_is_blocked_over_websockets_too()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        const string key = "fgs_live_readonly_ws";
        await SeedData.InsertApiKeyAsync(fixture, key, ApiTier.Registered, name: "self-service key");

        var result = await RunOverWebSocketAsync(key, CreateSubMutation, ConcelhoInput);
        Assert.Contains("API_KEY_READ_ONLY", result.ToString());

        var ctx = SeedData.Context(fixture);
        Assert.Equal(0, await ctx.AlertSubscriptions.CountDocumentsAsync(FilterDefinition<AlertSubscription>.Empty));
    }

    [SkippableFact]
    public async Task FirstParty_key_can_still_mutate_over_websockets()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        const string key = "fgs_live_first_party_ws";
        await SeedData.InsertApiKeyAsync(fixture, key, ApiTier.FirstParty, name: "web ssr key");

        var result = await RunOverWebSocketAsync(key, CreateSubMutation, ConcelhoInput);
        Assert.DoesNotContain("API_KEY_READ_ONLY", result.ToString());
        Assert.Equal("1106", result.GetProperty("data").GetProperty("createAlertSubscription")
            .GetProperty("dico").GetString());
    }

    private static void AssertReadOnlyError(JsonElement root)
    {
        var errors = root.GetProperty("errors");
        Assert.Equal(1, errors.GetArrayLength());
        Assert.Equal("As chaves de API são apenas de leitura.", errors[0].GetProperty("message").GetString());
        Assert.Equal("API_KEY_READ_ONLY", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    /// <summary>
    /// Runs a single operation over graphql-transport-ws (connection_init with the API key in the connect
    /// payload → subscribe → first result frame) and returns the payload of the <c>next</c> frame, or a
    /// synthesized <c>{ errors: [...] }</c> object when the server answers with an <c>error</c> frame.
    /// </summary>
    private async Task<JsonElement> RunOverWebSocketAsync(string apiKey, string query, object variables)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var client = fixture.Factory.Server.CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/graphql"), cts.Token);

        await SendAsync(socket, new { type = "connection_init", payload = new { apiKey } }, cts.Token);
        var ack = await ReceiveAsync(socket, cts.Token);
        Assert.Equal("connection_ack", ack.GetProperty("type").GetString());

        await SendAsync(socket, new { id = "1", type = "subscribe", payload = new { query, variables } }, cts.Token);

        while (true)
        {
            var message = await ReceiveAsync(socket, cts.Token);
            switch (message.GetProperty("type").GetString())
            {
                case "next":
                    return message.GetProperty("payload");
                case "error":
                    using (var synthesized = JsonDocument.Parse(
                        $$"""{ "errors": {{message.GetProperty("payload").GetRawText()}} }"""))
                    {
                        return synthesized.RootElement.Clone();
                    }
                case "complete":
                    throw new InvalidOperationException("Operation completed without a result frame.");
                // ping/keep-alive frames: ignore.
            }
        }
    }

    private static Task SendAsync(WebSocket socket, object message, CancellationToken ct) =>
        socket.SendAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)),
            WebSocketMessageType.Text,
            endOfMessage: true,
            ct);

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException($"Socket closed: {socket.CloseStatus} {socket.CloseStatusDescription}");
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }
        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private async Task ResetAsync()
    {
        var ctx = SeedData.Context(fixture);
        await ctx.ApiClients.DeleteManyAsync(FilterDefinition<ApiClient>.Empty);
        await ctx.Users.DeleteManyAsync(FilterDefinition<User>.Empty);
        await ctx.AlertSubscriptions.DeleteManyAsync(FilterDefinition<AlertSubscription>.Empty);
        await ctx.WebhookEndpoints.DeleteManyAsync(FilterDefinition<WebhookEndpoint>.Empty);
        await ctx.Locations.DeleteManyAsync(FilterDefinition<Location>.Empty);
        await ctx.Locations.InsertOneAsync(new Location { Level = LocationLevel.Concelho, Code = "1106", Name = "LISBOA", Dico = "1106" });
    }
}
