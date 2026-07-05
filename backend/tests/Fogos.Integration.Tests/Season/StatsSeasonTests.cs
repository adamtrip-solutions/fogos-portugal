using System.Text.Json;
using Fogos.Domain.Incidents;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Season;

/// <summary>
/// Season-analytics aggregations over a fixed seeded dataset, asserting exact numbers including the
/// Lisbon-timezone day boundary (a UTC-late summer ignition buckets on the next Lisbon day) and the
/// year boundary (a prior-December fire is excluded from the following year).
/// </summary>
[Collection("fogos")]
public sealed class StatsSeasonTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Ignitions_burn_area_and_causes_respect_year_and_lisbon_timezone()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);

        await ctx.Incidents.InsertOneAsync(WithIcnf(
            Fire("F1", Utc(2025, 1, 1, 0, 30), district: "Lisboa"), burn: 10, cause: "Intencional"));
        // 2025-07-15 23:30 UTC → Lisbon (WEST, +1) 2025-07-16 00:30.
        await ctx.Incidents.InsertOneAsync(WithIcnf(
            Fire("F2", Utc(2025, 7, 15, 23, 30), district: "Porto"), burn: 5, cause: "Natural"));
        await ctx.Incidents.InsertOneAsync(
            Fire("F3", Utc(2025, 7, 16, 10, 0), district: "Porto"));
        // Prior-year fire — must never appear in 2025.
        await ctx.Incidents.InsertOneAsync(WithIcnf(
            Fire("F4", Utc(2024, 12, 31, 23, 30), district: "Lisboa"), burn: 99, cause: "Intencional"));

        using var doc = await fixture.GraphQLAsync(
            """
            query {
              stats {
                ignitionsByDay(year: 2025) { date count }
                burnAreaCumulative(year: 2025) { date totalHa }
                causeBreakdown(year: 2025) { causeFamily count burnAreaHa }
              }
            }
            """);
        var stats = doc.RootElement.GetProperty("data").GetProperty("stats");

        // ── ignitionsByDay ──
        var byDay = stats.GetProperty("ignitionsByDay").EnumerateArray()
            .ToDictionary(e => e.GetProperty("date").GetString()!, e => e.GetProperty("count").GetInt32());
        Assert.Equal(3, byDay.Values.Sum());                 // F1, F2, F3 (F4 is 2024)
        Assert.Equal(1, byDay["2025-01-01"]);
        Assert.Equal(0, byDay["2025-07-15"]);                // Lisbon rolled F2 to the 16th
        Assert.Equal(2, byDay["2025-07-16"]);                // F2 + F3
        Assert.DoesNotContain(byDay.Keys, k => k.StartsWith("2024"));

        // ── burnAreaCumulative ──
        var cumulative = stats.GetProperty("burnAreaCumulative").EnumerateArray()
            .ToDictionary(e => e.GetProperty("date").GetString()!, e => e.GetProperty("totalHa").GetDouble());
        Assert.Equal(10, cumulative["2025-01-01"]);
        Assert.Equal(15, cumulative["2025-07-16"]);
        Assert.Equal(15, cumulative["2025-12-31"]);

        // ── causeBreakdown ──
        var causes = stats.GetProperty("causeBreakdown").EnumerateArray()
            .ToDictionary(e => e.GetProperty("causeFamily").GetString()!,
                          e => (Count: e.GetProperty("count").GetInt32(), Burn: e.GetProperty("burnAreaHa").GetDouble()));
        Assert.Equal(3, causes.Count);
        Assert.Equal((1, 10), causes["Intencional"]);
        Assert.Equal((1, 5), causes["Natural"]);
        Assert.Equal((1, 0), causes["Desconhecida"]);        // F3 has no cause family
    }

    [SkippableFact]
    public async Task False_alarm_stats_rank_districts_over_the_minimum_sample()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);

        // Distrito Alfa: 25 total (≥ 20), 5 non-active false alarms → rate 0.2.
        for (var i = 0; i < 5; i++)
            await ctx.Incidents.InsertOneAsync(FalseAlarm($"FA{i}", "Distrito Alfa"));
        for (var i = 0; i < 20; i++)
            await ctx.Incidents.InsertOneAsync(Fire($"OK{i}", Utc(2025, 3, 1, 12, 0), district: "Distrito Alfa"));

        // Distrito Beta: only 10 total → below the threshold, excluded.
        for (var i = 0; i < 10; i++)
            await ctx.Incidents.InsertOneAsync(FalseAlarm($"FB{i}", "Distrito Beta"));

        using var doc = await fixture.GraphQLAsync(
            "query { stats { falseAlarmStats(year: 2025) { district total falseAlarms rate } } }");
        var rows = doc.RootElement.GetProperty("data").GetProperty("stats").GetProperty("falseAlarmStats").EnumerateArray().ToList();

        var alfa = Assert.Single(rows, r => r.GetProperty("district").GetString() == "Distrito Alfa");
        Assert.Equal(25, alfa.GetProperty("total").GetInt32());
        Assert.Equal(5, alfa.GetProperty("falseAlarms").GetInt32());
        Assert.Equal(0.2, alfa.GetProperty("rate").GetDouble(), 6);
        Assert.DoesNotContain(rows, r => r.GetProperty("district").GetString() == "Distrito Beta");
    }

    [SkippableFact]
    public async Task Response_time_stats_median_over_status_history()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.IncidentStatusHistory.DeleteManyAsync(FilterDefinition<IncidentStatusChange>.Empty);

        await ctx.Incidents.InsertOneAsync(Fire("RT1", Utc(2025, 6, 1, 8, 0)));
        await ctx.Incidents.InsertOneAsync(Fire("RT2", Utc(2025, 6, 2, 8, 0)));

        var t0 = Utc(2025, 6, 1, 8, 0);
        await ctx.IncidentStatusHistory.InsertManyAsync(
        [
            Change("RT1", t0, IncidentStatusCatalog.Despacho),
            Change("RT1", t0.AddSeconds(120), IncidentStatusCatalog.ChegadaAoTeatroDeOperacoes),
            Change("RT1", t0.AddSeconds(420), IncidentStatusCatalog.EmResolucao),
        ]);
        var u0 = Utc(2025, 6, 2, 8, 0);
        await ctx.IncidentStatusHistory.InsertManyAsync(
        [
            Change("RT2", u0, IncidentStatusCatalog.DespachoPrimeiroAlerta),
            Change("RT2", u0.AddSeconds(180), IncidentStatusCatalog.ChegadaAoTeatroDeOperacoes),
        ]);

        using var doc = await fixture.GraphQLAsync(
            "query { stats { responseTimeStats(year: 2025) { count medianDispatchToArrivalSeconds medianArrivalToControlSeconds } } }");
        var rt = doc.RootElement.GetProperty("data").GetProperty("stats").GetProperty("responseTimeStats");

        Assert.Equal(2, rt.GetProperty("count").GetInt32());
        Assert.Equal(150, rt.GetProperty("medianDispatchToArrivalSeconds").GetInt32()); // median(120, 180)
        Assert.Equal(300, rt.GetProperty("medianArrivalToControlSeconds").GetInt32());  // single sample 300
    }

    private static DateTimeOffset Utc(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    private static Incident Fire(string id, DateTimeOffset occurredAt, string district = "Lisboa") =>
        SeedData.Incident(id, occurredAt: occurredAt, district: district, concelho: district);

    private static Incident FalseAlarm(string id, string district) =>
        SeedData.Incident(id, occurredAt: Utc(2025, 3, 1, 12, 0), district: district, concelho: district,
            statusCode: IncidentStatusCatalog.FalsoAlarme, active: false);

    private static Incident WithIcnf(Incident incident, double burn, string cause)
    {
        incident.Icnf = new IcnfData { BurnArea = new BurnArea(null, null, null, burn), CauseFamily = cause };
        return incident;
    }

    private static IncidentStatusChange Change(string incidentId, DateTimeOffset at, int code) =>
        new() { IncidentId = incidentId, At = at, Code = code, Label = IncidentStatusCatalog.FromCode(code).Label };
}
