using System.Globalization;
using System.Xml.Linq;

namespace Fogos.Worker.Jobs.Icnf;

/// <summary>One ICNF occurrence parsed from <c>webserviceocorrencias.asp</c> (the <c>&lt;CODIGO&gt;</c> node).</summary>
public sealed record IcnfOccurrence
{
    public string? DataAlerta { get; init; }
    public string? HoraAlerta { get; init; }

    /// <summary>Extinction datetime (Lisbon-local <c>dd-MM-yyyy HH:mm:ss</c>); primary source for the real close time.</summary>
    public string? Dhfim { get; init; }

    /// <summary>Extinction date, the fallback pair for <see cref="Dhfim"/> when it is absent.</summary>
    public string? DataExtincao { get; init; }
    public string? HoraExtincao { get; init; }

    public string? Distrito { get; init; }
    public string? Concelho { get; init; }
    public string? Freguesia { get; init; }
    public string? Local { get; init; }
    public string? Ine { get; init; }
    public double? Lat { get; init; }
    public double? Lon { get; init; }

    public double? AreaTotal { get; init; }
    public double? AreaPovoamento { get; init; }
    public double? AreaAgricola { get; init; }
    public double? AreaMato { get; init; }

    // ── ICNF TIPO flags (0/1) — the occurrence's declared fire nature (see IcnfNatureza) ──────────
    public bool Incendio { get; init; }
    public bool Agricola { get; init; }
    public bool Fogacho { get; init; }
    public bool Queimada { get; init; }
    public bool FalsoAlarme { get; init; }

    public string? FonteAlerta { get; init; }
    public string? Causa { get; init; }
    public string? TipoCausa { get; init; }
    public string? CausaFamilia { get; init; }

    /// <summary>KML perimeter URL if the occurrence carries one (GTF preferred over GNR, as in legacy).</summary>
    public string? KmlUrl { get; init; }
}

/// <summary>
/// Parses the ICNF per-occurrence XML (ProcessICNFNewFireData / ProcessICNFFireData). Mirrors the legacy
/// <c>$xml-&gt;CODIGO-&gt;FIELD</c> reads; returns null when the document has no <c>CODIGO</c> node.
/// </summary>
public static class IcnfOccurrenceXml
{
    public static IcnfOccurrence? Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var codigo = doc.Descendants("CODIGO").FirstOrDefault();
        if (codigo is null)
            return null;

        string? S(string name)
        {
            var value = codigo.Element(name)?.Value?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        double? D(string name) =>
            double.TryParse(S(name), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

        // Legacy boolval((int)$data->FLAG): "1" is true, anything else (absent, "0", blank) is false.
        bool B(string name) => int.TryParse(S(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v != 0;

        var kmlUrl = S("AREASFICHEIROS_GTF") ?? S("AREASFICHEIROS_GNR");

        return new IcnfOccurrence
        {
            DataAlerta = S("DATAALERTA"),
            HoraAlerta = S("HORAALERTA"),
            Dhfim = S("DHFIM"),
            DataExtincao = S("DATAEXTINCAO"),
            HoraExtincao = S("HORAEXTINCAO"),
            Distrito = S("DISTRITO"),
            Concelho = S("CONCELHO"),
            Freguesia = S("FREGUESIA"),
            Local = S("LOCAL"),
            Ine = S("INE"),
            Lat = D("LAT"),
            Lon = D("LON"),
            AreaTotal = D("AREATOTAL"),
            AreaPovoamento = D("AREAPOV"),
            AreaAgricola = D("AREAAGRIC"),
            AreaMato = D("AREAMATO"),
            Incendio = B("INCENDIO"),
            Agricola = B("AGRICOLA"),
            Fogacho = B("FOGACHO"),
            Queimada = B("QUEIMADA"),
            FalsoAlarme = B("FALSOALARME"),
            FonteAlerta = S("FONTEALERTA"),
            Causa = S("CAUSA"),
            TipoCausa = S("TIPOCAUSA"),
            CausaFamilia = S("CAUSAFAMILIA"),
            KmlUrl = kmlUrl,
        };
    }
}
