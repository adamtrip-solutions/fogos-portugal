using System.Net.Http.Json;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Ops;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Rendering;

/// <summary>
/// Client for the Node/Playwright renderer (<c>POST /render</c>). Retries with exponential backoff
/// (1s/3s/9s…), enforces the min-bytes floor client-side too, and degrades to null on total failure
/// (a screenshot never blocks the event pipeline — callers fall back to a text-only post).
/// </summary>
public sealed class RendererClient(
    IHttpClientFactory httpFactory,
    IOptions<RendererOptions> options,
    IOpsNotifier ops,
    ILogger<RendererClient> logger)
{
    public const string HttpClientName = "renderer";
    public const string LeafletTilesLoaded = ".leaflet-tile-loaded";

    private RendererOptions Options => options.Value;

    /// <summary>Screenshot URL for an incident detail page (mirrors legacy <c>fogo/{id}/detalhe</c>).</summary>
    public string IncidentDetailUrl(string incidentId) => BuildUrl($"fogo/{incidentId}/detalhe");

    /// <summary>Builds a capture target URL from a bare path.</summary>
    public string BuildUrl(string path) => $"https://{Options.ScreenshotDomain}/{path.TrimStart('/')}";

    /// <summary>Captures an incident detail screenshot, waiting on the Leaflet tiles selector.</summary>
    public Task<byte[]?> CaptureIncidentDetailAsync(string incidentId, int? width = null, int? height = null, CancellationToken ct = default) =>
        CaptureAsync($"fogo/{incidentId}/detalhe", width, height, LeafletTilesLoaded, ct);

    /// <summary>Captures <paramref name="path"/> (under the screenshot domain), returning PNG bytes or null.</summary>
    public async Task<byte[]?> CaptureAsync(
        string path,
        int? width = null,
        int? height = null,
        string? waitFor = LeafletTilesLoaded,
        CancellationToken ct = default)
    {
        var target = BuildUrl(path);
        var payload = new Dictionary<string, object>
        {
            ["url"] = target,
            ["width"] = width ?? Options.DefaultWidth,
            ["height"] = height ?? Options.DefaultHeight,
            ["minBytes"] = Options.MinBytes,
        };
        if (!string.IsNullOrEmpty(waitFor))
            payload["waitFor"] = waitFor;

        var client = httpFactory.CreateClient(HttpClientName);
        var retries = Math.Max(1, Options.Retries);
        string? lastError = null;

        for (var attempt = 1; attempt <= retries; attempt++)
        {
            try
            {
                using var response = await client.PostAsJsonAsync($"{Options.Url.TrimEnd('/')}/render", payload, ct);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"renderer returned {(int)response.StatusCode}");

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < Options.MinBytes)
                    throw new InvalidOperationException($"renderer returned {bytes.Length} bytes (< minBytes {Options.MinBytes})");

                return bytes;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                logger.LogWarning("Renderer attempt {Attempt}/{Retries} failed for {Target}: {Error}", attempt, retries, target, ex.Message);
                if (attempt < retries)
                {
                    var delay = Options.RetryBaseDelay * Math.Pow(3, attempt - 1);
                    try { await Task.Delay(delay, ct); }
                    catch (OperationCanceledException) { return null; }
                }
            }
        }

        await ops.ErrorAsync($"🖼️ Renderer failed for {target} after {retries} attempts: {lastError}", ct);
        return null;
    }
}
