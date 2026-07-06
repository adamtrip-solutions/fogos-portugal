using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fogos.Integration.Tests;

/// <summary>
/// A misconfigured Discord webhook (non-URL value from a bad env entry) must be skipped without
/// attempting HTTP — previously every ops alert logged an InvalidOperationException forever.
/// </summary>
public sealed class DiscordOpsNotifierTests
{
    [Theory]
    [InlineData("discord.com/api/webhooks/1/x")] // missing scheme
    [InlineData("https://discord.com/api/webhooks/1/x # ops feed")] // inline comment kept by compose
    [InlineData("changeme")] // leftover placeholder
    public async Task Invalid_webhook_value_never_attempts_http(string webhook)
    {
        var factory = new CountingHttpClientFactory();
        var notifier = new DiscordOpsNotifier(
            factory,
            Microsoft.Extensions.Options.Options.Create(new OpsOptions { DiscordGeneralWebhook = webhook }),
            NullLogger<DiscordOpsNotifier>.Instance);

        await notifier.InfoAsync("hello");
        await notifier.InfoAsync("hello again");

        Assert.Equal(0, factory.Created);
    }

    [Fact]
    public async Task Empty_webhook_stays_a_silent_noop()
    {
        var factory = new CountingHttpClientFactory();
        var notifier = new DiscordOpsNotifier(
            factory,
            Microsoft.Extensions.Options.Options.Create(new OpsOptions()),
            NullLogger<DiscordOpsNotifier>.Instance);

        await notifier.InfoAsync("hello");

        Assert.Equal(0, factory.Created);
    }

    private sealed class CountingHttpClientFactory : IHttpClientFactory
    {
        public int Created { get; private set; }

        public HttpClient CreateClient(string name)
        {
            Created++;
            return new HttpClient(new ThrowingHandler());
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("No HTTP expected in this test.");
        }
    }
}
