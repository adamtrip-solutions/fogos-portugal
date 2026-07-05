using Fogos.Domain.Auth;
using Fogos.Domain.Warnings;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Writes;

/// <summary>
/// <c>addWarning</c> operator mutation: persists a broadcast warning with the caller as issuer, fans it out
/// (dry-run) via the worker handler — MANUAL with the "ALERTA:" banner, AGIF verbatim — rejects SITE, and
/// enforces the <c>write:warnings</c> scope.
/// </summary>
[Collection("fogos")]
public sealed class AddWarningTests(ContainerFixture fixture)
{
    private const string WarnKey = "fgs_live_operator_warnings";

    private async Task SeedWarningsOperatorAsync() =>
        await SeedData.InsertApiKeyAsync(fixture, WarnKey, ApiTier.Operator,
            name: "civil protection", scopes: [ApiScopes.WriteWarnings]);

    [SkippableFact]
    public async Task Manual_warning_persists_with_issuer()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await SeedWarningsOperatorAsync();

        var doc = await fixture.GraphQLAsync(WarnKey,
            "mutation($input:WarningInput!){ addWarning(kind:MANUAL, input:$input){ id kind message } }",
            new { input = new { message = "Evacuação imediata da zona X" } });

        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        var result = doc.RootElement.GetProperty("data").GetProperty("addWarning");
        var id = result.GetProperty("id").GetString()!;
        Assert.Equal("MANUAL", result.GetProperty("kind").GetString());

        var stored = await ctx.Warnings.Find(Builders<Warning>.Filter.Eq(x => x.Id, id)).SingleAsync();
        Assert.Equal(WarningKind.Manual, stored.Kind);
        Assert.Equal("civil protection", stored.IssuedBy);
        Assert.Equal("Evacuação imediata da zona X", stored.Message);
    }

    [SkippableFact]
    public async Task Site_warning_is_rejected()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await SeedWarningsOperatorAsync();

        var doc = await fixture.GraphQLAsync(WarnKey,
            "mutation($input:WarningInput!){ addWarning(kind:SITE, input:$input){ id } }",
            new { input = new { message = "banner" } });

        Assert.True(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        Assert.Contains("WARNING_KIND_UNSUPPORTED", doc.RootElement.GetProperty("errors").ToString());
    }

    [SkippableFact]
    public async Task Incidents_only_operator_cannot_add_a_warning()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var incidentsKey = "fgs_live_operator_incidents_nowarn";
        await SeedData.InsertApiKeyAsync(fixture, incidentsKey, ApiTier.Operator,
            name: "incident operator", scopes: [ApiScopes.WriteIncidents]);

        var doc = await fixture.GraphQLAsync(incidentsKey,
            "mutation($input:WarningInput!){ addWarning(kind:MANUAL, input:$input){ id } }",
            new { input = new { message = "should be denied" } });

        Assert.True(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
    }
}
