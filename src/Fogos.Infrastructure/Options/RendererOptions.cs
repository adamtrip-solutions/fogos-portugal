namespace Fogos.Infrastructure.Options;

/// <summary>Node/Playwright screenshot renderer client settings.</summary>
public sealed class RendererOptions
{
    public const string SectionName = "Renderer";

    /// <summary>
    /// Base URL of the renderer service. Empty = renderer disabled: captures return null quietly
    /// and posts go out text-only. Compose sets this to the service name (http://renderer:3000);
    /// a host-run worker can point at http://localhost:3000 when the container's port is mapped.
    /// </summary>
    public string Url { get; set; } = "";

    /// <summary>Domain the captured pages live on; the screenshot URL is <c>https://{domain}/{path}</c>.</summary>
    public string ScreenshotDomain { get; set; } = "fogos.pt";

    public int Retries { get; set; } = 3;

    public int MinBytes { get; set; } = 8192;

    public int DefaultWidth { get; set; } = 1000;

    public int DefaultHeight { get; set; } = 1300;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Base backoff; the wait after attempt <c>n</c> is <c>RetryBaseDelay × 3^(n-1)</c> (1s/3s/9s…).</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);
}
