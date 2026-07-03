using System.Net;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Rendering;
using Fogos.Worker.Jobs.Risk;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fogos.Integration.Tests.Risk;

/// <summary>Builds an <see cref="RcmProcessor"/> wired to real DryRun publishers + a stubbed renderer.</summary>
internal static class RiskTestHost
{
    /// <summary>
    /// A processor with all publishers in DryRun (captures land on the returned <see cref="RecordingOps"/>).
    /// <paramref name="rendererSucceeds"/> false makes the renderer return 500 so the map degrades to text-only.
    /// </summary>
    public static (RcmProcessor Processor, RecordingOps Ops) BuildProcessor(MongoContext mongo, bool rendererSucceeds)
    {
        var ops = new RecordingOps();
        var publishing = Options.Create(new PublishingOptions()); // every channel defaults to DryRun.

        var handler = new StubHttpMessageHandler(_ =>
            rendererSucceeds
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[256]) }
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var httpFactory = new StubHttpClientFactory(handler);

        var twitter = new TwitterPublisher(httpFactory, publishing, Options.Create(new TwitterOptions()), ops, NullLogger<TwitterPublisher>.Instance);
        var telegram = new TelegramPublisher(httpFactory, publishing, Options.Create(new TelegramOptions()), ops, NullLogger<TelegramPublisher>.Instance);
        var facebook = new FacebookPublisher(httpFactory, publishing, Options.Create(new FacebookOptions()), ops, NullLogger<FacebookPublisher>.Instance);

        var rendererOptions = Options.Create(new RendererOptions
        {
            Retries = 1,
            RetryBaseDelay = TimeSpan.Zero,
            MinBytes = 1,
        });
        var renderer = new RendererClient(httpFactory, rendererOptions, ops, NullLogger<RendererClient>.Instance);

        var processor = new RcmProcessor(mongo, new ConcelhoPolygons(), twitter, telegram, facebook, renderer, NullLogger<RcmProcessor>.Instance);
        return (processor, ops);
    }
}
