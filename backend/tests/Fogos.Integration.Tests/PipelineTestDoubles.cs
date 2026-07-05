using System.Collections.Concurrent;
using Fogos.Infrastructure.Ops;

namespace Fogos.Integration.Tests;

/// <summary>Records ops calls so publisher dry-run captures and error escalations can be asserted.</summary>
internal sealed class RecordingOps : IOpsNotifier
{
    public readonly ConcurrentBag<(string Channel, string Payload)> Captures = [];
    public readonly ConcurrentBag<string> Errors = [];
    public readonly ConcurrentBag<string> Infos = [];

    public Task InfoAsync(string message, CancellationToken ct = default)
    {
        Infos.Add(message);
        return Task.CompletedTask;
    }

    public Task ErrorAsync(string message, CancellationToken ct = default)
    {
        Errors.Add(message);
        return Task.CompletedTask;
    }

    public Task DryRunCaptureAsync(string channel, string payload, CancellationToken ct = default)
    {
        Captures.Add((channel, payload));
        return Task.CompletedTask;
    }
}

/// <summary>An <see cref="IHttpClientFactory"/> that hands out clients over a single stub handler.</summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>Stub handler that responds per-attempt via a supplied function (attempt counter is 1-based).</summary>
internal sealed class StubHttpMessageHandler(Func<int, HttpResponseMessage> responder) : HttpMessageHandler
{
    private int _attempts;

    public int Attempts => _attempts;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var n = Interlocked.Increment(ref _attempts);
        return Task.FromResult(responder(n));
    }
}
