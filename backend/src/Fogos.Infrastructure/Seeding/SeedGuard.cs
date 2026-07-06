using System.Reflection;
using System.Text.Json;
using Fogos.Domain.Locations;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Seeding;

/// <summary>
/// Startup verify-and-heal for the static <c>locations</c> reference table (29 distritos + 308 concelhos).
/// <para>
/// Motivating incident: prod once ran for days with an EMPTY <c>locations</c> collection (a routine `db clean`
/// wiped it and nothing reseeded), so <c>LocationResolver</c>'s name path missed every concelho and incident
/// ingestion silently skipped the whole feed. PR #29 made that non-fatal (coordinate inference back-fills an
/// alias row) but the table is static reference data that should simply never be empty — so we heal it at boot
/// rather than merely complaining. This runs as the first hosted service (before the Quartz jobs and the queue
/// consumers) so a degraded first sweep is avoided; PR #29's fallback tolerates one anyway if ordering slips.
/// </para>
/// <para>
/// Split by data ownership: <b>locations</b> are authoritative static data embedded in the assembly, so we
/// heal them (bulk-upsert every seed row). <b>weather_stations</b> come from IPMA (the dev fixture is 3 fake
/// rows), so we only check-and-note — <c>UpdateWeatherStationsJob</c> (daily 03:21 Lisbon) is the authority.
/// </para>
/// <para>
/// Alias-upgrade interaction with <c>LocationResolver</c>: when the table was empty/partial, the resolver
/// self-heals concelho names it can't find by inferring the DICO from the incident's coordinates and upserting
/// an <c>Inferred = true</c> alias row keyed on <c>(Level, Name)</c>. The heal here upserts the CANONICAL seed
/// rows on the same <c>(Level, Name)</c> key with <c>Set(Inferred = false)</c>, so any such alias whose name
/// matches a canonical concelho is upgraded in place (correct Code/Dico, marked non-inferred) rather than
/// duplicated. The fast-path count therefore only tallies canonical rows (<c>Inferred != true</c>).
/// </para>
/// <para>
/// Concurrency: the API and the Worker both register this and may boot simultaneously. No lock is taken — the
/// upserts are idempotent, keyed on <c>(Level, Name)</c>, so concurrent runs converge on the same rows.
/// Startup is never blocked on failure: everything is wrapped and a fault only logs + best-effort ops error.
/// </para>
/// </summary>
public sealed class SeedGuard(MongoContext mongo, IOpsNotifier ops, ILogger<SeedGuard> logger) : IHostedService
{
    private const string ResourceSuffix = "Seeding.locations.json";

    // Same reader posture the Importer's Seeder uses: web (camelCase) defaults, numeric LocationLevel enums.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await HealLocationsAsync(cancellationToken);
            await CheckWeatherStationsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Never crash the host over seed maintenance — log and best-effort ping ops, then let boot proceed.
            logger.LogError(ex, "SeedGuard failed");
            try
            {
                await ops.ErrorAsync($"SeedGuard failed: {ex.Message}", cancellationToken);
            }
            catch (Exception notifyEx)
            {
                logger.LogError(notifyEx, "SeedGuard failed to notify ops of its own failure");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task HealLocationsAsync(CancellationToken ct)
    {
        // We upsert keyed on (Level, Name), so the target row count is the number of DISTINCT (Level, Name)
        // keys, not the raw seed size: two concelho names legitimately repeat across districts ("Lagoa" in
        // Faro + Açores, "Calheta" in Açores + Madeira), which the key collapses to one row each (337 → 335).
        // FIRST occurrence wins because that is what production has always resolved: LocationResolver's name
        // path is Find(Name).FirstOrDefault() in Mongo natural order, and an Importer-seeded table inserts in
        // seed JSON order — so "Lagoa" has always hit 0806 (Faro) and "Calheta" 3101 (Açores). First-wins
        // makes a healed-from-empty table resolve those names identically to an Importer-seeded one.
        var seed = LoadSeed()
            .GroupBy(r => (r.Level, r.Name))
            .Select(g => g.First())
            .ToList();

        var administrative = Builders<Location>.Filter.In(x => x.Level, [LocationLevel.Distrito, LocationLevel.Concelho]);
        var canonical = administrative & Builders<Location>.Filter.Ne(x => x.Inferred, true);

        var totalBefore = await mongo.Locations.CountDocumentsAsync(administrative, cancellationToken: ct);
        var canonicalBefore = await mongo.Locations.CountDocumentsAsync(canonical, cancellationToken: ct);

        // Fast path: enough canonical rows already present — nothing to heal, don't touch the collection.
        if (canonicalBefore >= seed.Count)
        {
            logger.LogInformation(
                "SeedGuard: locations already seeded ({CanonicalBefore} canonical rows >= {SeedCount} seed rows), skipping heal",
                canonicalBefore, seed.Count);
            return;
        }

        // Upsert every canonical row keyed on (Level, Name). update-with-Set (not Replace) preserves existing
        // _id values — Location's string Id maps to an ObjectId (immutable on upsert), so we never Set the Id.
        // Overwriting Code/Dico and forcing Inferred=false upgrades any PR-#29 self-healed alias in place.
        var writes = new List<WriteModel<Location>>(seed.Count);
        foreach (var row in seed)
        {
            var filter = Builders<Location>.Filter.Eq(x => x.Level, row.Level)
                         & Builders<Location>.Filter.Eq(x => x.Name, row.Name);
            // Level and Name come from the filter's equality on insert; setting them again would collide.
            var update = Builders<Location>.Update
                .Set(x => x.Code, row.Code)
                .Set(x => x.Inferred, false);
            update = row.Dico is null
                ? update.Unset(x => x.Dico)   // distrito rows carry no DICO
                : update.Set(x => x.Dico, row.Dico);

            writes.Add(new UpdateOneModel<Location>(filter, update) { IsUpsert = true });
        }

        var result = await mongo.Locations.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
        var changed = result.Upserts.Count + result.ModifiedCount;
        var totalAfter = await mongo.Locations.CountDocumentsAsync(administrative, cancellationToken: ct);

        var prefix = totalBefore == 0 ? "⚠️ locations was EMPTY — " : "";
        await ops.InfoAsync(
            $"SeedGuard: {prefix}locations healed — {changed} inserted/updated ({totalBefore} → {totalAfter} rows)", ct);
        logger.LogInformation(
            "SeedGuard: locations healed — {Changed} inserted/updated ({TotalBefore} -> {TotalAfter} rows)",
            changed, totalBefore, totalAfter);
    }

    private async Task CheckWeatherStationsAsync(CancellationToken ct)
    {
        // Check only, never seed: the authoritative source is IPMA (the dev fixture is 3 fake rows).
        var count = await mongo.WeatherStations.CountDocumentsAsync(FilterDefinition<Domain.Weather.WeatherStation>.Empty, cancellationToken: ct);
        if (count == 0)
        {
            await ops.InfoAsync(
                "SeedGuard: weather_stations is empty — UpdateWeatherStationsJob (daily 03:21 Lisbon) will populate it", ct);
            logger.LogInformation("SeedGuard: weather_stations is empty — awaiting UpdateWeatherStationsJob");
        }
    }

    private static List<Location> LoadSeed()
    {
        var assembly = typeof(SeedGuard).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded locations seed '*{ResourceSuffix}' not found.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<List<Location>>(reader.ReadToEnd(), Json) ?? [];
    }
}
