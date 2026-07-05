using Fogos.Domain.Incidents;

namespace Fogos.Domain.Tests;

public class ResponseTimesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

    private static IncidentStatusChange Change(int code, DateTimeOffset at) =>
        new() { IncidentId = "X", At = at, Code = code, Label = IncidentStatusCatalog.FromCode(code).Label };

    [Fact]
    public void Empty_history_yields_null()
    {
        Assert.Null(ResponseTimes.From([]));
    }

    [Fact]
    public void Full_lifecycle_computes_every_component()
    {
        // Despacho(3) @0m, EmCurso(5) @5m, Chegada(6) @10m, Resolução(7) @70m, Conclusão(8) @100m.
        var history = new[]
        {
            Change(3, T0),
            Change(5, T0.AddMinutes(5)),
            Change(6, T0.AddMinutes(10)),
            Change(7, T0.AddMinutes(70)),
            Change(8, T0.AddMinutes(100)),
        };

        var rt = ResponseTimes.From(history);

        Assert.NotNull(rt);
        Assert.Equal(600, rt!.DispatchToArrivalSeconds);        // 3 → 6 = 10 min
        Assert.Equal(3600, rt.ArrivalToControlSeconds);         // 6 → 7 = 60 min
        Assert.Equal(1800, rt.ControlToConclusionSeconds);      // 7 → 8 = 30 min
        Assert.Equal(6000, rt.TotalSeconds);                    // 3 → 8 = 100 min
    }

    [Fact]
    public void Uses_first_alert_code_when_no_plain_dispatch()
    {
        // Only "Despacho de 1º Alerta" (4) as the dispatch endpoint.
        var history = new[]
        {
            Change(4, T0),
            Change(6, T0.AddMinutes(15)),
        };

        var rt = ResponseTimes.From(history);

        Assert.NotNull(rt);
        Assert.Equal(900, rt!.DispatchToArrivalSeconds);
        Assert.Null(rt.ArrivalToControlSeconds);
        Assert.Null(rt.TotalSeconds);
    }

    [Fact]
    public void Missing_endpoint_yields_null_component()
    {
        var history = new[] { Change(3, T0), Change(6, T0.AddMinutes(10)) };

        var rt = ResponseTimes.From(history);

        Assert.NotNull(rt);
        Assert.Equal(600, rt!.DispatchToArrivalSeconds);
        Assert.Null(rt.ArrivalToControlSeconds);
        Assert.Null(rt.ControlToConclusionSeconds);
        Assert.Null(rt.TotalSeconds);
    }

    [Fact]
    public void Inverted_ordering_yields_null_component()
    {
        // Conclusão(8) logged before Resolução(7): controlToConclusion inverts → null.
        var history = new[]
        {
            Change(7, T0.AddMinutes(50)),
            Change(8, T0.AddMinutes(30)),
        };

        var rt = ResponseTimes.From(history);

        Assert.NotNull(rt);
        Assert.Null(rt!.ControlToConclusionSeconds);
    }

    [Fact]
    public void Earliest_entry_per_code_is_used()
    {
        // Two arrivals; the earliest (10 min) anchors the dispatch→arrival span.
        var history = new[]
        {
            Change(3, T0),
            Change(6, T0.AddMinutes(10)),
            Change(6, T0.AddMinutes(40)),
        };

        var rt = ResponseTimes.From(history);

        Assert.Equal(600, rt!.DispatchToArrivalSeconds);
    }
}
