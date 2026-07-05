using Fogos.Domain.Incidents;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Signals;

/// <summary>
/// The read schema surfaces <c>signals</c> (never null, absent → all-false defaults) and
/// <c>responseTimes</c> (computed from the status log) on an incident.
/// </summary>
[Collection("fogos")]
public sealed class SignalsGraphQLTests(ContainerFixture fixture)
{
    private const string Query = """
        query($id: ID!) {
          incident(id: $id) {
            signals {
              escalating peakAssets rekindle rekindleOfId
              criticalConditions criticalReasons
            }
            responseTimes {
              dispatchToArrivalSeconds arrivalToControlSeconds
              controlToConclusionSeconds totalSeconds
            }
          }
        }
        """;

    [SkippableFact]
    public async Task Populated_signals_and_response_times_resolve()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);

        var incident = SeedData.Incident("GQL_SIG");
        incident.Signals = new IncidentSignals
        {
            Escalating = true,
            PeakAssets = 42,
            Rekindle = true,
            RekindleOfId = "PRIOR9",
            CriticalConditions = true,
            CriticalReasons = [SignalRules.TempAbove30, SignalRules.WindAbove30],
        };
        await ctx.Incidents.InsertOneAsync(incident);

        var t0 = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
        await ctx.IncidentStatusHistory.InsertManyAsync(
        [
            new IncidentStatusChange { IncidentId = "GQL_SIG", At = t0, Code = 3, Label = "Despacho" },
            new IncidentStatusChange { IncidentId = "GQL_SIG", At = t0.AddMinutes(10), Code = 6, Label = "Chegada ao TO" },
        ]);

        using var doc = await fixture.GraphQLAsync(Query, new { id = "GQL_SIG" });
        var incidentNode = doc.RootElement.GetProperty("data").GetProperty("incident");

        var signals = incidentNode.GetProperty("signals");
        Assert.True(signals.GetProperty("escalating").GetBoolean());
        Assert.Equal(42, signals.GetProperty("peakAssets").GetInt32());
        Assert.True(signals.GetProperty("rekindle").GetBoolean());
        Assert.Equal("PRIOR9", signals.GetProperty("rekindleOfId").GetString());
        Assert.True(signals.GetProperty("criticalConditions").GetBoolean());
        var reasons = signals.GetProperty("criticalReasons").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { SignalRules.TempAbove30, SignalRules.WindAbove30 }, reasons);

        var rt = incidentNode.GetProperty("responseTimes");
        Assert.Equal(600, rt.GetProperty("dispatchToArrivalSeconds").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, rt.GetProperty("totalSeconds").ValueKind);
    }

    [SkippableFact]
    public async Task Absent_signals_map_to_defaults_and_response_times_are_null()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);

        await ctx.Incidents.InsertOneAsync(SeedData.Incident("GQL_BARE"));

        using var doc = await fixture.GraphQLAsync(Query, new { id = "GQL_BARE" });
        var incidentNode = doc.RootElement.GetProperty("data").GetProperty("incident");

        var signals = incidentNode.GetProperty("signals");
        Assert.False(signals.GetProperty("escalating").GetBoolean());
        Assert.False(signals.GetProperty("rekindle").GetBoolean());
        Assert.False(signals.GetProperty("criticalConditions").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, signals.GetProperty("peakAssets").ValueKind);
        Assert.Empty(signals.GetProperty("criticalReasons").EnumerateArray());

        Assert.Equal(System.Text.Json.JsonValueKind.Null, incidentNode.GetProperty("responseTimes").ValueKind);
    }
}
