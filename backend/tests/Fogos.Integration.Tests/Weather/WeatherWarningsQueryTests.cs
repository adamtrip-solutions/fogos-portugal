using Fogos.Domain.Time;
using Fogos.Domain.Weather;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Weather;

/// <summary>
/// The <c>weatherWarnings</c> root query: returns every scraped IPMA awareness warning still in force
/// (ends in the future), most-severe first. Avisos are automatic-only — there is no write path.
/// </summary>
[Collection("fogos")]
public sealed class WeatherWarningsQueryTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Returns_only_in_force_warnings_most_severe_first()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);
        var clock = fixture.Factory.Services.GetRequiredService<IClock>();
        var now = clock.UtcNow;

        // Expired — ended in the past, must be excluded.
        await ctx.WeatherWarnings.InsertOneAsync(new WeatherWarning
        {
            AreaCode = "PTO", AwarenessType = "Nevoeiro", Level = "yellow",
            StartsAt = now.AddHours(-8), EndsAt = now.AddHours(-2),
            Control = "ctrl-expired", CreatedAt = now.AddHours(-8),
        });

        // In force — a yellow and a red, both ending in the future.
        await ctx.WeatherWarnings.InsertOneAsync(new WeatherWarning
        {
            AreaCode = "LSB", AwarenessType = "Tempo Quente", Level = "yellow",
            StartsAt = now.AddHours(-1), EndsAt = now.AddHours(6),
            Control = "ctrl-lsb", CreatedAt = now,
        });
        await ctx.WeatherWarnings.InsertOneAsync(new WeatherWarning
        {
            AreaCode = "FAR", AwarenessType = "Agitação Marítima", Level = "red",
            StartsAt = now.AddHours(-1), EndsAt = now.AddHours(12),
            Control = "ctrl-far", CreatedAt = now,
        });

        using var doc = await fixture.GraphQLAsync(
            "query { weatherWarnings { areaCode awarenessType level levelPt } }");

        var warnings = doc.RootElement.GetProperty("data").GetProperty("weatherWarnings").EnumerateArray().ToList();
        Assert.Equal(2, warnings.Count); // the expired one is dropped
        Assert.DoesNotContain(warnings, w => w.GetProperty("areaCode").GetString() == "PTO");

        // Most severe first: red (FAR) before yellow (LSB).
        Assert.Equal("FAR", warnings[0].GetProperty("areaCode").GetString());
        Assert.Equal("Vermelho", warnings[0].GetProperty("levelPt").GetString());
        Assert.Equal("LSB", warnings[1].GetProperty("areaCode").GetString());
        Assert.Equal("Amarelo", warnings[1].GetProperty("levelPt").GetString());
    }

    private async Task ResetAsync()
    {
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.WeatherWarnings.DeleteManyAsync(FilterDefinition<WeatherWarning>.Empty);
    }
}
