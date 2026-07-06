using Fogos.Domain.Locations;
using Fogos.Infrastructure.Ingest;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Incidents;

/// <summary>
/// The coordinate-based location fallback: a concelho name that misses the <c>locations</c> table is inferred
/// from the incident's coordinates (never dropped), the inference self-heals an alias row so the next sweep
/// hits the fast name path, and an unusable fix still yields the non-null "Desconhecido"/"0000" sentinel.
/// Uses GUID-suffixed feed names so the process-lifetime ops dedup and the alias upsert stay isolated per test.
/// </summary>
[Collection("fogos")]
public sealed class LocationInferenceTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Name_hit_resolves_authoritatively_and_is_not_inferred()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await h.SeedConcelhoAsync("Ourém", "1408", "1408", "14");
        await h.SeedDistrictAsync("Santarém", "14");

        var info = await h.Resolver.ResolveAsync(new RawIncident { Id = "N1", Concelho = "Ourém", Lat = 39.6, Lng = -8.4 });

        Assert.NotNull(info);
        Assert.False(info!.Inferred);
        Assert.Equal("1408", info.Dico);
        Assert.Equal("Santarém", info.District);
    }

    [SkippableFact]
    public async Task Name_miss_with_good_coords_infers_from_polygon_and_self_heals_an_alias()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        // The district table is seeded but the concelho alias is not — the fallback back-fills the alias.
        await h.SeedDistrictAsync("Viseu", "18");
        var feedName = "Vzl-" + Guid.NewGuid().ToString("N")[..8];

        // First resolve: name misses → inferred from the Vouzela fire coordinates (DICO 1824, Viseu).
        var first = await h.Resolver.ResolveAsync(new RawIncident { Id = "I1", Concelho = feedName, Lat = 40.680513, Lng = -8.15205 });

        Assert.NotNull(first);
        Assert.True(first!.Inferred);
        Assert.Equal("1824", first.Dico);
        Assert.Equal("Viseu", first.District);
        Assert.Equal("Vouzela", first.Concelho); // canonical polygon name, not the feed string

        // The alias row was upserted so the name path can serve it next time.
        var alias = await h.Mongo.Locations
            .Find(Builders<Location>.Filter.Eq(x => x.Level, LocationLevel.Concelho) & Builders<Location>.Filter.Eq(x => x.Name, feedName))
            .FirstOrDefaultAsync();
        Assert.NotNull(alias);
        Assert.Equal("1824", alias!.Dico);
        Assert.Equal("1824", alias.Code); // 4-char DICO → DeriveDistrictCode reads "18" = Viseu
        Assert.True(alias.Inferred);

        // Second resolve of the same feed name now hits the authoritative name path (no longer inferred).
        var second = await h.Resolver.ResolveAsync(new RawIncident { Id = "I2", Concelho = feedName, Lat = 40.680513, Lng = -8.15205 });
        Assert.NotNull(second);
        Assert.False(second!.Inferred);
        Assert.Equal("1824", second.Dico);
        Assert.Equal("Viseu", second.District);

        // Inference notice fired exactly once for this feed name despite two resolves (process-lifetime dedup).
        Assert.Single(h.Ops.Infos, m => m.Contains(feedName));
    }

    [SkippableFact]
    public async Task Name_miss_without_usable_coords_yields_the_non_null_sentinel()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        var feedName = "Nowhere-" + Guid.NewGuid().ToString("N")[..8];

        // (0,0) is the feed's "no fix" placeholder — the fallback must not use it, but must never return null.
        var info = await h.Resolver.ResolveAsync(new RawIncident { Id = "S1", Concelho = feedName, Lat = 0, Lng = 0 });

        Assert.NotNull(info);
        Assert.True(info!.Inferred);
        Assert.Equal("0000", info.Dico);
        Assert.Equal("Desconhecido", info.District);
        Assert.Equal(LocationResolver.Title(feedName), info.Concelho);
    }

    [SkippableFact]
    public async Task Spain_and_icnf_preresolved_paths_are_not_inferred()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);

        var spain = await h.Resolver.ResolveAsync(new RawIncident { Id = "SP", SpainOverride = true, Concelho = "Espanha" });
        Assert.False(spain!.Inferred);
        Assert.Equal("00", spain.Dico);

        var icnf = await h.Resolver.ResolveAsync(new RawIncident
        {
            Id = "IC", Concelho = "Ourém", PreResolvedDistrict = "Santarém", PreResolvedDico = "1408",
        });
        Assert.False(icnf!.Inferred);
        Assert.Equal("1408", icnf.Dico);
        Assert.Equal("Santarém", icnf.District);
    }
}
