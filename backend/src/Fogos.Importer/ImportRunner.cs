using Fogos.Importer.Mapping;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Importer;

/// <summary>Per-collection outcome tallies for the import report.</summary>
public sealed class CollectionReport
{
    public required string Collection { get; init; }
    public required string Target { get; init; }
    public long Read { get; set; }
    public long Mapped { get; set; }
    public long Upserted { get; set; }
    public long Quarantined { get; set; }
    public long Skipped { get; set; }

    /// <summary>Set when the collection aborted (mapper unavailable, cursor/write failure).</summary>
    public string? FatalError { get; set; }

    public bool Failed => FatalError is not null;
}

/// <summary>Options controlling one import run.</summary>
public sealed record ImportSettings
{
    public required IReadOnlyList<string> Collections { get; init; }
    public DateTimeOffset? Since { get; init; }
    public bool DryRun { get; init; }
    public int BatchSize { get; init; } = 500;
}

/// <summary>
/// Streams each requested legacy collection in batches, runs its mapper, and bulk-upserts the
/// resulting new-schema entities (ReplaceOne, IsUpsert, keyed on the target <c>_id</c>). Unmappable
/// docs are written to <c>import_quarantine</c>; deliberate skips are counted only. Idempotent:
/// upserts key on business/natural/carried-over ids, so a re-run is equivalent to one run.
/// </summary>
public sealed class ImportRunner(
    IMongoDatabase source,
    IMongoDatabase target,
    MapperRegistry registry)
{
    private static readonly ReplaceOptions Upsert = new() { IsUpsert = true };

    public async Task<IReadOnlyList<CollectionReport>> RunAsync(ImportSettings settings, CancellationToken ct = default)
    {
        var reports = new List<CollectionReport>();
        foreach (var collection in settings.Collections)
        {
            var report = await RunCollectionAsync(collection, settings, ct);
            reports.Add(report);
            PrintReport(report, settings.DryRun);
        }
        return reports;
    }

    private async Task<CollectionReport> RunCollectionAsync(string collection, ImportSettings settings, CancellationToken ct)
    {
        if (!registry.TryGet(collection, out var mapper))
        {
            return new CollectionReport
            {
                Collection = collection,
                Target = "(unknown)",
                FatalError = $"no mapper registered for legacy collection '{collection}'",
            };
        }

        var report = new CollectionReport { Collection = collection, Target = mapper.TargetDescription };
        var quarantine = target.GetCollection<BsonDocument>("import_quarantine");
        var pendingByCollection = new Dictionary<string, List<ReplaceOneModel<BsonDocument>>>(StringComparer.Ordinal);
        var pendingQuarantine = new List<BsonDocument>();

        try
        {
            var filter = BuildSinceFilter(settings.Since);
            var options = new FindOptions<BsonDocument> { BatchSize = settings.BatchSize };
            using var cursor = await source.GetCollection<BsonDocument>(collection).FindAsync(filter, options, ct);

            while (await cursor.MoveNextAsync(ct))
            {
                foreach (var doc in cursor.Current)
                {
                    report.Read++;
                    var result = SafeMap(mapper, doc);
                    switch (result)
                    {
                        case MapResult.Mapped mapped:
                            report.Mapped++;
                            foreach (var entity in mapped.Entities)
                                Enqueue(pendingByCollection, entity, ref report);
                            break;
                        case MapResult.Skipped:
                            report.Skipped++;
                            break;
                        case MapResult.Quarantined q:
                            report.Quarantined++;
                            pendingQuarantine.Add(QuarantineDocument(collection, q.Reason, doc));
                            break;
                    }

                    await FlushIfFullAsync(pendingByCollection, pendingQuarantine, quarantine, settings, report, ct);
                }
            }

            await FlushAsync(pendingByCollection, pendingQuarantine, quarantine, settings, report, ct);
        }
        catch (Exception ex)
        {
            report.FatalError = $"{ex.GetType().Name}: {ex.Message}";
        }

        return report;
    }

    private void Enqueue(
        Dictionary<string, List<ReplaceOneModel<BsonDocument>>> pending,
        MappedEntity entity,
        ref CollectionReport report)
    {
        var bson = entity.Entity.ToBsonDocument(entity.Entity.GetType());
        var id = bson.GetValue("_id", BsonNull.Value);
        var model = new ReplaceOneModel<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", id), bson)
        { IsUpsert = true };

        if (!pending.TryGetValue(entity.Collection, out var list))
            pending[entity.Collection] = list = [];
        list.Add(model);
        report.Upserted++;
    }

    private async Task FlushIfFullAsync(
        Dictionary<string, List<ReplaceOneModel<BsonDocument>>> pending,
        List<BsonDocument> pendingQuarantine,
        IMongoCollection<BsonDocument> quarantine,
        ImportSettings settings,
        CollectionReport report,
        CancellationToken ct)
    {
        var total = pending.Values.Sum(v => v.Count) + pendingQuarantine.Count;
        if (total >= settings.BatchSize)
            await FlushAsync(pending, pendingQuarantine, quarantine, settings, report, ct);
    }

    private async Task FlushAsync(
        Dictionary<string, List<ReplaceOneModel<BsonDocument>>> pending,
        List<BsonDocument> pendingQuarantine,
        IMongoCollection<BsonDocument> quarantine,
        ImportSettings settings,
        CollectionReport report,
        CancellationToken ct)
    {
        if (!settings.DryRun)
        {
            foreach (var (name, models) in pending)
            {
                if (models.Count == 0) continue;
                await target.GetCollection<BsonDocument>(name).BulkWriteAsync(
                    models, new BulkWriteOptions { IsOrdered = false }, ct);
            }
            if (pendingQuarantine.Count > 0)
                await quarantine.InsertManyAsync(pendingQuarantine, cancellationToken: ct);
        }

        foreach (var list in pending.Values)
            list.Clear();
        pendingQuarantine.Clear();
    }

    private static MapResult SafeMap(Mapping.ILegacyCollectionMapper mapper, BsonDocument doc)
    {
        try
        {
            return mapper.Map(doc);
        }
        catch (Exception ex)
        {
            // Any unexpected mapping/serialization fault quarantines the doc rather than aborting the run.
            return MapResult.Quarantine($"mapping threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The quarantine record shape: <c>{legacyCollection, reason, doc, importedAt}</c>.</summary>
    public static BsonDocument QuarantineDocument(string collection, string reason, BsonDocument doc) => new()
    {
        ["legacyCollection"] = collection,
        ["reason"] = reason,
        ["doc"] = doc,
        ["importedAt"] = BsonValue.Create(DateTime.UtcNow),
    };

    /// <summary>Delta filter on <c>updated</c> (fallback <c>created</c>) &gt;= since. Legacy dates are BSON dates.</summary>
    private static FilterDefinition<BsonDocument> BuildSinceFilter(DateTimeOffset? since)
    {
        if (since is null)
            return FilterDefinition<BsonDocument>.Empty;

        var t = new BsonDateTime(since.Value.UtcDateTime);
        var b = Builders<BsonDocument>.Filter;
        return b.Or(
            b.Gte("updated", t),
            b.And(b.Not(b.Exists("updated")), b.Gte("created", t)));
    }

    private static void PrintReport(CollectionReport r, bool dryRun)
    {
        var verb = dryRun ? "would upsert" : "upserted";
        if (r.Failed)
        {
            Console.Error.WriteLine($"  {r.Collection,-20} FAILED: {r.FatalError}");
            return;
        }
        Console.WriteLine(
            $"  {r.Collection,-20} → {r.Target,-32} read {r.Read,7} | mapped {r.Mapped,7} | " +
            $"{verb} {r.Upserted,7} | quarantined {r.Quarantined,6} | skipped {r.Skipped,6}");
    }
}
