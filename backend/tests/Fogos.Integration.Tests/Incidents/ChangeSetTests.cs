using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Ingest;

namespace Fogos.Integration.Tests.Incidents;

/// <summary>
/// Pure tests for the ingest diff. Focus: an incident first stored with an inferred location must upgrade in
/// place once the locations table is seeded and the name path resolves it (LocationInferred true → false).
/// </summary>
public sealed class ChangeSetTests
{
    [Fact]
    public void LocationInferred_flip_marks_field_updates()
    {
        var stored = Fire();
        stored.LocationInferred = true;
        var mapped = Fire();
        mapped.LocationInferred = false;

        var change = ChangeSet.Diff(stored, mapped);

        Assert.True(change.FieldUpdates);
        Assert.False(change.StatusChanged);
        Assert.False(change.ResourcesChanged);
    }

    [Fact]
    public void Identical_docs_are_nothing()
    {
        var change = ChangeSet.Diff(Fire(), Fire());
        Assert.True(change.Nothing);
    }

    private static Incident Fire() => new()
    {
        Id = "X",
        OccurredAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        Location = "Viseu, Vouzela, ",
        District = "Viseu",
        Concelho = "Vouzela",
        Dico = "1824",
        Coordinates = GeoPoint.FromLatLng(40.68, -8.15),
        Status = IncidentStatusCatalog.FromCode(IncidentStatusCatalog.EmCurso),
        Kind = IncidentKind.Fire,
        NaturezaCode = "3101",
        Natureza = "Incêndio Florestal",
        Resources = new Resources { Man = 10, Terrain = 5, Aerial = 1 },
        Active = true,
    };
}
