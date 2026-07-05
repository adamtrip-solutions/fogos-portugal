using Fogos.Domain.Aircraft;
using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Planes;

/// <summary>
/// Polls Flightradar24 <c>live-positions-light</c> by the registrations of the active tracked fleet
/// and appends <c>flight_positions</c> (source <c>fr24</c>). Ported from the legacy
/// <c>ProcessFR24Planes</c> (offset 0 of the 3-minute plane cycle).
///
/// Gates run <b>before</b> any API call, in the legacy order:
/// (a) FR24 mode is not Off and a key is configured;
/// (b) the Lisbon daylight window (sunrise + 1h → sunset − 1h);
/// (c) at least one active fire incident committing aerial assets;
/// (d) the shared FR24 monthly credit budget is below the 95% guard.
///
/// The spend gate is <c>Sources:Fr24:Mode</c> (<c>FR24_MODE</c>): <c>Off</c> disables polling entirely;
/// <c>DryRun</c> (default) runs every gate above but never makes the paid API call; <c>On</c> polls live.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ProcessFr24PlanesJob(
    Fr24Client fr24,
    Fr24CreditMeter creditMeter,
    AircraftReads aircraftReads,
    MongoContext mongo,
    IClock clock,
    IOpsNotifier ops,
    IOptions<FogosSourcesOptions> sources,
    PlaneJobFreshness freshness,
    ILogger<ProcessFr24PlanesJob> logger) : IJob
{
    public const string JobName = "fr24-planes";

    private static readonly TimeSpan Cadence = TimeSpan.FromMinutes(3);

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        try
        {
            await RunAsync(ct);
        }
        catch (Exception ex)
        {
            // Never let a poller crash the scheduler.
            logger.LogError(ex, "FR24 plane job failed unexpectedly");
            await ops.ErrorAsync($"FR24 plane job failed: {ex.Message}", ct);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var fr24Options = sources.Value.Fr24;

        // (a) enabled + key configured
        if (fr24Options.Mode == PublisherMode.Off || string.IsNullOrWhiteSpace(fr24Options.ApiKey))
        {
            logger.LogDebug("FR24 poll skipped: mode Off or no API key");
            return;
        }

        // (a2) fail closed: a key with no configured budget must never spend shared credits.
        if (fr24Options.MonthlyBudget <= 0)
        {
            await freshness.NoteOnceAsync(JobName, "no-budget",
                "FR24 key configured but no monthly budget — refusing to spend", ct);
            return;
        }

        // (b) daylight window (Lisbon)
        if (!SolarWindow.IsLisbonDaylight(clock.UtcNow))
        {
            logger.LogDebug("FR24 poll skipped: outside the Lisbon daylight window");
            return;
        }

        // (c) at least one active fire incident with aerial assets
        if (!await HasActiveAerialFireAsync(ct))
        {
            logger.LogDebug("FR24 poll skipped: no active fire incident with aerial assets");
            return;
        }

        // (d) credit budget guard (95%)
        if (!await creditMeter.HasBudgetAsync())
        {
            await freshness.NoteOnceAsync(JobName, "budget",
                "FR24 monthly credit budget guard tripped (95%) — pausing FR24 polling.", ct);
            return;
        }

        var fleet = await aircraftReads.TrackedAsync(ct);
        if (fleet.Count == 0)
        {
            await freshness.NoteOnceAsync(JobName, "empty-fleet",
                "FR24 plane job: no active tracked aircraft — nothing to poll.", ct);
            return;
        }

        var registrations = fleet
            .Select(a => a.Registration)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (registrations.Count == 0)
            return;

        // Spend gate: DryRun exercises every gate above but never makes the paid FR24 call.
        if (fr24Options.Mode == PublisherMode.DryRun)
        {
            logger.LogInformation(
                "FR24 poll dry-run: {Count} tracked registrations would be polled; no paid call made.",
                registrations.Count);
            return;
        }

        await freshness.CheckStaleAsync(JobName, Cadence, ct);

        string json;
        try
        {
            json = await fr24.GetLivePositionsLightAsync(string.Join(",", registrations), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FR24 API request failed");
            await ops.ErrorAsync($"FR24 API request failed: {ex.Message}", ct);
            return;
        }

        IReadOnlyList<PlaneSample> samples;
        try
        {
            samples = Fr24PositionParser.Parse(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FR24 response parse failed");
            await ops.ErrorAsync($"FR24 response parse failed: {ex.Message}", ct);
            return;
        }

        // Record the credits this poll spent (legacy cost model: 2 + rows × 0.04).
        await creditMeter.TryConsumeAsync((int)Math.Ceiling(2 + samples.Count * 0.04));

        await PersistAsync(samples, fleet, ct);

        await freshness.MarkSuccessAsync(JobName, ct);
    }

    private async Task<bool> HasActiveAerialFireAsync(CancellationToken ct)
    {
        var f = Builders<Incident>.Filter;
        var filter = f.Eq(x => x.Active, true)
                     & f.Eq(x => x.Kind, IncidentKind.Fire)
                     & f.Gt(x => x.Resources.Aerial, 0);
        return await mongo.Incidents.Find(filter).Limit(1).AnyAsync(ct);
    }

    private async Task PersistAsync(
        IReadOnlyList<PlaneSample> samples, IReadOnlyList<TrackedAircraft> fleet, CancellationToken ct)
    {
        var byIcao = BuildLookup(fleet, a => a.Icao);
        var byReg = BuildLookup(fleet, a => a.Registration);

        foreach (var sample in samples)
        {
            var aircraft = Match(sample, byIcao, byReg);
            try
            {
                var position = new FlightPosition
                {
                    Icao = sample.Icao,
                    Registration = sample.Registration ?? aircraft?.Registration ?? "",
                    Position = GeoPoint.FromLatLng(sample.Latitude, sample.Longitude),
                    Altitude = sample.Altitude,
                    SampledAt = sample.SampledAt ?? clock.UtcNow,
                    Source = Fr24PositionParser.Source,
                    Fr24Id = sample.Fr24Id,
                };
                await mongo.FlightPositions.InsertOneAsync(position, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping malformed FR24 sample for {Icao}", sample.Icao);
            }
        }
    }

    private static TrackedAircraft? Match(
        PlaneSample sample,
        IReadOnlyDictionary<string, TrackedAircraft> byIcao,
        IReadOnlyDictionary<string, TrackedAircraft> byReg)
    {
        if (byIcao.TryGetValue(sample.Icao.ToLowerInvariant(), out var byHex))
            return byHex;
        if (!string.IsNullOrWhiteSpace(sample.Registration) &&
            byReg.TryGetValue(sample.Registration.ToUpperInvariant(), out var byRegistration))
            return byRegistration;
        return null;
    }

    private static Dictionary<string, TrackedAircraft> BuildLookup(
        IReadOnlyList<TrackedAircraft> fleet, Func<TrackedAircraft, string?> key)
    {
        var map = new Dictionary<string, TrackedAircraft>(StringComparer.OrdinalIgnoreCase);
        foreach (var aircraft in fleet)
        {
            var k = key(aircraft);
            if (!string.IsNullOrWhiteSpace(k))
                map.TryAdd(k, aircraft);
        }
        return map;
    }
}
