using Fogos.Api.Auth;
using Fogos.Domain.Auth;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.RateLimiting;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using HotChocolate.Execution;
using Microsoft.Extensions.Options;

namespace Fogos.Api.GraphQL.RateLimiting;

/// <summary>
/// Resolves caller identity from the websocket connect payload (<c>{ apiKey }</c> or
/// <c>{ authorization: "Bearer …" }</c>), rejects tiers that may not subscribe (anonymous, cap 0),
/// and enforces the per-caller concurrent-subscription cap via a Redis counter — acquiring on each
/// subscription start and releasing on completion/close.
/// </summary>
public sealed class SubscriptionSessionInterceptor(
    JwtService jwt,
    ApiKeyResolver apiKeys,
    SubscriptionLimiter limiter,
    IOptions<RateLimitOptions> options)
    : DefaultSocketSessionInterceptor
{
    private const string CallerKey = "FogosSubscriptionCaller";
    private const string PartitionKey = "FogosSubscriptionPartition";
    private const string ActiveCountKey = "FogosSubscriptionActive";

    private readonly RateLimitOptions _options = options.Value;

    public override async ValueTask<ConnectionStatus> OnConnectAsync(
        ISocketSession session,
        IOperationMessagePayload connectionInitMessage,
        CancellationToken cancellationToken)
    {
        var caller = await ResolveAsync(session, connectionInitMessage, cancellationToken);
        var cap = _options.For(caller.Tier).Subscriptions;

        if (!SubscriptionLimiter.Allowed(cap))
            return ConnectionStatus.Reject("Subscriptions are not permitted for this credential tier.");

        var http = session.Connection.HttpContext;
        http.Items[CallerKey] = caller;
        http.Items[PartitionKey] = RateLimitPartition.For(caller);
        http.Items[ActiveCountKey] = 0;

        return ConnectionStatus.Accept();
    }

    public override async ValueTask OnRequestAsync(
        ISocketSession session,
        string operationSessionId,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken)
    {
        var http = session.Connection.HttpContext;
        if (http.Items[CallerKey] is not FogosCaller caller || http.Items[PartitionKey] is not string partition)
            return;

        var cap = _options.For(caller.Tier).Subscriptions;
        if (!await limiter.TryAcquireAsync(partition, cap))
        {
            // Cap exhausted — close the socket rather than silently starting an unmetered stream.
            await session.Connection.CloseAsync(
                "Concurrent subscription limit reached.",
                ConnectionCloseReason.PolicyViolation,
                cancellationToken);
            return;
        }

        http.Items[ActiveCountKey] = (http.Items[ActiveCountKey] as int? ?? 0) + 1;
        await base.OnRequestAsync(session, operationSessionId, requestBuilder, cancellationToken);
    }

    public override async ValueTask OnCompleteAsync(
        ISocketSession session,
        string operationSessionId,
        CancellationToken cancellationToken)
    {
        await ReleaseOneAsync(session);
        await base.OnCompleteAsync(session, operationSessionId, cancellationToken);
    }

    public override async ValueTask OnCloseAsync(ISocketSession session, CancellationToken cancellationToken)
    {
        // Release any subscriptions still counted at disconnect.
        var http = session.Connection.HttpContext;
        var remaining = http.Items[ActiveCountKey] as int? ?? 0;
        if (http.Items[PartitionKey] is string partition)
        {
            for (var i = 0; i < remaining; i++)
                await limiter.ReleaseAsync(partition);
        }
        http.Items[ActiveCountKey] = 0;
        await base.OnCloseAsync(session, cancellationToken);
    }

    private async ValueTask ReleaseOneAsync(ISocketSession session)
    {
        var http = session.Connection.HttpContext;
        if (http.Items[PartitionKey] is not string partition)
            return;
        var count = http.Items[ActiveCountKey] as int? ?? 0;
        if (count <= 0)
            return;
        http.Items[ActiveCountKey] = count - 1;
        await limiter.ReleaseAsync(partition);
    }

    private async ValueTask<FogosCaller> ResolveAsync(
        ISocketSession session,
        IOperationMessagePayload payload,
        CancellationToken ct)
    {
        var ip = ResolveIp(session.Connection.HttpContext);

        ConnectPayload? parsed = null;
        try { parsed = payload.As<ConnectPayload>(); }
        catch { /* no/invalid payload → anonymous */ }

        var bearer = parsed?.Authorization;
        if (!string.IsNullOrWhiteSpace(bearer) && bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = bearer["Bearer ".Length..].Trim();
            if (jwt.TryValidate(token, out var claims) && claims is not null)
            {
                return new FogosCaller
                {
                    Tier = claims.Tier,
                    ClientId = claims.ClientId,
                    Name = claims.Name,
                    Scopes = claims.Scopes,
                    RemoteIp = ip,
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(parsed?.ApiKey))
        {
            var client = await apiKeys.ResolveAsync(parsed.ApiKey, ct);
            if (client is not null && !client.IsRevoked)
            {
                return new FogosCaller
                {
                    Tier = client.Tier,
                    ClientId = client.Id,
                    Name = client.Name,
                    Scopes = client.Scopes,
                    PublicContext = client.PublicContext,
                    AllowedOrigins = client.AllowedOrigins,
                    RemoteIp = ip,
                };
            }
        }

        return FogosCaller.Anonymous(ip);
    }

    private static string ResolveIp(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrEmpty(forwarded))
        {
            var first = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(first))
                return first;
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>Shape of the graphql-ws <c>connection_init</c> payload we understand.</summary>
    public sealed record ConnectPayload(
        [property: System.Text.Json.Serialization.JsonPropertyName("apiKey")] string? ApiKey,
        [property: System.Text.Json.Serialization.JsonPropertyName("authorization")] string? Authorization);
}
