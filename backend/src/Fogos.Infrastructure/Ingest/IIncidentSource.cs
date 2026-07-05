namespace Fogos.Infrastructure.Ingest;

/// <summary>
/// A pluggable incident feed. The active ingester (<see cref="ArcGisOcorrenciasSource"/>) and the
/// registered-but-unscheduled fallback (<see cref="AnepcApiSource"/>) both fetch + parse into
/// <see cref="RawIncident"/>s; the pipeline downstream is source-agnostic (ANALYSIS.md §3).
/// </summary>
public interface IIncidentSource
{
    Task<IReadOnlyList<RawIncident>> FetchAsync(CancellationToken ct = default);
}
