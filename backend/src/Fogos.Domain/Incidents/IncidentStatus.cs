namespace Fogos.Domain.Incidents;

/// <summary>Canonical incident status: ANEPC code + display label. Color derives from the catalog.</summary>
public sealed record IncidentStatus(int Code, string Label)
{
    public string Color => IncidentStatusCatalog.ColorFor(Code);
}
