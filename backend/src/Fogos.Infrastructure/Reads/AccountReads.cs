using Fogos.Domain.Alerts;
using Fogos.Domain.Auth;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>
/// Read queries over a signed-in user's owned resources: the self-service API keys and alert
/// subscriptions they own (by <c>ownerUserId</c>). Backs the <c>me</c> resolvers and the per-user caps.
/// </summary>
public sealed class AccountReads(MongoContext context)
{
    /// <summary>All API keys owned by a user, newest first — including revoked keys (the UI badges them).</summary>
    public async Task<IReadOnlyList<ApiClient>> ApiKeysByUserAsync(string userId, CancellationToken ct = default) =>
        await context.ApiClients
            .Find(Builders<ApiClient>.Filter.Eq(x => x.OwnerUserId, userId))
            .Sort(Builders<ApiClient>.Sort.Descending(x => x.CreatedAt))
            .ToListAsync(ct);

    /// <summary>Count of a user's active (non-revoked) keys — the per-user cap frees a slot on revoke.</summary>
    public async Task<long> CountActiveApiKeysByUserAsync(string userId, CancellationToken ct = default)
    {
        var f = Builders<ApiClient>.Filter;
        return await context.ApiClients.CountDocumentsAsync(
            f.Eq(x => x.OwnerUserId, userId) & f.Eq(x => x.RevokedAt, null), cancellationToken: ct);
    }

    /// <summary>All alert subscriptions owned by a user, newest first.</summary>
    public async Task<IReadOnlyList<AlertSubscription>> AlertSubscriptionsByUserAsync(string userId, CancellationToken ct = default) =>
        await context.AlertSubscriptions
            .Find(Builders<AlertSubscription>.Filter.Eq(x => x.OwnerUserId, userId))
            .Sort(Builders<AlertSubscription>.Sort.Descending(x => x.CreatedAt))
            .ToListAsync(ct);

    /// <summary>Count of a user's owned subscriptions (against the per-user cap).</summary>
    public async Task<long> CountAlertSubscriptionsByUserAsync(string userId, CancellationToken ct = default) =>
        await context.AlertSubscriptions.CountDocumentsAsync(
            Builders<AlertSubscription>.Filter.Eq(x => x.OwnerUserId, userId), cancellationToken: ct);
}
