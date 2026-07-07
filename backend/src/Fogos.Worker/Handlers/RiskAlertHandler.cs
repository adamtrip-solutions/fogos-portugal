using Fogos.Domain.Alerts;
using Fogos.Domain.Events;
using Fogos.Domain.Risk;
using Fogos.Infrastructure.Alerts;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Reads;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// On <see cref="RcmProcessed"/>, records a RISK alert event (once per day per subscription, via the
/// <c>risk:{dico}:{yyyy-MM-dd}</c> dedupe key) for every concelho subscription whose configured threshold
/// is met by today's <c>rcm_daily</c> level. Idempotent — the hourly RCM run redispatches, but the dedupe
/// insert keeps it to one event per day. Push fires on the winning insert (exactly-once) and never fails
/// the handler.
/// </summary>
public sealed class RiskAlertHandler(
    MongoContext mongo,
    AlertReads alerts,
    AlertEventStore events,
    AlertDeliveryService delivery)
    : IEventHandler<RcmProcessed>
{
    public async Task HandleAsync(RcmProcessed evt, CancellationToken ct)
    {
        var subs = await alerts.ConcelhoSubscriptionsWithRiskAsync(ct);
        if (subs.Count == 0)
            return;

        var day = evt.ForecastDate;
        var dicos = subs.Select(s => s.Dico!).Distinct().ToList();

        var risks = await mongo.RcmDaily
            .Find(Builders<ConcelhoRisk>.Filter.In(x => x.Dico, dicos)
                  & Builders<ConcelhoRisk>.Filter.Eq(x => x.Date, day))
            .ToListAsync(ct);
        var byDico = risks
            .GroupBy(r => r.Dico)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var sub in subs)
        {
            if (!byDico.TryGetValue(sub.Dico!, out var risk) || risk.Today is not int level)
                continue;
            if (sub.RiskThreshold is not int threshold || level < threshold)
                continue;

            var message = AlertCopy.Risk(risk.Concelho, RiskLevels.Label(level));
            var dedupe = $"risk:{sub.Dico}:{day:yyyy-MM-dd}";
            if (await events.TryAppendAsync(sub.Id, AlertEventKind.Risk, null, message, dedupe, ct))
                await delivery.DeliverAsync(sub, dedupe, AlertEventKind.Risk, message, "/risco", ct);
        }
    }
}
