using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Sources;

/// <summary>
/// NASA FIRMS active-fire CSV client (shell): area CSV by source + bbox + day range. Returns raw CSV
/// text; Wave 2 owns the hotspot parsing.
/// </summary>
public sealed class FirmsClient(HttpClient http, IOptions<FogosSourcesOptions> options)
{
    public const string HttpClientName = "firms";

    private FirmsOptions Options => options.Value.Firms;

    /// <summary>
    /// Fetch active-fire CSV for a source (e.g. <c>VIIRS_SNPP_NRT</c>, <c>MODIS_NRT</c>) over a bbox
    /// (<c>west,south,east,north</c>) for <paramref name="dayRange"/> days.
    /// </summary>
    public async Task<string> GetAreaCsvAsync(string source, string bbox, int dayRange = 1, CancellationToken ct = default)
    {
        var url = $"{Options.BaseUrl}/{Options.Key}/{source}/{bbox}/{dayRange}";
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
