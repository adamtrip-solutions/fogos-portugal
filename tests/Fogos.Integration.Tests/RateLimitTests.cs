using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fogos.Domain.Photos;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fogos.Integration.Tests;

[Collection("fogos")]
public sealed class RateLimitTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Anonymous_requests_over_limit_get_429_with_retry_after()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimit:Anonymous:Requests"] = "5",
        });
        var client = factory.CreateClient();

        // First 5 within the window succeed.
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v3/incidents/active.geojson")).StatusCode);

        // The 6th trips the limiter.
        var blocked = await client.GetAsync("/v3/incidents/active.geojson");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
    }

    [SkippableFact]
    public async Task GraphQL_cost_budget_rejects_heavy_query_and_allows_cheap_one()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimit:Anonymous:CostBudget"] = "20",
        });
        var client = factory.CreateClient();

        // Cheap query (cost 10 + 1) stays under budget.
        var cheap = await client.PostAsJsonAsync("/graphql", new { query = "{ __typename }" });
        using var cheapDoc = JsonDocument.Parse(await cheap.Content.ReadAsStringAsync());
        Assert.False(cheapDoc.RootElement.TryGetProperty("errors", out _), "cheap query should pass the budget");

        // Heavy paginated query (cost 10 + 5×4 = 30) exceeds the tiny budget → RATE_LIMITED.
        var heavy = await client.PostAsJsonAsync("/graphql", new { query = "{ incidents(first:100){ nodes { id } } }" });
        using var heavyDoc = JsonDocument.Parse(await heavy.Content.ReadAsStringAsync());
        Assert.True(heavyDoc.RootElement.TryGetProperty("errors", out var errors), "heavy query should be rejected");
        Assert.Equal("RATE_LIMITED", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task PhotoUploadGates_trip_on_per_ip_rate_and_pending_cap()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();

        // The shared factory raises the photo gates (endpoint upload tests share one TestServer IP);
        // this test asserts the real production defaults, so it builds its own factory with them.
        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["PhotoGate:PerIpPerMinute"] = "3",
            ["PhotoGate:PerIncidentPerIpPerHour"] = "8",
            ["PhotoGate:PerIncidentPerHour"] = "80",
            ["PhotoGate:PendingPerIncident"] = "50",
        });
        var gates = factory.Services.GetRequiredService<PhotoUploadGates>();

        // 3/min/IP → the 4th attempt trips.
        const string incidentA = "photo-inc-A";
        const string ip = "203.0.113.7";
        Assert.True((await gates.CheckAsync(incidentA, ip)).Passed);
        Assert.True((await gates.CheckAsync(incidentA, ip)).Passed);
        Assert.True((await gates.CheckAsync(incidentA, ip)).Passed);
        var fourth = await gates.CheckAsync(incidentA, ip);
        Assert.False(fourth.Passed);
        Assert.Equal(PhotoGate.PerIpPerMinute, fourth.Gate);

        // Pending-moderation cap: 50 pending docs on an incident block further uploads.
        const string incidentB = "photo-inc-B";
        var ctx = SeedData.Context(fixture);
        await ctx.IncidentPhotos.InsertManyAsync(
            Enumerable.Range(0, 50).Select(_ => SeedData.Photo(incidentB, ModerationStatus.Pending)));

        var pending = await gates.CheckAsync(incidentB, "198.51.100.9");
        Assert.False(pending.Passed);
        Assert.Equal(PhotoGate.PendingModeration, pending.Gate);
    }

    [SkippableFact]
    public async Task Subscription_caps_reject_anonymous_and_enforce_tier_limit()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimit:Anonymous:Subscriptions"] = "0",
            ["RateLimit:Registered:Subscriptions"] = "2",
        });
        _ = factory.Services;

        var options = factory.Services.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        var limiter = factory.Services.GetRequiredService<SubscriptionLimiter>();

        // Anonymous (cap 0) is never allowed to subscribe — the interceptor rejects the connection.
        Assert.Equal(0, options.Anonymous.Subscriptions);
        Assert.False(SubscriptionLimiter.Allowed(options.Anonymous.Subscriptions));

        // Registered cap of 2: two slots granted, the third refused until one is released.
        const string partition = "ck:sub-test-client";
        Assert.True(await limiter.TryAcquireAsync(partition, 2));
        Assert.True(await limiter.TryAcquireAsync(partition, 2));
        Assert.False(await limiter.TryAcquireAsync(partition, 2));

        await limiter.ReleaseAsync(partition);
        Assert.True(await limiter.TryAcquireAsync(partition, 2));
    }
}
