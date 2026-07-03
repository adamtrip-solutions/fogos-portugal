using Fogos.Domain.Photos;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Infrastructure.RateLimiting;

/// <summary>Which gate blocked an upload (or <see cref="None"/> when it passed).</summary>
public enum PhotoGate
{
    None,
    PerIpPerMinute,
    PerIncidentPerIpPerHour,
    PerIncidentPerHour,
    PendingModeration,
}

/// <summary>Result of the photo-upload pre-flight: pass, or the failing gate + how long to wait.</summary>
public readonly record struct PhotoGateResult(bool Passed, PhotoGate Gate, int RetryAfterSeconds)
{
    public static readonly PhotoGateResult Pass = new(true, PhotoGate.None, 0);
    public static PhotoGateResult Fail(PhotoGate gate, int retryAfter) => new(false, gate, retryAfter);
}

/// <summary>
/// The deliberate anonymous-write exception: citizen photo submission, fenced by abuse gates
/// (per-IP/min, per-incident/IP/hour, per-incident global/hour Redis windows + a pending-moderation
/// cap from Mongo). Phase 4 wires this to the upload endpoint; built and tested standalone now.
/// </summary>
public sealed class PhotoUploadGates(
    MongoContext mongo,
    RedisCounters counters,
    IOptions<PhotoGateOptions> options)
{
    private readonly PhotoGateOptions _o = options.Value;

    /// <summary>
    /// Checks all gates for an upload of <paramref name="ip"/> onto <paramref name="incidentId"/>.
    /// The pending-moderation cap (a read) is checked first; the counting gates increment only
    /// when the pending cap allows, so a saturated incident doesn't spend anyone's per-IP budget.
    /// </summary>
    public async Task<PhotoGateResult> CheckAsync(string incidentId, string ip, CancellationToken ct = default)
    {
        // Gate 4 (read-only): pending-moderation cap per incident.
        var pending = await mongo.IncidentPhotos.CountDocumentsAsync(
            Builders<IncidentPhoto>.Filter.And(
                Builders<IncidentPhoto>.Filter.Eq(x => x.IncidentId, incidentId),
                Builders<IncidentPhoto>.Filter.Eq(x => x.Status, ModerationStatus.Pending)),
            cancellationToken: ct);
        if (pending >= _o.PendingPerIncident)
            return PhotoGateResult.Fail(PhotoGate.PendingModeration, 0);

        // Gate 1: per-IP per-minute.
        if (await Exceeded($"photo:ip:{ip}", _o.PerIpPerMinute, 60) is { } g1)
            return PhotoGateResult.Fail(PhotoGate.PerIpPerMinute, g1);

        // Gate 2: per-incident per-IP per-hour.
        if (await Exceeded($"photo:incip:{incidentId}:{ip}", _o.PerIncidentPerIpPerHour, 3600) is { } g2)
            return PhotoGateResult.Fail(PhotoGate.PerIncidentPerIpPerHour, g2);

        // Gate 3: per-incident global per-hour.
        if (await Exceeded($"photo:inc:{incidentId}", _o.PerIncidentPerHour, 3600) is { } g3)
            return PhotoGateResult.Fail(PhotoGate.PerIncidentPerHour, g3);

        return PhotoGateResult.Pass;
    }

    /// <summary>Increments the window and returns the retry-after when the limit is exceeded, else null.</summary>
    private async Task<int?> Exceeded(string key, int limit, int windowSeconds)
    {
        var hit = await counters.HitAsync($"rl:{key}", 1, windowSeconds);
        if (hit is null)
            return null; // Redis down → fail open.
        return hit.Value.Total > limit ? hit.Value.RetryAfterSeconds : null;
    }
}
