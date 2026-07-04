using Fogos.Api.Auth;
using Fogos.Domain.Auth;
using Fogos.Infrastructure.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Fogos.Integration.Tests;

/// <summary>
/// Pure unit tests for <see cref="ClientIpResolver"/> — the anti-spoofing IP resolution behind every
/// rate-limit / photo gate. No containers.
/// </summary>
public sealed class ClientIpResolverTests
{
    private static ClientIpResolver Resolver(string? header = null)
    {
        var options = new RateLimitOptions();
        if (header is not null)
            options.ClientIpHeader = header;
        return new ClientIpResolver(Options.Create(options));
    }

    private static HttpContext Context(Action<HttpRequest> configure)
    {
        var ctx = new DefaultHttpContext();
        configure(ctx.Request);
        return ctx;
    }

    [Fact]
    public void Spoofed_first_hops_of_xff_resolve_to_the_same_nearest_trusted_hop()
    {
        var resolver = Resolver();

        // An attacker can prepend anything; only the LAST hop was appended by the trusted proxy.
        var a = resolver.Resolve(Context(r => r.Headers["X-Forwarded-For"] = "1.1.1.1, 9.9.9.9, 203.0.113.7"));
        var b = resolver.Resolve(Context(r => r.Headers["X-Forwarded-For"] = "6.6.6.6, 203.0.113.7"));

        Assert.Equal("203.0.113.7", a);
        Assert.Equal("203.0.113.7", b);

        // ⇒ both spoof attempts land in the same limiter partition (same bucket).
        Assert.Equal(
            RateLimitPartition.For(FogosCaller.Anonymous(a)),
            RateLimitPartition.For(FogosCaller.Anonymous(b)));
    }

    [Fact]
    public void Cf_connecting_ip_wins_over_xff()
    {
        var resolver = Resolver();

        var ip = resolver.Resolve(Context(r =>
        {
            r.Headers["CF-Connecting-IP"] = "198.51.100.42";
            r.Headers["X-Forwarded-For"] = "1.1.1.1, 203.0.113.7"; // ignored while the edge header is present
        }));

        Assert.Equal("198.51.100.42", ip);
    }

    [Fact]
    public void Configurable_header_name_is_honoured()
    {
        var resolver = Resolver(header: "True-Client-IP");

        var ip = resolver.Resolve(Context(r =>
        {
            r.Headers["True-Client-IP"] = "198.51.100.9";
            r.Headers["CF-Connecting-IP"] = "10.0.0.1"; // not the configured header → ignored
        }));

        Assert.Equal("198.51.100.9", ip);
    }
}
