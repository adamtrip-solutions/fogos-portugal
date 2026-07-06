using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;

namespace Fogos.Infrastructure.Ingest;

/// <summary>
/// The diff between a freshly-mapped incident and its stored counterpart, computed per record during
/// ingest. Drives which domain events are dispatched: created, resources delta, status delta. A pure
/// value so the ingest service stays testable.
/// </summary>
public sealed record ChangeSet
{
    public bool Created { get; init; }
    public bool StatusChanged { get; init; }
    public bool ResourcesChanged { get; init; }

    /// <summary>Any change at all (created, status, resources, or a plain field update) — i.e. worth persisting.</summary>
    public bool FieldUpdates { get; init; }

    public IncidentStatus? PreviousStatus { get; init; }
    public Resources? PreviousResources { get; init; }

    /// <summary>Nothing changed — the store already matches; skip the write.</summary>
    public bool Nothing => !Created && !FieldUpdates;

    public static ChangeSet ForNew() => new() { Created = true, FieldUpdates = true };

    public static ChangeSet Diff(Incident stored, Incident mapped)
    {
        var statusChanged = stored.Status.Code != mapped.Status.Code;
        var resourcesChanged = !stored.Resources.Equals(mapped.Resources);
        var fieldUpdates = statusChanged
            || resourcesChanged
            || stored.Location != mapped.Location
            || stored.Natureza != mapped.Natureza
            || stored.NaturezaCode != mapped.NaturezaCode
            || stored.Kind != mapped.Kind
            || stored.Active != mapped.Active
            || stored.Dico != mapped.Dico
            || stored.LocationInferred != mapped.LocationInferred
            || !NullableGeoEquals(stored.Coordinates, mapped.Coordinates)
            || stored.DetailLocation != mapped.DetailLocation
            || stored.Region != mapped.Region
            || stored.SubRegion != mapped.SubRegion
            || !Equals(stored.ArcGis, mapped.ArcGis);

        return new ChangeSet
        {
            Created = false,
            StatusChanged = statusChanged,
            ResourcesChanged = resourcesChanged,
            FieldUpdates = fieldUpdates,
            PreviousStatus = statusChanged ? stored.Status : null,
            PreviousResources = resourcesChanged ? stored.Resources : null,
        };
    }

    private static bool NullableGeoEquals(GeoPoint? a, GeoPoint? b) =>
        (a is null && b is null) || (a is { } x && b is { } y && x.Equals(y));
}
