using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Ingest;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Incidents;

/// <summary>
/// Core ingestion (ProcessOcorrenciasSite, every 5 min, single-flight): fetch the active feed →
/// <see cref="IncidentIngestService"/> upsert + events → CheckIsActive (deactivate stored actives that
/// dropped off the feed) → CheckImportantFireIncident → feed-freshness tracking. Escalates fetch failures
/// to ops without crashing the scheduler.
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
        await ReconcileActiveAsync(outcome.SeenIds, ct);
        await important.RunAsync(ct);

        var hash = IncidentFeedFreshness.HashOf(raws.Select(r => (r.Id, r.StatusLabel)));
        await freshness.TrackAsync(hash, options.Value.FeedStaleAfter, ct);
    }

    /// <summary>
    /// CheckIsActive: any stored <c>active=true</c> incident whose id is absent from the current feed is
    /// flipped to <c>active=false</c> (status untouched — the change-stream bridge picks the flag up).
    /// </summary>
    public async Task<long> ReconcileActiveAsync(IReadOnlySet<string> seenIds, CancellationToken ct)
    {
        var f = Builders<Incident>.Filter;
        var filter = f.Eq(x => x.Active, true) & f.Nin(x => x.Id, seenIds);
        var result = await mongo.Incidents.UpdateManyAsync(
            filter, Builders<Incident>.Update.Set(x => x.Active, false), cancellationToken: ct);
        return result.ModifiedCount;
    }
}
