using System.Text.Json;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Sources;

namespace Fogos.Infrastructure.Ingest;

/// <summary>
/// Primary ingester: maps the ArcGIS OcorrenciasSite FeatureServer attribute bag into
/// <see cref="RawIncident"/>s (ProcessOcorrenciasSite.php fetch + prepareData). Attribute names and
/// the ms-epoch date fields are ported verbatim; the natureza label is the part after " - ".
/// </summary>
public sealed class ArcGisOcorrenciasSource(ArcGisClient client) : IIncidentSource
{
    public async Task<IReadOnlyList<RawIncident>> FetchAsync(CancellationToken ct = default)
    {
        var attributes = await client.QueryAllAsync(ct);
        var result = new List<RawIncident>(attributes.Count);
        foreach (var attrs in attributes)
        {
            var raw = Map(attrs);
            if (raw is not null)
                result.Add(raw);
        }
        return result;
    }

    public static RawIncident? Map(IReadOnlyDictionary<string, JsonElement> a)
    {
        var numero = Str(a, "Numero");
        if (string.IsNullOrEmpty(numero))
            return null;

        var occurredAt = ParseWhen(a);
        var aerial = Int(a, "MeiosAereos");
        var terrain = Int(a, "MeiosTerrestres");
        var man = Int(a, "Operacionais");

        return new RawIncident
        {
            Id = numero,
            OccurredAt = occurredAt,
            NaturezaCode = Str(a, "CodNatureza"),
            Natureza = NaturezaLabel(Str(a, "Natureza")),
            StatusLabel = Str(a, "EstadoOcorrencia"),
            Concelho = Str(a, "Concelho"),
            Freguesia = Str(a, "Freguesia"),
            Localidade = string.Join(' ', new[] { Str(a, "Localidade"), Str(a, "Endereco") }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim(),
            Lat = Dbl(a, "Latitude"),
            Lng = Dbl(a, "Longitude"),
            Resources = new Resources { Man = man, Terrain = terrain, Aerial = aerial },
            Region = NullIfEmpty(Str(a, "Regiao")),
            SubRegion = NullIfEmpty(Str(a, "SubRegiao")),
            EstadoAgrupado = NullIfEmpty(Str(a, "EstadoAgrupado")),
            FaseIncendio = NullIfEmpty(Str(a, "FaseIncendio")),
            Rasi = Bool(a, "RASI"),
            DuracaoMinutos = a.ContainsKey("DuracaoMinutos") ? Int(a, "DuracaoMinutos") : null,
            Endereco = NullIfEmpty(Str(a, "Endereco")),
            DataDosDados = ParseMs(a, "DataDosDados"),
        };
    }

    /// <summary>Legacy: DataOcorrencia is a ms epoch; fall back to a "Data Hora" string parsed as-is.</summary>
    private static DateTimeOffset ParseWhen(IReadOnlyDictionary<string, JsonElement> a)
    {
        var ms = ParseMs(a, "DataOcorrencia");
        if (ms is { } value)
            return value;

        var raw = $"{Str(a, "Data")} {Str(a, "Hora")}".Trim();
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed.ToUniversalTime() : DateTimeOffset.UtcNow;
    }

    private static string NaturezaLabel(string natureza)
    {
        var idx = natureza.IndexOf(" - ", StringComparison.Ordinal);
        return (idx >= 0 ? natureza[(idx + 3)..] : natureza).Trim();
    }

    // ── JsonElement readers (ArcGIS mixes strings and numbers per field) ───────────────────────────
    private static string Str(IReadOnlyDictionary<string, JsonElement> a, string key)
    {
        if (!a.TryGetValue(key, out var v))
            return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }

    private static int Int(IReadOnlyDictionary<string, JsonElement> a, string key)
    {
        if (!a.TryGetValue(key, out var v))
            return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var i) ? i : (int)v.GetDouble(),
            JsonValueKind.String => int.TryParse(v.GetString(), out var i) ? i : 0,
            _ => 0,
        };
    }

    private static double? Dbl(IReadOnlyDictionary<string, JsonElement> a, string key)
    {
        if (!a.TryGetValue(key, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String => double.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => null,
        };
    }

    private static bool? Bool(IReadOnlyDictionary<string, JsonElement> a, string key)
    {
        if (!a.TryGetValue(key, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.TryGetInt32(out var i) && i != 0,
            JsonValueKind.String => v.GetString() is { Length: > 0 } s && s != "0" && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase),
            _ => null,
        };
    }

    private static DateTimeOffset? ParseMs(IReadOnlyDictionary<string, JsonElement> a, string key)
    {
        if (!a.TryGetValue(key, out var v))
            return null;
        long? ms = v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var l) ? l : null,
            JsonValueKind.String => long.TryParse(v.GetString(), out var l) ? l : null,
            _ => null,
        };
        return ms is { } value and > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(value) : null;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
