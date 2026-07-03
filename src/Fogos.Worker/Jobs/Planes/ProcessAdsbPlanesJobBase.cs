using Fogos.Domain.Aircraft;
using Fogos.Domain.Geo;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Reads;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Planes;

/// <summary>
/// Shared base for the ADS-B pollers (adsb.fi / airplanes.live). Queries the active fleet by ICAO
/// hex, appends <c>flight_positions</c> with the provider's source label, and drops a sample that is
/// an exact repeat of the last stored one for that aircraft (same ICAO + coordinates + minute).
/// There is no social side-effect and no gate beyond the provider's enable flag (a configured base
/// URL) — this mirrors the wave-2 plane spec, which trims the legacy ADS-B daylight gate.
/// </summary>
[DisallowConcurrentExecution]
public abstract class ProcessAdsbPlanesJobBase(
    AircraftReads aircraftReads,
    MongoContext mongo,
    IClock clock,
    IOpsNotifier ops,
    PlaneJobFreshness freshness,
    ILogger logger) : IJob
{
    protected static readonly TimeSpan Cadence = TimeSpan.FromMinutes(3);

    /// <summary>Redis freshness key stem, e.g. <c>adsbfi-planes</c>.</summary>
    protected abstract string JobName { get; }

    /// <summary><see cref="FlightPosition.Source"/> label written for this provider.</summary>
    protected abstract string SourceLabel { get; }

    /// <summary>The provider base URL; empty/unset means the provider is disabled and the job no-ops.</summary>
    protected abstract string? BaseUrl { get; }

    /// <summary>Fetch raw JSON for a provider path (the concrete job forwards to its typed client).</summary>
    protected abstract Task<string> FetchAsync(string pathAndQuery, CancellationToken ct);

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        try
        {
            await RunAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Job} failed unexpectedly", JobName);
            await ops.ErrorAsync($"{JobName} failed: {ex.Message}", ct);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            logger.LogDebug("{Job} skipped: provider disabled (no base URL)", JobName);
            return;
        }

        var fleet = await aircraftReads.TrackedAsync(ct);
        if (fleet.Count == 0)
        {
            await freshness.NoteOnceAsync(JobName, "empty-fleet",
                $"{JobName}: no active tracked aircraft — nothing to poll.", ct);
            return;
        }

        var hexes = fleet
            .Select(a => a.Icao)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.ToLowerInvariant())
            .Distinct()
            .ToList();
        if (hexes.Count == 0)
            return;

        await freshness.CheckStaleAsync(JobName, Cadence, ct);

        string json;
        try
        {
            json = await FetchAsync($"hex/{string.Join(",", hexes)}", ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Job} API request failed", JobName);
            await ops.ErrorAsync($"{JobName} API request failed: {ex.Message}", ct);
            return;
        }

        IReadOnlyList<PlaneSample> samples;
        try
        {
            samples = AdsbPositionParser.Parse(json, clock.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Job} response parse failed", JobName);
            await ops.ErrorAsync($"{JobName} response parse failed: {ex.Message}", ct);
            return;
        }

        var byIcao = BuildRegistrationLookup(fleet);
        var icaos = samples.Select(s => s.Icao).Distinct().ToList();
        var previous = icaos.Count > 0
            ? await aircraftReads.LatestPositionsByIcaosAsync(icaos, ct)
            : new Dictionary<string, FlightPosition>();

        foreach (var sample in samples)
        {
            try
            {
                if (IsConsecutiveDuplicate(previous, sample))
                    continue;

                var position = new FlightPosition
                {
                    Icao = sample.Icao,
                    Registration = sample.Registration ?? byIcao.GetValueOrDefault(sample.Icao) ?? "",
                    Position = GeoPoint.FromLatLng(sample.Latitude, sample.Longitude),
                    Altitude = sample.Altitude,
                    SampledAt = sample.SampledAt ?? clock.UtcNow,
                    Source = SourceLabel,
                };
                await mongo.FlightPositions.InsertOneAsync(position, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Job}: skipping malformed sample for {Icao}", JobName, sample.Icao);
            }
        }

        await freshness.MarkSuccessAsync(JobName, ct);
    }

    /// <summary>Skip a sample identical to the last stored one for the ICAO: same coords, same minute.</summary>
    private bool IsConsecutiveDuplicate(IReadOnlyDictionary<string, FlightPosition> previous, PlaneSample sample)
    {
        if (!previous.TryGetValue(sample.Icao, out var last))
            return false;

        var sampledAt = sample.SampledAt ?? clock.UtcNow;
        return last.Position.Latitude.Equals(sample.Latitude)
               && last.Position.Longitude.Equals(sample.Longitude)
               && TruncateToMinute(last.SampledAt) == TruncateToMinute(sampledAt);
    }

    private static long TruncateToMinute(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        return utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMinute);
    }

    private static Dictionary<string, string> BuildRegistrationLookup(IReadOnlyList<TrackedAircraft> fleet)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var aircraft in fleet)
        {
            if (!string.IsNullOrWhiteSpace(aircraft.Icao) && !string.IsNullOrWhiteSpace(aircraft.Registration))
                map.TryAdd(aircraft.Icao.ToLowerInvariant(), aircraft.Registration);
        }
        return map;
    }
}
