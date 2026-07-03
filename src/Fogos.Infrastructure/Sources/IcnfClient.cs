using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Sources;

/// <summary>
/// ICNF fire data client (shell): the HTML occurrences table, per-occurrence XML, and KML perimeter
/// downloads. TLS validation is relaxed for the ICNF hosts (their chain is broken) behind an option
/// flag — the relaxation is scoped to this client's dedicated handler, no other host is affected.
/// </summary>
public sealed class IcnfClient(HttpClient http, IOptions<FogosSourcesOptions> options)
{
    public const string HttpClientName = "icnf";

    private IcnfOptions Options => options.Value.Icnf;

    /// <summary>Raw HTML of <c>faztable.asp</c> (a JS array embedded in HTML — parsed by Wave 2).</summary>
    public async Task<string> FetchTableAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Options.TableUrl, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Raw XML from <c>webserviceocorrencias.asp?ncco={id}</c>.</summary>
    public async Task<string> FetchOccurrenceXmlAsync(string ncco, CancellationToken ct = default)
    {
        var url = $"{Options.OccurrenceUrl}?ncco={Uri.EscapeDataString(ncco)}";
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Downloads the KML perimeter for an incident id, or null when absent.</summary>
    public async Task<byte[]?> DownloadKmlAsync(string incidentId, CancellationToken ct = default)
    {
        var url = $"{Options.KmlBaseUrl}/{incidentId}.kml";
        using var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
