using Fogos.Api.Auth;
using Fogos.Domain.Alerts;
using Fogos.Infrastructure.Reads;
using HotChocolate.Types;

namespace Fogos.Api.GraphQL.Types;

/// <summary>
/// The owned-resource fields on <see cref="Me"/>: the caller's API keys, webhooks, and alert
/// subscriptions. Identity is re-derived from the caller accessor on every resolver — never trusting the
/// parent <see cref="Me.Id"/> — so a signed-in user can only ever read their own resources.
/// </summary>
[ExtendObjectType(typeof(Me))]
public sealed class MeExtensions
{
    /// <summary>The caller's self-service API keys, newest first, including revoked keys.</summary>
    public async Task<IReadOnlyList<ApiKeyInfo>> ApiKeys(
        IFogosCallerAccessor callerAccessor,
        AccountReads accounts,
        CancellationToken ct)
    {
        var userId = callerAccessor.Caller.UserId;
        if (userId is null)
            return [];
        var keys = await accounts.ApiKeysByUserAsync(userId, ct);
        return keys.Select(ApiKeyInfo.From).ToList();
    }

    /// <summary>
    /// The webhooks registered under any of the caller's API keys (join by ClientId). The signing secret
    /// is never exposed here.
    /// </summary>
    public async Task<IReadOnlyList<Webhook>> Webhooks(
        IFogosCallerAccessor callerAccessor,
        AccountReads accounts,
        WebhookReads webhooks,
        CancellationToken ct)
    {
        var userId = callerAccessor.Caller.UserId;
        if (userId is null)
            return [];
        var keys = await accounts.ApiKeysByUserAsync(userId, ct);
        var clientIds = keys.Select(k => k.Id).ToList();
        if (clientIds.Count == 0)
            return [];
        var endpoints = await webhooks.ByClientIdsAsync(clientIds, ct);
        return endpoints.Select(Webhook.WithoutSecret).ToList();
    }

    /// <summary>The caller's owned alert subscriptions, newest first.</summary>
    public async Task<IReadOnlyList<AlertSubscription>> AlertSubscriptions(
        IFogosCallerAccessor callerAccessor,
        AccountReads accounts,
        CancellationToken ct)
    {
        var userId = callerAccessor.Caller.UserId;
        if (userId is null)
            return [];
        return await accounts.AlertSubscriptionsByUserAsync(userId, ct);
    }
}
