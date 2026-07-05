using Fogos.Domain.Risk;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>Read queries for fire-risk (RCM) — per-concelho lookups and the stored GeoJSON payloads.</summary>
public sealed class RiskReads(MongoContext context)
{
    /// <summary>Single concelho risk for a forecast run date (matches by concelho name).</summary>
    public async Task<ConcelhoRisk?> ConcelhoAsync(string concelho, DateOnly date, CancellationToken ct = default)
    {
        var f = Builders<ConcelhoRisk>.Filter;
        return await context.RcmDaily
            .Find(f.Eq(x => x.Concelho, concelho) & f.Eq(x => x.Date, date))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Most recent forecast run for a concelho (by DICO), or null when none stored.</summary>
    public async Task<ConcelhoRisk?> LatestByDicoAsync(string dico, CancellationToken ct = default) =>
        await context.RcmDaily
            .Find(Builders<ConcelhoRisk>.Filter.Eq(x => x.Dico, dico))
            .Sort(Builders<ConcelhoRisk>.Sort.Descending(x => x.Date))
            .FirstOrDefaultAsync(ct);

    /// <summary>Latest stored GeoJSON payload for a horizon.</summary>
    public async Task<RiskGeoJson?> GeoJsonAsync(RiskDay day, CancellationToken ct = default) =>
        await context.RcmGeoJson
            .Find(Builders<RiskGeoJson>.Filter.Eq(x => x.When, day))
            .Sort(Builders<RiskGeoJson>.Sort.Descending(x => x.ForecastDate))
            .FirstOrDefaultAsync(ct);

    /// <summary>Batched by DICO for the incident.fireRisk DataLoader (one forecast date).</summary>
    public async Task<IReadOnlyDictionary<string, ConcelhoRisk>> ByDicosAsync(IReadOnlyList<string> dicos, DateOnly date, CancellationToken ct = default)
    {
        var f = Builders<ConcelhoRisk>.Filter;
        var items = await context.RcmDaily
            .Find(f.In(x => x.Dico, dicos) & f.Eq(x => x.Date, date))
            .ToListAsync(ct);
        // A DICO could in theory appear twice across runs of the same date; keep the first.
        var map = new Dictionary<string, ConcelhoRisk>();
        foreach (var r in items)
            map.TryAdd(r.Dico, r);
        return map;
    }
}
