using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Sources;

/// <summary>Raw bytes plus content type for a passthrough proxy response (WMS tiles/images).</summary>
public sealed record ProxyPayload(byte[] Bytes, string? ContentType);

/// <summary>
/// IPMA client (shell): open-data station/observation JSON, daily observations, scraped homepage and
/// RCM pages, and a WMS passthrough. Returns raw payloads — Wave 2 owns the scraping/regex parsing.
/// </summary>
public sealed class IpmaClient(HttpClient http, IOptions<FogosSourcesOptions> options)
{
    public const string HttpClientName = "ipma";

    private IpmaOptions Options => options.Value.Ipma;

    public Task<string> GetStationsAsync(CancellationToken ct = default) => GetStringAsync(Options.StationsUrl, ct);

    public Task<string> GetObservationsAsync(CancellationToken ct = default) => GetStringAsync(Options.ObservationsUrl, ct);

    public Task<string> GetDailyObservationsAsync(CancellationToken ct = default) => GetStringAsync(Options.DailyObservationsUrl, ct);

    public Task<string> GetHomepageHtmlAsync(CancellationToken ct = default) => GetStringAsync(Options.HomepageUrl, ct);

    public Task<string> GetRcmPageAsync(CancellationToken ct = default) => GetStringAsync(Options.RcmUrl, ct);

    /// <summary>Passthrough proxy for a WMS request (relative path + query appended to the WMS base).</summary>
    public async Task<ProxyPayload> ProxyWmsAsync(string relativePathAndQuery, CancellationToken ct = default)
    {
        var url = $"{Options.WmsBaseUrl.TrimEnd('/')}/{relativePathAndQuery.TrimStart('/')}";
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new ProxyPayload(bytes, response.Content.Headers.ContentType?.ToString());
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
