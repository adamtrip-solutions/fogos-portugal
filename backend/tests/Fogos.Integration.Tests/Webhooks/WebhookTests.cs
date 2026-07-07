using System.Text.Json;
using Fogos.Domain.Auth;
using Fogos.Domain.Events;
using Fogos.Domain.Webhooks;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Webhooks;
using Fogos.Integration.Tests.Incidents;
using Fogos.Worker.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Webhooks;

/// <summary>
/// Webhook registration (HTTPS-only, valid events, per-client cap, secret-once) and delivery (signed
/// body, 2xx = success, failure counting + auto-disable) with a capturing fake HttpMessageHandler.
/// </summary>
[Collection("fogos")]
public sealed class WebhookTests(ContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Register_validates_and_returns_secret_once_then_list_hides_it()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var key = await SeedKeyAsync();

        const string register = """
            mutation($url: String!, $events: [String!]!) {
              registerWebhook(url: $url, events: $events) { id url events active secret }
            }
            """;

        // Anonymous is rejected.
        using var anon = await fixture.GraphQLAsync(register, new { url = "https://example.test/hook", events = new[] { "incident.created" } });
        Assert.Equal("UNAUTHENTICATED", ErrorCode(anon));

        // HTTP (non-TLS) is rejected.
        using var http = await fixture.GraphQLAsync(key, register, new { url = "http://example.test/hook", events = new[] { "incident.created" } });
        Assert.Equal("WEBHOOK_URL_INVALID", ErrorCode(http));

        // Unknown event is rejected.
        using var badEvent = await fixture.GraphQLAsync(key, register, new { url = "https://example.test/hook", events = new[] { "incident.exploded" } });
        Assert.Equal("WEBHOOK_EVENTS_INVALID", ErrorCode(badEvent));

        // Valid registration returns the secret.
        using var ok = await fixture.GraphQLAsync(key, register, new { url = "https://example.test/hook", events = new[] { "incident.created", "report.created" } });
        var created = ok.RootElement.GetProperty("data").GetProperty("registerWebhook");
        var id = created.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(created.GetProperty("secret").GetString()));
        Assert.True(created.GetProperty("active").GetBoolean());

        // The webhooks query never re-exposes the secret.
        using var list = await fixture.GraphQLAsync(key, "{ webhooks { id secret } }");
        var row = list.RootElement.GetProperty("data").GetProperty("webhooks")[0];
        Assert.Equal(id, row.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("secret").ValueKind);
    }

    [SkippableFact]
    public async Task Register_enforces_three_endpoint_cap()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var key = await SeedKeyAsync();

        const string register = "mutation($url: String!) { registerWebhook(url: $url, events: [\"incident.created\"]) { id } }";
        for (var i = 0; i < 3; i++)
        {
            using var ok = await fixture.GraphQLAsync(key, register, new { url = $"https://example.test/hook{i}" });
            Assert.False(ok.RootElement.TryGetProperty("errors", out _));
        }

        using var over = await fixture.GraphQLAsync(key, register, new { url = "https://example.test/hook4" });
        Assert.Equal("WEBHOOK_LIMIT", ErrorCode(over));
    }

    [SkippableFact]
    public async Task Delivery_signs_the_body()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        var endpoint = new WebhookEndpoint
        {
            ClientId = "client-x", Url = "https://hook.test/a", Secret = "s3cr3t",
            Events = ["incident.created"], Active = true, CreatedAt = Now,
        };
        await ctx.WebhookEndpoints.InsertOneAsync(endpoint);

        var capturing = new CapturingHandler((_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var handler = BuildHandler(capturing);

        await handler.HandleAsync(new IncidentCreated("INC1"), CancellationToken.None);

        var request = Assert.Single(capturing.Requests);
        Assert.Contains("incident.created", request.Body);
        Assert.Contains("INC1", request.Body);
        Assert.Equal(WebhookSigner.Sign("s3cr3t", request.Body), request.Signature);
    }

    [SkippableFact]
    public async Task Consecutive_failures_disable_the_endpoint_with_ops_notice()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        var endpoint = new WebhookEndpoint
        {
            ClientId = "client-y", Url = "https://hook.test/b", Secret = "k",
            Events = ["incident.created"], Active = true, CreatedAt = Now,
        };
        await ctx.WebhookEndpoints.InsertOneAsync(endpoint);

        var capturing = new CapturingHandler((_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        var ops = new RecordingOps();
        var handler = BuildHandler(capturing, ops);

        for (var i = 0; i < 10; i++)
            await handler.HandleAsync(new IncidentCreated($"INC{i}"), CancellationToken.None);

        var stored = await ctx.WebhookEndpoints.Find(Builders<WebhookEndpoint>.Filter.Eq(x => x.Id, endpoint.Id)).FirstAsync();
        Assert.False(stored.Active);
        Assert.True(stored.ConsecutiveFailures >= 10);
        Assert.Contains(ops.Errors, e => e.Contains("desativado"));
    }

    private WebhookDispatchHandler BuildHandler(CapturingHandler capturing, RecordingOps? ops = null)
    {
        var mongo = fixture.Factory.Services.GetRequiredService<MongoContext>();
        var factory = new StubHttpClientFactory(capturing);
        var clock = new TestClock { UtcNow = Now };
        return new WebhookDispatchHandler(
            new WebhookReads(mongo), mongo, factory, ops ?? new RecordingOps(), clock,
            Options.Create(new WebhookOptions()), NullLogger<WebhookDispatchHandler>.Instance);
    }

    private static string? ErrorCode(JsonDocument doc) =>
        doc.RootElement.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString();

    private async Task<string> SeedKeyAsync()
    {
        // First-party tier: issued (Registered, scope-less) keys are read-only and can no longer
        // register webhooks — the resolver logic under test needs a mutation-capable machine client.
        var plaintext = "wh-" + Guid.NewGuid().ToString("N");
        await SeedData.InsertApiKeyAsync(fixture, plaintext, ApiTier.FirstParty, name: "webhook client");
        return plaintext;
    }

    private async Task ResetAsync()
    {
        var ctx = SeedData.Context(fixture);
        await ctx.WebhookEndpoints.DeleteManyAsync(FilterDefinition<WebhookEndpoint>.Empty);
        await ctx.ApiClients.DeleteManyAsync(FilterDefinition<ApiClient>.Empty);
    }
}

/// <summary>Fake handler that records each request (url, body, signature) and responds via a supplied function.</summary>
internal sealed class CapturingHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
{
    public readonly List<(string Url, string Body, string? Signature)> Requests = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        request.Headers.TryGetValues(WebhookSigner.SignatureHeader, out var sig);
        Requests.Add((request.RequestUri!.ToString(), body, sig is null ? null : string.Join("", sig)));
        return responder(request, body);
    }
}
