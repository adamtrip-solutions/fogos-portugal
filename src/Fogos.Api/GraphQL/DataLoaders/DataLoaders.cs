using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Risk;
using Fogos.Domain.Time;
using Fogos.Domain.Weather;
using Fogos.Infrastructure.Reads;
using GreenDonut;

namespace Fogos.Api.GraphQL.DataLoaders;

/// <summary>Batches incident lookups by id (used by resolvers and subscription re-hydration).</summary>
public sealed class IncidentByIdDataLoader(IncidentReads reads, IBatchScheduler batchScheduler, DataLoaderOptions? options = null)
    : BatchDataLoader<string, Incident?>(batchScheduler, options!)
{
    protected override async Task<IReadOnlyDictionary<string, Incident?>> LoadBatchAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        var found = await reads.GetByIdsAsync(keys, ct);
        return keys.ToDictionary(k => k, k => found.GetValueOrDefault(k));
    }
}

/// <summary>Groups resource snapshots by incident (desc by time).</summary>
public sealed class IncidentHistoryDataLoader(IncidentReads reads, IBatchScheduler batchScheduler, DataLoaderOptions? options = null)
    : GroupedDataLoader<string, IncidentHistorySnapshot>(batchScheduler, options!)
{
    protected override async Task<ILookup<string, IncidentHistorySnapshot>> LoadGroupedBatchAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        var rows = await reads.HistoryByIncidentsAsync(keys, ct);
        return rows.ToLookup(r => r.IncidentId);
    }
}

/// <summary>Groups status transitions by incident (desc by time).</summary>
public sealed class IncidentStatusHistoryDataLoader(IncidentReads reads, IBatchScheduler batchScheduler, DataLoaderOptions? options = null)
    : GroupedDataLoader<string, IncidentStatusChange>(batchScheduler, options!)
{
    protected override async Task<ILookup<string, IncidentStatusChange>> LoadGroupedBatchAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        var rows = await reads.StatusHistoryByIncidentsAsync(keys, ct);
        return rows.ToLookup(r => r.IncidentId);
    }
}

/// <summary>Groups approved+public photos by incident.</summary>
public sealed class IncidentPhotosDataLoader(IncidentReads reads, IBatchScheduler batchScheduler, DataLoaderOptions? options = null)
    : GroupedDataLoader<string, IncidentPhoto>(batchScheduler, options!)
{
    protected override async Task<ILookup<string, IncidentPhoto>> LoadGroupedBatchAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        var rows = await reads.PublicPhotosByIncidentsAsync(keys, ct);
        return rows.ToLookup(r => r.IncidentId);
    }
}

/// <summary>Batches hotspot documents by incident id.</summary>
public sealed class IncidentHotspotsDataLoader(IncidentReads reads, IBatchScheduler batchScheduler, DataLoaderOptions? options = null)
    : BatchDataLoader<string, Hotspots?>(batchScheduler, options!)
{
    protected override async Task<IReadOnlyDictionary<string, Hotspots?>> LoadBatchAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        var found = await reads.HotspotsByIdsAsync(keys, ct);
        return keys.ToDictionary(k => k, k => found.GetValueOrDefault(k));
    }
}

/// <summary>Batches the latest hourly observation per weather station.</summary>
public sealed class LatestWeatherByStationDataLoader(WeatherReads reads, IBatchScheduler batchScheduler, DataLoaderOptions? options = null)
    : BatchDataLoader<int, WeatherObservation?>(batchScheduler, options!)
{
    protected override async Task<IReadOnlyDictionary<int, WeatherObservation?>> LoadBatchAsync(IReadOnlyList<int> keys, CancellationToken ct)
    {
        var found = await reads.LatestByStationsAsync(keys, ct);
        return keys.ToDictionary(k => k, k => found.GetValueOrDefault(k));
    }
}

/// <summary>Batches weather stations by id (name + coordinates for distance).</summary>
public sealed class WeatherStationByIdDataLoader(WeatherReads reads, IBatchScheduler batchScheduler, DataLoaderOptions? options = null)
    : BatchDataLoader<int, WeatherStation?>(batchScheduler, options!)
{
    protected override async Task<IReadOnlyDictionary<int, WeatherStation?>> LoadBatchAsync(IReadOnlyList<int> keys, CancellationToken ct)
    {
        var found = await reads.StationsByIdsAsync(keys, ct);
        return keys.ToDictionary(k => k, k => found.GetValueOrDefault(k));
    }
}

/// <summary>Batches today's concelho fire risk by DICO for incident.fireRisk.</summary>
public sealed class IncidentFireRiskDataLoader(RiskReads reads, IClock clock, IBatchScheduler batchScheduler, DataLoaderOptions? options = null)
    : BatchDataLoader<string, ConcelhoRisk?>(batchScheduler, options!)
{
    protected override async Task<IReadOnlyDictionary<string, ConcelhoRisk?>> LoadBatchAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        var found = await reads.ByDicosAsync(keys, clock.LisbonToday, ct);
        return keys.ToDictionary(k => k, k => found.GetValueOrDefault(k));
    }
}
