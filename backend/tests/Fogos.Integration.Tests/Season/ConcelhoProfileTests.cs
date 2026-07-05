using System.Text.Json;
using Fogos.Domain.Incidents;
using Fogos.Domain.Locations;
using Fogos.Domain.Risk;
using Fogos.Domain.Time;
using Fogos.Domain.Weather;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Season;

/// <summary>
/// The concelho page payload: resolves DICO → name + district, assembles the risk strip, active
/// incidents, district-mapped IPMA warnings and year-over-year counters; null for an unknown DICO.
/// </summary>
[Collection("fogos")]
public sealed class ConcelhoProfileTests(ContainerFixture fixture)
{
    private const string Dico = "1106";

    [SkippableFact]
    public async Task Resolves_full_profile_for_a_known_concelho()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);
        var clock = fixture.Factory.Services.GetRequiredService<IClock>();

        await ctx.Locations.InsertManyAsync(
        [
            new Location { Level = LocationLevel.Concelho, Code = "1106", Name = "LISBOA", Dico = Dico },
            new Location { Level = LocationLevel.Distrito, Code = "11", Name = "LISBOA" },
        ]);

        await ctx.RcmDaily.InsertOneAsync(new ConcelhoRisk
        {
            Dico = Dico, Concelho = "Lisboa", Date = clock.LisbonToday,
            Today = 3, Tomorrow = 4, After = 5, After2 = null, After3 = 2,
        });

        // Active fire this year in the concelho (also the year-ignition + burn-area sample).
        var active = SeedData.Incident("PROF_ACT", occurredAt: clock.UtcNow, active: true);
        active.Dico = Dico;
        active.Icnf = new IcnfData { BurnArea = new BurnArea(null, null, null, 7) };
        await ctx.Incidents.InsertOneAsync(active);

        // Same concelho, previous year — feeds previousYearIgnitions only.
        var prior = SeedData.Incident("PROF_PREV", occurredAt: clock.UtcNow.AddYears(-1), active: false,
            statusCode: IncidentStatusCatalog.Encerrada);
        prior.Dico = Dico;
        await ctx.Incidents.InsertOneAsync(prior);

        await ctx.WeatherWarnings.InsertOneAsync(new WeatherWarning
        {
            AreaCode = "LSB", AwarenessType = "Tempo Quente", Level = "orange",
            StartsAt = clock.UtcNow.AddHours(-1), EndsAt = clock.UtcNow.AddHours(6),
            Control = "ctrl-lsb-1", CreatedAt = clock.UtcNow,
        });

        using var doc = await fixture.GraphQLAsync(
            """
            query($dico: String!) {
              concelhoProfile(dico: $dico) {
                dico name district
                risk { date level label }
                activeIncidents { id }
                weatherWarnings { areaCode awarenessType levelPt }
                yearIgnitions previousYearIgnitions yearBurnAreaHa
              }
            }
            """,
            new { dico = Dico });
        var profile = doc.RootElement.GetProperty("data").GetProperty("concelhoProfile");

        Assert.Equal(Dico, profile.GetProperty("dico").GetString());
        Assert.Equal("Lisboa", profile.GetProperty("name").GetString());
        Assert.Equal("Lisboa", profile.GetProperty("district").GetString());

        var risk = profile.GetProperty("risk").EnumerateArray().ToList();
        Assert.Equal(4, risk.Count); // After2 (null) dropped
        Assert.Equal(3, risk[0].GetProperty("level").GetInt32());
        Assert.Equal("Elevado", risk[0].GetProperty("label").GetString());

        Assert.Equal("PROF_ACT", Assert.Single(profile.GetProperty("activeIncidents").EnumerateArray()).GetProperty("id").GetString());

        var warning = Assert.Single(profile.GetProperty("weatherWarnings").EnumerateArray());
        Assert.Equal("LSB", warning.GetProperty("areaCode").GetString());
        Assert.Equal("Laranja", warning.GetProperty("levelPt").GetString());

        Assert.Equal(1, profile.GetProperty("yearIgnitions").GetInt32());
        Assert.Equal(1, profile.GetProperty("previousYearIgnitions").GetInt32());
        Assert.Equal(7, profile.GetProperty("yearBurnAreaHa").GetDouble(), 6);
    }

    [SkippableFact]
    public async Task Unknown_dico_returns_null()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        using var doc = await fixture.GraphQLAsync(
            "query { concelhoProfile(dico: \"9999\") { dico } }");
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("data").GetProperty("concelhoProfile").ValueKind);
    }

    private async Task ResetAsync()
    {
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Locations.DeleteManyAsync(FilterDefinition<Location>.Empty);
        await ctx.RcmDaily.DeleteManyAsync(FilterDefinition<ConcelhoRisk>.Empty);
        await ctx.WeatherWarnings.DeleteManyAsync(FilterDefinition<WeatherWarning>.Empty);
    }
}
