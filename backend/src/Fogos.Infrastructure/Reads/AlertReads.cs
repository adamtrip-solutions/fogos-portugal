using Fogos.Domain.Alerts;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>Read queries over alert subscriptions and their delivered events.</summary>
public sealed class AlertReads(MongoContext context)
{
    /// <summary>A subscription by id, or null when the id is malformed / unknown.</summary>
    public async Task<AlertSubscription?> GetSubscriptionAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await context.AlertSubscriptions
                .Find(Builders<AlertSubscription>.Filter.Eq(x => x.Id, id))
                .FirstOrDefaultAsync(ct);
        }
        catch (FormatException)
        {
            return null; // not a valid ObjectId → treated as unknown.
        }
    }

    /// <summary>Concelho subscriptions watching a DICO.</summary>
    public async Task<IReadOnlyList<AlertSubscription>> ConcelhoSubscriptionsByDicoAsync(string dico, CancellationToken ct = default)
    {
        var f = Builders<AlertSubscription>.Filter;
        return await context.AlertSubscriptions
            .Find(f.Eq(x => x.Kind, AlertSubscriptionKind.Concelho) & f.Eq(x => x.Dico, dico))
            .ToListAsync(ct);
    }

    /// <summary>All point subscriptions (distance-matched in process against an incident).</summary>
    public async Task<IReadOnlyList<AlertSubscription>> PointSubscriptionsAsync(CancellationToken ct = default) =>
        await context.AlertSubscriptions
            .Find(Builders<AlertSubscription>.Filter.Eq(x => x.Kind, AlertSubscriptionKind.Point))
            .ToListAsync(ct);

    /// <summary>Concelho subscriptions that carry a risk threshold (risk-alert matching).</summary>
    public async Task<IReadOnlyList<AlertSubscription>> ConcelhoSubscriptionsWithRiskAsync(CancellationToken ct = default)
    {
        var f = Builders<AlertSubscription>.Filter;
        return await context.AlertSubscriptions
            .Find(f.Eq(x => x.Kind, AlertSubscriptionKind.Concelho)
                  & f.Ne(x => x.Dico, null)
                  & f.Ne(x => x.RiskThreshold, null))
            .ToListAsync(ct);
    }
}
