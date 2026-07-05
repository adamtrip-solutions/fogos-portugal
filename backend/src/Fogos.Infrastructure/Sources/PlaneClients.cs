using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Sources;

/// <summary>adsb.fi flight-tracking client (shell). Returns raw JSON.</summary>
public sealed class AdsbFiClient(HttpClient http, IOptions<FogosSourcesOptions> options)
{
    public const string HttpClientName = "adsbfi";

    public async Task<string> GetAsync(string pathAndQuery = "", CancellationToken ct = default)
    {
        var baseUrl = options.Value.AdsbFi.BaseUrl;
        var url = string.IsNullOrEmpty(pathAndQuery) ? baseUrl : $"{baseUrl.TrimEnd('/')}/{pathAndQuery.TrimStart('/')}";
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}

/// <summary>airplanes.live flight-tracking client (shell). Returns raw JSON.</summary>
public sealed class AirplanesLiveClient(HttpClient http, IOptions<FogosSourcesOptions> options)
{
    public const string HttpClientName = "airplaneslive";

    public async Task<string> GetAsync(string pathAndQuery = "", CancellationToken ct = default)
    {
        var baseUrl = options.Value.AirplanesLive.BaseUrl;
        var url = string.IsNullOrEmpty(pathAndQuery) ? baseUrl : $"{baseUrl.TrimEnd('/')}/{pathAndQuery.TrimStart('/')}";
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
