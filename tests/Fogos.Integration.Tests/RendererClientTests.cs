using System.Net;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fogos.Integration.Tests;

/// <summary>Renderer client retry + min-bytes behaviour against a stub handler.</summary>
public sealed class RendererClientTests
{
    private static RendererClient Build(StubHttpMessageHandler handler, RecordingOps ops, int retries)
    {
        var factory = new StubHttpClientFactory(handler);
        var options = Options.Create(new RendererOptions
        {
            Url = "http://renderer.test:3000",
            ScreenshotDomain = "fogos.test",
            Retries = retries,
            MinBytes = 8192,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        });
        return new RendererClient(factory, options, ops, NullLogger<RendererClient>.Instance);
    }

    private static HttpResponseMessage Png(int bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[bytes]) };

    [Fact]
    public async Task Retries_then_succeeds()
    {
        var handler = new StubHttpMessageHandler(attempt =>
            attempt == 1 ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Png(9000));
        var ops = new RecordingOps();
        var client = Build(handler, ops, retries: 3);

        var result = await client.CaptureIncidentDetailAsync("abc123");

        Assert.NotNull(result);
        Assert.Equal(9000, result!.Length);
        Assert.Equal(2, handler.Attempts);
        Assert.Empty(ops.Errors);
    }

    [Fact]
    public async Task Below_min_bytes_exhausts_retries_and_returns_null()
    {
        var handler = new StubHttpMessageHandler(_ => Png(10)); // always under minBytes
        var ops = new RecordingOps();
        var client = Build(handler, ops, retries: 2);

        var result = await client.CaptureIncidentDetailAsync("abc123");

        Assert.Null(result);
        Assert.Equal(2, handler.Attempts);
        Assert.Single(ops.Errors); // ops alerted on total failure
    }

    [Fact]
    public void Incident_detail_url_uses_screenshot_domain()
    {
        var client = Build(new StubHttpMessageHandler(_ => Png(9000)), new RecordingOps(), retries: 1);
        Assert.Equal("https://fogos.test/fogo/xyz/detalhe", client.IncidentDetailUrl("xyz"));
    }
}
