using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Ingest;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Incidents;

/// <summary>
/// Core ingestion (ProcessOcorrenciasSite, every 5 min, single-flight): fetch the active feed →
/// <see cref="IncidentIngestService"/> upsert + events → feed-freshness tracking → feed-drop close-out
/// (terminate active incidents that have dropped off the feed past the grace window) →
/// CheckImportantFireIncident. Escalates fetch failures to ops without crashing the scheduler.
/// </summary>
public sealed class ProcessOcorrenciasSiteJob(
    ISingleFlightLock lockService,
    ILogger<ProcessOcorrenciasSiteJob> logger,
    IIncidentSource source,
    IncidentIngestService ingest,
    ImportantFireChecker important,
    IncidentFeedFreshness freshness,
    MongoContext mongo,
    IOpsNotifier ops,
    IEventDispatcher dispatcher,
    IClock clock,
    IOptions<IncidentPipelineOptions> options) : UniqueJob(lockService, logger)
{
    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        IReadOnlyList<RawIncident> raws;
        try
        {
            raws = await source.FetchAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OcorrenciasSite fetch failed.");
            await ops.ErrorAsync($"Error OcorrenciasSite feed => {ex.Message}", ct);
            return;
        }

        if (raws.Count == 0)
        {
            logger.LogDebug("Empty incident feed; nothing to ingest.");
            return;
        }

        var outcome = await ingest.IngestAsync(raws, ct);

        var hash = IncidentFeedFreshness.HashOf(raws.Select(r => (r.Id, r.StatusLabel)));
        var feedFresh = await freshness.TrackAsync(hash, options.Value.FeedStaleAfter, ct);

        await CloseOutMissingAsync(outcome.SeenIds, feedFresh, ct);
        await important.RunAsync(ct);
    }

    /// <summary>
    /// Feed-drop close-out (replaces the legacy CheckIsActive flag-only flip). Any <c>active=true</c>
    /// incident whose id is absent from the current feed and whose last-seen time is older than
    /// <see cref="IncidentPipelineOptions.CloseAfterMissingFor"/> is given a real terminal transition to
    /// status 13 (Encerrada sem atualização): flag cleared, status set, <see cref="IncidentStatusChanged"/>
    /// dispatched (so the status-history row + downstream handlers run exactly like a feed-driven change),
    /// <c>UpdatedAt</c> bumped. Guards, ALL of which must hold or the sweep is skipped:
    /// <list type="bullet">
    /// <item>feed non-empty (already guaranteed by the caller's early return);</item>
    /// <item>feed fresh this sweep (<paramref name="feedFresh"/>) — a frozen feed can't signal an ending;</item>
    /// <item>candidate count within <c>max(3, MaxCloseFraction × active count)</c> — else abort + ops alert
    /// ("possible truncated feed"), never mass-close on a partial feed.</item>
    /// </list>
    /// </summary>
    public async Task<long> CloseOutMissingAsync(IReadOnlySet<string> seenIds, bool feedFresh, CancellationToken ct)
    {
        if (!feedFresh)
        {
            logger.LogDebug("Feed stale this sweep; skipping close-out (freshness alert already latched).");
            return 0;
        }

        var opts = options.Value;
        var now = clock.UtcNow;
        var graceCutoff = now - opts.CloseAfterMissingFor;
        var f = Builders<Incident>.Filter;

        // Active incidents absent from this sweep. The last-seen grace (null → CreatedAt fallback) is applied
        // in memory because the fallback isn't expressible in a single Mongo predicate.
        var activeUnseen = await mongo.Incidents
            .Find(f.Eq(x => x.Active, true) & f.Nin(x => x.Id, seenIds))
            .ToListAsync(ct);

        var candidates = activeUnseen
            .Where(i => (i.LastSeenInFeedAt ?? i.CreatedAt) < graceCutoff)
            .ToList();
        if (candidates.Count == 0)
            return 0;

        var activeCount = await mongo.Incidents.CountDocumentsAsync(f.Eq(x => x.Active, true), cancellationToken: ct);
        var cap = Math.Max(3, (int)(opts.MaxCloseFraction * activeCount));
        if (candidates.Count > cap)
        {
            logger.LogWarning("Close-out aborted: {Candidates} candidates exceed cap {Cap} (active={Active}).",
                candidates.Count, cap, activeCount);
            await ops.ErrorAsync(
                $"⚠️ Close-out abortado: {candidates.Count} incidentes ausentes excedem o limite {cap} " +
                $"(ativos={activeCount}). Possível feed truncado — nenhum incidente foi encerrado.", ct);
            return 0;
        }

        var terminal = IncidentStatusCatalog.FromCode(IncidentStatusCatalog.EncerradaSemAtualizacao);
        long closed = 0;
        foreach (var incident in candidates)
        {
            if (ct.IsCancellationRequested)
                break;

            var prev = incident.Status;
            await mongo.Incidents.UpdateOneAsync(
                f.Eq(x => x.Id, incident.Id),
                Builders<Incident>.Update
                    .Set(x => x.Active, false)
                    .Set(x => x.Status, terminal)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: ct);

            await dispatcher.DispatchAsync(
                new IncidentStatusChanged(incident.Id, prev.Code, prev.Label, terminal.Code, terminal.Label), ct: ct);
            closed++;
        }

        if (closed > 0)
            logger.LogInformation("Feed-drop close-out terminated {Closed} incident(s) to status 13.", closed);
        return closed;
    }
}
