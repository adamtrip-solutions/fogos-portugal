using System.Net.Http.Headers;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Sources;

/// <summary>GitHub contributors client (shell). Returns raw JSON for the configured repository.</summary>
public sealed class GitHubClient(HttpClient http, IOptions<FogosSourcesOptions> options)
{
    public const string HttpClientName = "github";

    private GitHubOptions Options => options.Value.GitHub;

    public async Task<string> GetContributorsAsync(CancellationToken ct = default)
    {
        var url = $"{Options.ApiBaseUrl}/repos/{Options.Repository}/contributors";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(Options.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(Options.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.Token);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
