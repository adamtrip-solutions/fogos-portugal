using System.Globalization;
using Fogos.Domain.Incidents;
using Fogos.Domain.Stats;
using Fogos.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>One Lisbon-local day and its fire-ignition count.</summary>
public sealed record IgnitionDayRow(DateOnly Date, int Count);

/// <summary>One Lisbon-local day and that day's (non-cumulative) accounted burn area in hectares.</summary>
public sealed record BurnAreaDayRow(DateOnly Date, double TotalHa);

/// <summary>Ignition count and accounted burn area for one ICNF cause family.</summary>
public sealed record CauseRow(string CauseFamily, int Count, double BurnAreaHa);

/// <summary>Per-district totals for the false-alarm rate (rate computed by the caller).</summary>
public sealed record DistrictFalseAlarmRow(string District, int Total, int FalseAlarms);

/// <summary>Per-incident first-transition durations (seconds), each null when unavailable/inverted.</summary>
public sealed record ResponseTimePair(int? DispatchToArrivalSeconds, int? ArrivalToControlSeconds);

/// <summary>Aggregate read queries backing the <c>stats</c> field. Time windows are computed by the caller (Lisbon-local).</summary>
public sealed class StatsReads(MongoContext context)
{
    private const string LisbonTz = "Europe/Lisbon";
    public async Task<int> ActiveCountAsync(bool fire, CancellationToken ct = default)
    {
        var f = Builders<Incident>.Filter;
        var kind = fire
            ? f.Eq(x => x.Kind, IncidentKind.Fire)
            : f.Ne(x => x.Kind, IncidentKind.Fire);
        return (int)await context.Incidents.CountDocumentsAsync(f.Eq(x => x.Active, true) & kind, cancellationToken: ct);
    }

