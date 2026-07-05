using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Icnf;

/// <summary>
/// Ports <c>UpdateICNFData(bucket)</c>: selects fire incidents by creation-age bucket and dispatches a
/// <see cref="ProcessIcnfFireData"/> onto the <c>icnf</c> stream for each, re-enriching older incidents at
/// decreasing frequency. Buckets 0–7 are defined; the scheduler wires 0–6 (matching bootstrap/app.php).
/// </summary>
public sealed class UpdateIcnfDataJob(
    MongoContext mongo,
    IEventDispatcher dispatcher,
    IClock clock,
    ILogger<UpdateIcnfDataJob> logger) : IJob
{
    public const string BucketKey = "bucket";
    public const string Stream = "icnf";

    /// <summary>Age windows in whole days: {olderBound, newerBound} on <c>created</c> (legacy intervals[]).</summary>
    private static readonly (int After, int Before)[] Windows =
    [
        (1, 0), (2, 1), (7, 2), (14, 7), (28, 14), (60, 28), (90, 60), (180, 90),
    ];

    public Task Execute(IJobExecutionContext context)
    {
        var bucket = context.MergedJobDataMap.GetInt(BucketKey);
        return RunAsync(bucket, context.CancellationToken);
    }

    public async Task RunAsync(int bucket, CancellationToken ct)
    {
        if (bucket < 0 || bucket >= Windows.Length)
            return;

        var (afterDays, beforeDays) = Windows[bucket];
        var now = clock.UtcNow;
        var after = now.AddDays(-afterDays);
        var before = now.AddDays(-beforeDays);

        var f = Builders<Incident>.Filter;
        var incidents = await mongo.Incidents
            .Find(f.Eq(x => x.Kind, IncidentKind.Fire) & f.Gte(x => x.CreatedAt, after) & f.Lte(x => x.CreatedAt, before))
            .Project(x => new { x.Id, IcnfId = x.Icnf!.IcnfId })
            .ToListAsync(ct);

        foreach (var incident in incidents)
        {
            if (ct.IsCancellationRequested)
                break;
            await dispatcher.DispatchAsync(new ProcessIcnfFireData(incident.Id, incident.IcnfId ?? incident.Id), Stream, ct);
        }

        logger.LogInformation("UpdateICNFData bucket {Bucket}: dispatched {Count} enrichment events.", bucket, incidents.Count);
    }
}
