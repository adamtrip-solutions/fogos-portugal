using System.Collections.Frozen;

namespace Fogos.Domain.Incidents;

/// <summary>
/// ANEPC "natureza" (occurrence nature) classification, ported verbatim from the five
/// NATUREZA_CODE_* arrays in app/Models/Incident.php. Codes are strings on purpose —
/// the feed zero-pads and we never do arithmetic on them.
/// </summary>
public static class NaturezaCatalog
{
    public static readonly FrozenSet<string> Fire =
        new[] { "3101", "3103", "3105", "3111", "3109", "4335" }.ToFrozenSet();

    public static readonly FrozenSet<string> UrbanFire = new[]
    {
        "2101", "2103", "2105", "2107", "2109", "2111", "2113", "2115",
        "2117", "2119", "2121", "2123", "2125", "2127", "2129",
    }.ToFrozenSet();

    public static readonly FrozenSet<string> TransportFire =
        new[] { "2301", "2303", "2305", "2307" }.ToFrozenSet();

    public static readonly FrozenSet<string> OtherFire =
        new[] { "3201", "3203", "2201", "2203", "3107" }.ToFrozenSet();

    public static readonly FrozenSet<string> Fma = new[]
    {
        "3315", "3317", "3301", "4305", "3309", "2419",
        "3313", "3319", "3321", "3329", "4329", "4339",
    }.ToFrozenSet();

    /// <summary>Aero-medical natureza that triggered the legacy Discord aero alert.</summary>
    public const string AeroAlertCode = "2409";

    public static IncidentKind Classify(string naturezaCode) => naturezaCode switch
    {
        _ when Fire.Contains(naturezaCode) => IncidentKind.Fire,
        _ when UrbanFire.Contains(naturezaCode) => IncidentKind.UrbanFire,
        _ when TransportFire.Contains(naturezaCode) => IncidentKind.TransportFire,
        _ when OtherFire.Contains(naturezaCode) => IncidentKind.OtherFire,
        _ when Fma.Contains(naturezaCode) => IncidentKind.Fma,
        _ => IncidentKind.Other,
    };
}
