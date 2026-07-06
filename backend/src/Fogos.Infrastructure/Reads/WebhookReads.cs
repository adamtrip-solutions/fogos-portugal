using Fogos.Domain.Webhooks;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>Read queries over registered webhook endpoints.</summary>
public sealed class WebhookReads(MongoContext context)
{
    /// <summary>All endpoints owned by a client, newest first.</summary>
    public async Task<IReadOnlyList<WebhookEndpoint>> ByClientAsync(string clientId, CancellationToken ct = default) =>
        await context.WebhookEndpoints
            .Find(Builders<WebhookEndpoint>.Filter.Eq(x => x.ClientId, clientId))
            .Sort(Builders<WebhookEndpoint>.Sort.Descending(x => x.CreatedAt))
            .ToListAsync(ct);

    /// <summary>All endpoints owned by any of the given clients (a user's keys), newest first.</summary>
    public async Task<IReadOnlyList<WebhookEndpoint>> ByClientIdsAsync(
        IReadOnlyCollection<string> clientIds, CancellationToken ct = default)
    {
        if (clientIds.Count == 0)
            return [];
        return await context.WebhookEndpoints
            .Find(Builders<WebhookEndpoint>.Filter.In(x => x.ClientId, clientIds))
            .Sort(Builders<WebhookEndpoint>.Sort.Descending(x => x.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>How many endpoints a client currently has (against the per-client cap).</summary>
    public async Task<long> CountByClientAsync(string clientId, CancellationToken ct = default) =>
        await context.WebhookEndpoints.CountDocumentsAsync(
            Builders<WebhookEndpoint>.Filter.Eq(x => x.ClientId, clientId), cancellationToken: ct);

    /// <summary>Active endpoints subscribed to an event name (delivery fan-out).</summary>
    public async Task<IReadOnlyList<WebhookEndpoint>> ActiveForEventAsync(string eventName, CancellationToken ct = default)
    {
        var f = Builders<WebhookEndpoint>.Filter;
        return await context.WebhookEndpoints
            .Find(f.Eq(x => x.Active, true) & f.AnyEq(x => x.Events, eventName))
            .ToListAsync(ct);
    }
}