    /// <summary>Fire ignitions whose occurrence falls in [from, to).</summary>
    public async Task<int> IgnitionCountAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var f = Builders<Incident>.Filter;
        var filter = f.Eq(x => x.Kind, IncidentKind.Fire)
                     & f.Gte(x => x.OccurredAt, from)
                     & f.Lt(x => x.OccurredAt, to);
        return (int)await context.Incidents.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    /// <summary>Occurrence instants of fire ignitions in [from, to) — caller buckets by Lisbon hour.</summary>
    public async Task<IReadOnlyList<DateTimeOffset>> IgnitionOccurredAtsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var f = Builders<Incident>.Filter;
        var filter = f.Eq(x => x.Kind, IncidentKind.Fire)
                     & f.Gte(x => x.OccurredAt, from)
                     & f.Lt(x => x.OccurredAt, to);
        var items = await context.Incidents
            .Find(filter)
            .Project(x => x.OccurredAt)
            .ToListAsync(ct);
        return items;
    }

    public async Task<HistoryTotal?> LatestTotalsAsync(CancellationToken ct = default) =>
        await context.HistoryTotals
            .Find(Builders<HistoryTotal>.Filter.Empty)
            .Sort(Builders<HistoryTotal>.Sort.Descending(x => x.At))
            .FirstOrDefaultAsync(ct);

    /// <summary>Sum of <c>icnf.burnArea.total</c> for fires occurring in [from, to).</summary>
    public async Task<double> BurnAreaTotalHaAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument
            {
                { "kind", IncidentKind.Fire.ToString() },
                { "occurredAt", new BsonDocument { { "$gte", from.UtcDateTime }, { "$lt", to.UtcDateTime } } },
                { "icnf.burnArea.total", new BsonDocument("$ne", BsonNull.Value) },
            }),
            new("$group", new BsonDocument { { "_id", BsonNull.Value }, { "total", new BsonDocument("$sum", "$icnf.burnArea.total") } }),
        };
        var row = await context.Incidents
            .Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
            .FirstOrDefaultAsync(ct);
        return row is null ? 0 : row["total"].ToDouble();
    }

    // ── Season analytics (year = Lisbon calendar year; from/to are its UTC bounds) ──────────────────

    private static BsonDocument OccurredInYear(DateTimeOffset from, DateTimeOffset to) =>
        new("occurredAt", new BsonDocument { { "$gte", from.UtcDateTime }, { "$lt", to.UtcDateTime } });

    private static BsonDocument LisbonDayString =>
        new("$dateToString", new BsonDocument { { "date", "$occurredAt" }, { "format", "%Y-%m-%d" }, { "timezone", LisbonTz } });

    private static DateOnly ParseDay(string yyyyMMdd) =>
        DateOnly.ParseExact(yyyyMMdd, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Fire ignitions grouped by Lisbon calendar day in [from, to) — days with none are omitted (caller fills gaps).</summary>
    public async Task<IReadOnlyList<IgnitionDayRow>> IgnitionsByDayAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument { { "kind", IncidentKind.Fire.ToString() } }.Merge(OccurredInYear(from, to))),
            new("$group", new BsonDocument { { "_id", LisbonDayString }, { "count", new BsonDocument("$sum", 1) } }),
        };
        var rows = await context.Incidents.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);
        return rows.Select(r => new IgnitionDayRow(ParseDay(r["_id"].AsString), r["count"].ToInt32())).ToList();
    }

    /// <summary>Per-Lisbon-day accounted burn area (ha) for fires in [from, to) — days with none omitted (caller fills/cumulates).</summary>
    public async Task<IReadOnlyList<BurnAreaDayRow>> BurnAreaByDayAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument
            {
                { "kind", IncidentKind.Fire.ToString() },
                { "icnf.burnArea.total", new BsonDocument("$ne", BsonNull.Value) },
            }.Merge(OccurredInYear(from, to))),
            new("$group", new BsonDocument { { "_id", LisbonDayString }, { "total", new BsonDocument("$sum", "$icnf.burnArea.total") } }),
        };
        var rows = await context.Incidents.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);
        return rows.Select(r => new BurnAreaDayRow(ParseDay(r["_id"].AsString), r["total"].ToDouble())).ToList();
    }

    /// <summary>Fire counts + accounted burn area grouped by ICNF cause family (null → "Desconhecida"), count desc.</summary>
    public async Task<IReadOnlyList<CauseRow>> CauseBreakdownAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument { { "kind", IncidentKind.Fire.ToString() } }.Merge(OccurredInYear(from, to))),
            new("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$ifNull", new BsonArray { "$icnf.causeFamily", "Desconhecida" }) },
                { "count", new BsonDocument("$sum", 1) },
                { "burnAreaHa", new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray { "$icnf.burnArea.total", 0 })) },
            }),
            new("$sort", new BsonDocument { { "count", -1 }, { "_id", 1 } }),
        };
        var rows = await context.Incidents.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);
        return rows.Select(r => new CauseRow(r["_id"].AsString, r["count"].ToInt32(), r["burnAreaHa"].ToDouble())).ToList();
    }

    /// <summary>Per-district false-alarm totals over ALL incident kinds in [from, to); districts with ≥ minTotal only.</summary>
    public async Task<IReadOnlyList<DistrictFalseAlarmRow>> FalseAlarmStatsAsync(DateTimeOffset from, DateTimeOffset to, int minTotal, CancellationToken ct = default)
    {
        var falseAlarm = new BsonDocument("$cond", new BsonArray
        {
            new BsonDocument("$and", new BsonArray
            {
                new BsonDocument("$in", new BsonArray { "$status.code", new BsonArray { IncidentStatusCatalog.FalsoAlarme, IncidentStatusCatalog.FalsoAlerta } }),
                new BsonDocument("$ne", new BsonArray { "$active", true }),
            }),
            1, 0,
        });

        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument().Merge(OccurredInYear(from, to))),
            new("$group", new BsonDocument
            {
                { "_id", "$district" },
                { "total", new BsonDocument("$sum", 1) },
                { "falseAlarms", new BsonDocument("$sum", falseAlarm) },
            }),
            new("$match", new BsonDocument("total", new BsonDocument("$gte", minTotal))),
        };
        var rows = await context.Incidents.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);
        return rows
            .Select(r => new DistrictFalseAlarmRow(r["_id"].IsBsonNull ? "" : r["_id"].AsString, r["total"].ToInt32(), r["falseAlarms"].ToInt32()))
            .ToList();
    }

    /// <summary>Ids of fires occurring in [from, to), optionally within a district (response-time universe).</summary>
    public async Task<IReadOnlyList<string>> FireIdsAsync(DateTimeOffset from, DateTimeOffset to, string? district, CancellationToken ct = default)
    {
        var f = Builders<Incident>.Filter;
        var filter = f.Eq(x => x.Kind, IncidentKind.Fire) & f.Gte(x => x.OccurredAt, from) & f.Lt(x => x.OccurredAt, to);
        if (!string.IsNullOrWhiteSpace(district))
            filter &= f.Eq(x => x.District, district);
        return await context.Incidents.Find(filter).Project(x => x.Id).ToListAsync(ct);
    }

    /// <summary>
    /// First-transition response-time pairs for the given incidents: per incident the earliest dispatch
    /// (3|4) → arrival (6) and arrival (6) → control (7) durations. One row per incident that logged any
    /// of those transitions; each component null when an endpoint is missing or the ordering is inverted.
    /// </summary>
    public async Task<IReadOnlyList<ResponseTimePair>> ResponseTimePairsAsync(IReadOnlyList<string> incidentIds, CancellationToken ct = default)
    {
        if (incidentIds.Count == 0)
            return [];

        var relevant = new BsonArray
        {
            IncidentStatusCatalog.Despacho, IncidentStatusCatalog.DespachoPrimeiroAlerta,
            IncidentStatusCatalog.ChegadaAoTeatroDeOperacoes, IncidentStatusCatalog.EmResolucao,
        };
        var dispatchCodes = new BsonArray { IncidentStatusCatalog.Despacho, IncidentStatusCatalog.DespachoPrimeiroAlerta };

        BsonDocument MinWhen(BsonDocument codeMatch) =>
            new("$min", new BsonDocument("$cond", new BsonArray { codeMatch, "$at", BsonNull.Value }));

        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument
            {
                { "incidentId", new BsonDocument("$in", new BsonArray(incidentIds)) },
                { "code", new BsonDocument("$in", relevant) },
            }),
            new("$group", new BsonDocument
            {
                { "_id", "$incidentId" },
                { "dispatch", MinWhen(new BsonDocument("$in", new BsonArray { "$code", dispatchCodes })) },
                { "arrival", MinWhen(new BsonDocument("$eq", new BsonArray { "$code", IncidentStatusCatalog.ChegadaAoTeatroDeOperacoes })) },
                { "control", MinWhen(new BsonDocument("$eq", new BsonArray { "$code", IncidentStatusCatalog.EmResolucao })) },
            }),
        };

        var rows = await context.IncidentStatusHistory.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);

        static int? Seconds(BsonValue from, BsonValue to)
        {
            if (from.IsBsonNull || to.IsBsonNull)
                return null;
            var a = from.ToUniversalTime();
            var b = to.ToUniversalTime();
            return b < a ? null : (int)Math.Round((b - a).TotalSeconds);
        }

        return rows
            .Select(r => new ResponseTimePair(Seconds(r["dispatch"], r["arrival"]), Seconds(r["arrival"], r["control"])))
            .ToList();
    }

    // ── Concelho-scoped counts (concelho profile) ───────────────────────────────────────────────────

    /// <summary>Fire ignitions in [from, to) within a concelho (by DICO).</summary>
    public async Task<int> ConcelhoIgnitionCountAsync(string dico, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var f = Builders<Incident>.Filter;
        var filter = f.Eq(x => x.Kind, IncidentKind.Fire) & f.Eq(x => x.Dico, dico)
                     & f.Gte(x => x.OccurredAt, from) & f.Lt(x => x.OccurredAt, to);
        return (int)await context.Incidents.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    /// <summary>Accounted ICNF burn area (ha) for fires in [from, to) within a concelho (by DICO).</summary>
    public async Task<double> ConcelhoBurnAreaHaAsync(string dico, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument
            {
                { "kind", IncidentKind.Fire.ToString() },
                { "dico", dico },
                { "occurredAt", new BsonDocument { { "$gte", from.UtcDateTime }, { "$lt", to.UtcDateTime } } },
                { "icnf.burnArea.total", new BsonDocument("$ne", BsonNull.Value) },
            }),
            new("$group", new BsonDocument { { "_id", BsonNull.Value }, { "total", new BsonDocument("$sum", "$icnf.burnArea.total") } }),
        };
        var row = await context.Incidents.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).FirstOrDefaultAsync(ct);
        return row is null ? 0 : row["total"].ToDouble();
    }
}
