using System.Text.Json;
using Fogos.Domain.Alerts;
using Fogos.Domain.Locations;
using Fogos.Infrastructure.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Alerts;

/// <summary>
/// The createAlertSubscription validation matrix (Concelho DICO existence, Point radius + Portugal
/// bounding box, risk threshold) plus delete, driven through the anonymous GraphQL surface.
/// </summary>
[Collection("fogos")]
public sealed class AlertSubscriptionTests(ContainerFixture fixture)
{
    private const string CreateMutation = """
        mutation($input: CreateAlertSubscriptionInput!) {
          createAlertSubscription(input: $input) {
            id kind dico radiusKm riskThreshold point { latitude longitude }
          }
        }
        """;

    [SkippableFact]
    public async Task Concelho_subscription_requires_a_known_dico()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        using var ok = await fixture.GraphQLAsync(CreateMutation, new { input = new { kind = "CONCELHO", dico = "1106" } });
        var node = ok.RootElement.GetProperty("data").GetProperty("createAlertSubscription");
        Assert.Equal("1106", node.GetProperty("dico").GetString());
        Assert.Equal("CONCELHO", node.GetProperty("kind").GetString());

        using var unknown = await fixture.GraphQLAsync(CreateMutation, new { input = new { kind = "CONCELHO", dico = "9999" } });
        Assert.Equal("ALERT_DICO_UNKNOWN", ErrorCode(unknown));

        using var missing = await fixture.GraphQLAsync(CreateMutation, new { input = new { kind = "CONCELHO" } });
        Assert.Equal("ALERT_DICO_REQUIRED", ErrorCode(missing));
    }

    [SkippableFact]
    public async Task Point_subscription_validates_radius_and_bounding_box()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        using var ok = await fixture.GraphQLAsync(CreateMutation,
            new { input = new { kind = "POINT", latitude = 38.72, longitude = -9.14, radiusKm = 10.0 } });
        var node = ok.RootElement.GetProperty("data").GetProperty("createAlertSubscription");
        Assert.Equal(10.0, node.GetProperty("radiusKm").GetDouble());
        Assert.True(node.GetProperty("riskThreshold").ValueKind == JsonValueKind.Null);

        // Risk thresholds are concelho-only — rejected on point subscriptions.
        using var withRisk = await fixture.GraphQLAsync(CreateMutation,
            new { input = new { kind = "POINT", latitude = 38.72, longitude = -9.14, radiusKm = 10.0, riskThreshold = 5 } });
        Assert.Equal("ALERT_RISK_THRESHOLD_SCOPE", ErrorCode(withRisk));

        using var farAway = await fixture.GraphQLAsync(CreateMutation,
            new { input = new { kind = "POINT", latitude = 48.85, longitude = 2.35, radiusKm = 10.0 } });
        Assert.Equal("ALERT_POINT_OUT_OF_BOUNDS", ErrorCode(farAway));

        using var tooWide = await fixture.GraphQLAsync(CreateMutation,
            new { input = new { kind = "POINT", latitude = 38.72, longitude = -9.14, radiusKm = 100.0 } });
        Assert.Equal("ALERT_RADIUS_RANGE", ErrorCode(tooWide));

        using var noRadius = await fixture.GraphQLAsync(CreateMutation,
            new { input = new { kind = "POINT", latitude = 38.72, longitude = -9.14 } });
        Assert.Equal("ALERT_RADIUS_REQUIRED", ErrorCode(noRadius));
    }

    [SkippableFact]
    public async Task Invalid_risk_threshold_is_rejected()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        using var bad = await fixture.GraphQLAsync(CreateMutation,
            new { input = new { kind = "CONCELHO", dico = "1106", riskThreshold = 3 } });
        Assert.Equal("ALERT_RISK_THRESHOLD", ErrorCode(bad));
    }

    [SkippableFact]
    public async Task Delete_returns_true_then_false()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        using var created = await fixture.GraphQLAsync(CreateMutation, new { input = new { kind = "CONCELHO", dico = "1106" } });
        var id = created.RootElement.GetProperty("data").GetProperty("createAlertSubscription").GetProperty("id").GetString();

        const string deleteMutation = "mutation($id: ID!) { deleteAlertSubscription(id: $id) }";
        using var first = await fixture.GraphQLAsync(deleteMutation, new { id });
        Assert.True(first.RootElement.GetProperty("data").GetProperty("deleteAlertSubscription").GetBoolean());

        using var second = await fixture.GraphQLAsync(deleteMutation, new { id });
        Assert.False(second.RootElement.GetProperty("data").GetProperty("deleteAlertSubscription").GetBoolean());
    }

    private static string? ErrorCode(JsonDocument doc) =>
        doc.RootElement.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString();

    private async Task ResetAsync()
    {
        var ctx = fixture.Factory.Services.GetRequiredService<MongoContext>();
        await ctx.AlertSubscriptions.DeleteManyAsync(FilterDefinition<AlertSubscription>.Empty);
        await ctx.Locations.DeleteManyAsync(FilterDefinition<Location>.Empty);
        await ctx.Locations.InsertOneAsync(new Location { Level = LocationLevel.Concelho, Code = "1106", Name = "LISBOA", Dico = "1106" });
    }
}
