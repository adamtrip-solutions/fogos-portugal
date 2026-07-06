using Fogos.Domain.Incidents;

namespace Fogos.Worker.Jobs.Icnf;

/// <summary>
/// Derives an ANEPC natureza (code + display label) for an ICNF-only new fire from the occurrence's
/// TIPO flags. ICNF hands us booleans, not natureza codes; before this the new-fire job hardcoded
/// "3103"/"Mato" for every occurrence, so AGRICOLA/FOGACHO/povoamento fires were all filed as Mato.
///
/// Only codes that already live in <see cref="NaturezaCatalog.Fire"/> are emitted, so every mapped
/// occurrence still classifies as <see cref="IncidentKind.Fire"/> and stays in the fire feeds. The
/// labels match the catalog's canonical spelling (see DemoSeed NaturezaLabel: 3101/3103/3105).
///
/// Priority (first match wins — order matters when several flags are set):
///   1. FALSOALARME → no distinct falso-alarme natureza classifies as Fire, so keep the "3103"/Mato
///      default (unchanged behavior; the flag only pins the priority against combos).
///   2. AGRICOLA    → 3105 Incêndio Agrícola.
///   3. QUEIMADA    → 3105 Incêndio Agrícola. ("3107 Queimadas" exists but classifies as OtherFire,
///      which would drop the fire out of the fire feeds — so we use the agricultural fallback.)
///   4. INCENDIO && AREAPOV > 0 → 3101 Incêndio em Povoamento Florestal.
///   5. INCENDIO    → 3103 Incêndio em Mato.
///   6. FOGACHO     → no distinct fogacho natureza classifies as Fire, so 3103 Incêndio em Mato.
///   7. nothing set → 3103 Incêndio em Mato (the legacy default).
/// </summary>
public static class IcnfNatureza
{
    private static readonly (string Code, string Label) Mato = ("3103", "Incêndio em Mato");
    private static readonly (string Code, string Label) Agricola = ("3105", "Incêndio Agrícola");
    private static readonly (string Code, string Label) Povoamento = ("3101", "Incêndio em Povoamento Florestal");

    public static (string Code, string Label) Resolve(IcnfOccurrence occ)
    {
        if (occ.FalsoAlarme)
            return Mato;
        if (occ.Agricola)
            return Agricola;
        if (occ.Queimada)
            return Agricola;
        if (occ.Incendio && occ.AreaPovoamento > 0)
            return Povoamento;
        if (occ.Incendio)
            return Mato;
        if (occ.Fogacho)
            return Mato;
        return Mato;
    }
}
