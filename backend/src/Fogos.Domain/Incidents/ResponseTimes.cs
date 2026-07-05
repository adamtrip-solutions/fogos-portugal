namespace Fogos.Domain.Incidents;

/// <summary>
/// Operational response durations derived from an incident's status-change log (no stored state).
/// Each component is null when either endpoint transition is missing or the ordering is inverted.
/// </summary>
public sealed record ResponseTimes(
    int? DispatchToArrivalSeconds,
    int? ArrivalToControlSeconds,
    int? ControlToConclusionSeconds,
    int? TotalSeconds)
{
    private static readonly int[] Dispatch = [IncidentStatusCatalog.Despacho, IncidentStatusCatalog.DespachoPrimeiroAlerta];
    private static readonly int[] DispatchOrInProgress =
        [IncidentStatusCatalog.Despacho, IncidentStatusCatalog.DespachoPrimeiroAlerta, IncidentStatusCatalog.EmCurso];

    /// <summary>
    /// Computes the response times from the status history. Returns null when the log is empty.
    /// Each transition endpoint is the earliest logged entry with the relevant code.
    /// </summary>
    public static ResponseTimes? From(IReadOnlyList<IncidentStatusChange> statusHistory)
    {
        if (statusHistory.Count == 0)
            return null;

        var dispatch = FirstOf(statusHistory, Dispatch);
        var arrival = FirstOf(statusHistory, [IncidentStatusCatalog.ChegadaAoTeatroDeOperacoes]);
        var control = FirstOf(statusHistory, [IncidentStatusCatalog.EmResolucao]);
        var conclusion = FirstOf(statusHistory, [IncidentStatusCatalog.Conclusao]);
        var start = FirstOf(statusHistory, DispatchOrInProgress);

        return new ResponseTimes(
            Seconds(dispatch, arrival),
            Seconds(arrival, control),
            Seconds(control, conclusion),
            Seconds(start, conclusion));
    }

    /// <summary>Earliest logged transition whose code is one of <paramref name="codes"/>.</summary>
    private static DateTimeOffset? FirstOf(IReadOnlyList<IncidentStatusChange> history, int[] codes)
    {
        DateTimeOffset? earliest = null;
        foreach (var change in history)
        {
            if (Array.IndexOf(codes, change.Code) < 0)
                continue;
            if (earliest is null || change.At < earliest)
                earliest = change.At;
        }
        return earliest;
    }

    private static int? Seconds(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is not { } a || to is not { } b || b < a)
            return null;
        return (int)Math.Round((b - a).TotalSeconds);
    }
}
