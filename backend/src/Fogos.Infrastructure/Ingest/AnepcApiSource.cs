using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Sources;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Ingest;

/// <summary>
/// Fallback ingester (ProcessANPCAllDataV2.php): the ANEPC direct JSON API behind Basic auth. Registered
/// but not scheduled — selectable via <c>Incidents:Source = anepc</c>. Field mapping (numero_sado,
/// codigo_natureza, meios_*, estado, outra_localizacao Spain override) is ported verbatim.
/// </summary>
public sealed class AnepcApiSource(IHttpClientFactory httpFactory, IOptions<FogosSourcesOptions> options, IClock clock) : IIncidentSource
{
    public const string HttpClientName = "anepc";

    private AnepcOptions Options => options.Value.Anepc;

    public async Task<IReadOnlyList<RawIncident>> FetchAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Options.ApiUrl))
            return [];

        var http = httpFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, Options.ApiUrl);
        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Options.Username}:{Options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
        request.Headers.UserAgent.ParseAdd("Fogos.pt/3.0");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<RawIncident>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var raw = Map(item, clock);
            if (raw is not null)
                result.Add(raw);
        }
        return result;
    }

    public static RawIncident? Map(JsonElement e, IClock clock)
    {
        var numero = Str(e, "numero_sado");
        if (string.IsNullOrEmpty(numero))
            return null;

        var spain = string.Equals(Str(e, "outra_localizacao"), "Espanha", StringComparison.Ordinal);
        var when = ParseLisbon(Str(e, "data_ocorrencia"), clock);

        return new RawIncident
        {
            Id = numero,
            OccurredAt = when,
            NaturezaCode = Str(e, "codigo_natureza"),
            Natureza = Str(e, "abreviatura_natureza"),
            StatusLabel = Str(e, "estado"),
            Concelho = spain ? "Espanha" : Str(e, "concelho"),
            Freguesia = spain ? "Espanha" : NullIfEmpty(Str(e, "freguesia")),
            Localidade = $"{Str(e, "local")} {Str(e, "outra_localizacao")}".Trim(),
            SpainOverride = spain,
            Lat = Dbl(e, "latitude"),
            Lng = Dbl(e, "longitude"),
            Resources = new Resources
            {
                Man = Int(e, "operacionais"),
                Terrain = Int(e, "meios_terrestres"),
                Aerial = Int(e, "meios_aereos"),
                Aquatic = Int(e, "meios_aquaticos"),
            },
            Region = NullIfEmpty(Str(e, "regiao")),
            SubRegion = NullIfEmpty(Str(e, "sub_regiao")),
        };
    }

    private static DateTimeOffset ParseLisbon(string value, IClock clock) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var naive)
            ? clock.FromLisbon(naive)
            : clock.UtcNow;

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? ""
        : e.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.Number ? n.GetRawText()
        : "";

    private static int Int(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v))
            return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var i) ? i : (int)v.GetDouble(),
            JsonValueKind.String => int.TryParse(v.GetString(), out var i) ? i : 0,
            _ => 0,
        };
    }

    private static double? Dbl(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String => double.TryParse(v.GetString(), CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => null,
        };
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
