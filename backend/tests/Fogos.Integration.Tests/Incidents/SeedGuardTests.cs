using Fogos.Domain.Locations;
using Fogos.Infrastructure.Ingest;
using Fogos.Infrastructure.Seeding;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Incidents;

/// <summary>
/// The startup verify-and-heal guard for the static <c>locations</c> table. Covers: an empty table is fully
/// re-seeded (and then resolves canonically), a PR-#29 self-healed alias is upgraded to canonical in place
/// (no duplicate, same <c>_id</c>), a complete table short-circuits the fast path untouched, the
/// weather-stations check notes emptiness only, and a faulted run never escapes <c>StartAsync</c>.
/// </summary>
[Collection("fogos")]
public sealed class SeedGuardTests(ContainerFixture fixture)
{
    private static readonly FilterDefinition<Location> Administrative =
        Builders<Location>.Filter.In(x => x.Level, [LocationLevel.Distrito, LocationLevel.Concelho]);

    private static SeedGuard Guard(IncidentPipelineHarness h, RecordingOps ops) =>
        new(h.Mongo, ops, NullLogger<SeedGuard>.Instance);

    [SkippableFact]
    public async Task Empty_table_is_healed_to_full_seed_and_resolves_canonically()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);

        await Guard(h, h.Ops).StartAsync(CancellationToken.None);

        // 335, not 337: the (Level, Name) upsert key collapses the two cross-district name repeats
        // ("Lagoa", "Calheta") to one row each (see SeedGuard.HealLocationsAsync).
        var rows = await h.Mongo.Locations.CountDocumentsAsync(Administrative);
        Assert.Equal(335, rows);

        // The loud "was EMPTY" ops line fired.
        Assert.Single(h.Ops.Infos, m => m.Contains("locations healed") && m.Contains("EMPTY"));

        // A known concelho now resolves via the authoritative name path (not the coordinate fallback).
        var info = await h.Resolver.ResolveAsync(new RawIncident { Id = "O1", Concelho = "Ourém", Lat = 39.6, Lng = -8.4 });
        Assert.NotNull(info);
        Assert.False(info!.Inferred);
        Assert.Equal("1421", info.Dico);
        Assert.Equal("Santarém", info.District);

        // Cross-district name repeat: first seed occurrence wins the (Level, Name) key, matching what an
        // Importer-seeded table has always resolved (FirstOrDefault in natural order = seed JSON order) —
        // "Lagoa" is the mainland Faro concelho (0806), NOT the Açores one (4201).
        var lagoa = await h.Resolver.ResolveAsync(new RawIncident { Id = "L1", Concelho = "Lagoa" });
        Assert.NotNull(lagoa);
        Assert.False(lagoa!.Inferred);
        Assert.Equal("0806", lagoa.Dico);
        Assert.Equal("Faro", lagoa.District);
    }

    [SkippableFact]
    public async Task Inferred_alias_matching_a_seed_name_is_upgraded_in_place()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);

        // A PR-#29 self-healed alias: correct name, but marked Inferred with the padded-DICO-as-Code shape and
        // a pinned _id so we can prove the heal upgrades this row rather than inserting a duplicate.
        var aliasId = ObjectId.GenerateNewId().ToString();
        await h.Mongo.Locations.InsertOneAsync(new Location
        {
            Id = aliasId, Level = LocationLevel.Concelho, Name = "Ourém", Code = "1421", Dico = "1421", Inferred = true,
        });

        await Guard(h, h.Ops).StartAsync(CancellationToken.None);

        var ourem = await h.Mongo.Locations
            .Find(Builders<Location>.Filter.Eq(x => x.Level, LocationLevel.Concelho) & Builders<Location>.Filter.Eq(x => x.Name, "Ourém"))
            .ToListAsync();

        Assert.Single(ourem); // upgraded, not duplicated
        var row = ourem[0];
        Assert.Equal(aliasId, row.Id); // same _id preserved (update, not replace)
        Assert.False(row.Inferred); // upgraded to canonical
        Assert.Equal("1421", row.Code);
        Assert.Equal("1421", row.Dico);
    }

    [SkippableFact]
    public async Task Complete_table_takes_the_no_op_fast_path()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);

        // First run seeds the table; a station keeps the weather-stations check silent so the second run's ops
        // recorder cleanly proves the fast path writes/announces nothing.
        await Guard(h, h.Ops).StartAsync(CancellationToken.None);
        await h.SeedStationAsync(1, 40.0, -8.0);
        var before = await h.Mongo.Locations.CountDocumentsAsync(Administrative);

        var quietOps = new RecordingOps();
        await Guard(h, quietOps).StartAsync(CancellationToken.None);

        var after = await h.Mongo.Locations.CountDocumentsAsync(Administrative);
        Assert.Equal(before, after);
        Assert.Empty(quietOps.Infos);
        Assert.Empty(quietOps.Errors);
    }

    [SkippableFact]
    public async Task Weather_stations_check_notes_emptiness_only_when_empty()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);

        // Empty weather_stations → the guard notes it (populate deferred to the daily IPMA job).
        await Guard(h, h.Ops).StartAsync(CancellationToken.None);
        Assert.Single(h.Ops.Infos, m => m.Contains("weather_stations is empty"));

        // Now non-empty → a second run stays silent about weather stations.
        await h.SeedStationAsync(2, 41.0, -8.5);
        var secondOps = new RecordingOps();
        await Guard(h, secondOps).StartAsync(CancellationToken.None);
        Assert.DoesNotContain(secondOps.Infos, m => m.Contains("weather_stations"));
    }

    [SkippableFact]
    public async Task Faulted_run_never_throws_out_of_start()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);

        // A pre-canceled token faults the very first Mongo call; StartAsync must swallow it and escalate to ops.
        var canceled = new CancellationToken(canceled: true);
        var record = await Record.ExceptionAsync(() => Guard(h, h.Ops).StartAsync(canceled));

        Assert.Null(record);
        Assert.Contains(h.Ops.Errors, m => m.Contains("SeedGuard failed"));
    }
}
