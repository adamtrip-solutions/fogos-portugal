using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Domain.Weather;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Signals;

/// <summary>
/// Every 2 minutes, re-derives escalation and (hourly) critical-conditions signals for every active
/// fire from its recent <c>incident_history</c> and the nearest-station weather / concelho risk. Writes
/// each incident's <c>Signals</c> subdocument with targeted <c>$set</c>s (never a whole-doc rewrite) and
/// dispatches <see cref="IncidentEscalating"/> on a false→true escalation transition. Single-flight.
/// </summary>
public sealed class SignalsJob(
    ISingleFlightLock lockService,
    ILogger<SignalsJob> logger,
    MongoContext mongo,
    IncidentReads incidents,
    WeatherReads weather,
    RiskReads risk,
    IClock clock,
    IEventDispatcher dispatcher,
    IOptions<SignalsOptions> options) : UniqueJob(lockService, logger)
{
    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var escalation = new SignalRules.EscalationThresholds(
            opts.WindowTargetMinutes, opts.WindowMinMinutes, opts.WindowMaxMinutes,
            opts.EscalationGrowthFactor, opts.EscalationAbsoluteGrowth, opts.EscalationAerialThreshold,
            opts.HysteresisGrowthFactor);
        var critical = new SignalRules.CriticalThresholds(
            opts.CriticalTempAbove, opts.CriticalHumidityBelow, opts.CriticalWindAbove);

        var fires = await incidents.ActiveAsync([IncidentKind.Fire], ct);
        if (fires.Count == 0)
            return;

        var now = clock.UtcNow;
        var ids = fires.Select(f => f.Id).ToList();
        var historyByIncident = (await incidents.HistoryByIncidentsAsync(ids, ct)).ToLookup(h => h.IncidentId);

        // Critical-conditions inputs, batched: nearest-station latest observation, concelho risk, heat waves.
        var stationIds = fires.Where(f => f.NearestWeatherStationId is not null)
            .Select(f => f.NearestWeatherStationId!.Value).Distinct().ToList();
        var latestObs = stationIds.Count > 0
            ? await weather.LatestByStationsAsync(stationIds, ct)
            : new Dictionary<int, WeatherObservation>();
        var dicos = fires.Select(f => f.Dico).Where(d => !string.IsNullOrEmpty(d)).Distinct().ToList();
        var risks = dicos.Count > 0
            ? await risk.ByDicosAsync(dicos, clock.LisbonToday, ct)
            : new Dictionary<string, Domain.Risk.ConcelhoRisk>();
        var ongoingHeatStations = (await weather.WavesAsync(ongoingOnly: true, ct))
            .Where(w => w.Type == WaveType.Heat)
            .Select(w => w.StationId)
            .ToHashSet();

        foreach (var fire in fires)
        {
            try
            {
                await ProcessFireAsync(fire, now, escalation, critical, opts, historyByIncident,
                    latestObs, risks, ongoingHeatStations, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Signals evaluation failed for incident {IncidentId}", fire.Id);
            }
        }
    }

    private async Task ProcessFireAsync(
        Incident fire,
        DateTimeOffset now,
        SignalRules.EscalationThresholds escalation,
        SignalRules.CriticalThresholds critical,
        SignalsOptions opts,
        ILookup<string, IncidentHistorySnapshot> historyByIncident,
        IReadOnlyDictionary<int, WeatherObservation> latestObs,
        IReadOnlyDictionary<string, Domain.Risk.ConcelhoRisk> risks,
        IReadOnlySet<int> ongoingHeatStations,
        CancellationToken ct)
    {
        var signals = fire.Signals;
        var updates = new List<UpdateDefinition<Incident>>();

        // ── Escalation ──────────────────────────────────────────────────────────
        var series = historyByIncident[fire.Id]
            .Select(h => (h.At, Assets: h.Terrain + h.Aerial, h.Aerial))
            .ToList();
        var wasEscalating = signals?.Escalating ?? false;
        var evaluated = SignalRules.EvaluateEscalation(series, now, wasEscalating, escalation);

        // The rule owns the growth test; the 30-min-since-detected gate lives here (stored state).
        var escalating = evaluated;
        if (wasEscalating && !evaluated
            && signals?.EscalationDetectedAt is { } detectedAt
            && now - detectedAt < TimeSpan.FromMinutes(opts.HysteresisMinMinutes))
        {
            escalating = true;
        }

        var peak = Math.Max(signals?.PeakAssets ?? 0, fire.Resources.TotalAssets);

        updates.Add(Builders<Incident>.Update.Set(x => x.Signals!.Escalating, escalating));
        updates.Add(Builders<Incident>.Update.Set(x => x.Signals!.PeakAssets, peak));

        var window = SignalRules.SelectWindow(series, now, escalation);
        var firstTransition = escalating && !wasEscalating;
        if (firstTransition)
            updates.Add(Builders<Incident>.Update.Set(x => x.Signals!.EscalationDetectedAt, now));

        // ── Critical conditions (hourly) ─────────────────────────────────────────
        var conditionsDue = signals?.ConditionsEvaluatedAt is not { } evaluatedAt
                            || now - evaluatedAt >= TimeSpan.FromMinutes(opts.ConditionsEvalMinMinutes);
        if (conditionsDue)
        {
            WeatherObservation? obs = fire.NearestWeatherStationId is { } stationId
                && latestObs.TryGetValue(stationId, out var found) ? found : null;
            int? riskLevel = !string.IsNullOrEmpty(fire.Dico) && risks.TryGetValue(fire.Dico, out var r) ? r.Today : null;
            var heatWave = fire.NearestWeatherStationId is { } sid && ongoingHeatStations.Contains(sid);

            var result = SignalRules.EvaluateCriticalConditions(
                obs?.Temperature, obs?.Humidity, obs?.WindSpeedKmh, riskLevel, heatWave, critical);

            updates.Add(Builders<Incident>.Update.Set(x => x.Signals!.CriticalConditions, result.Critical));
            updates.Add(Builders<Incident>.Update.Set(x => x.Signals!.CriticalReasons, result.Reasons.ToList()));
            updates.Add(Builders<Incident>.Update.Set(x => x.Signals!.ConditionsEvaluatedAt, now));
        }

        // Dispatch the escalation event BEFORE persisting the flag: if dispatch throws (or the process
        // dies) the $set never lands, so next run still sees wasEscalating=false and retries the sequence.
        // A dispatch-ok / persist-fails race is harmless — downstream consumers dedupe per incident
        // (EscalationPushHandler's escalationpush:{id} marker, WP4 alerts' esc:{id} key).
        if (firstTransition)
        {
            var previousAssets = window?.BaselineAssets ?? fire.Resources.TotalAssets;
            await dispatcher.DispatchAsync(
                new IncidentEscalating(fire.Id, fire.Resources.TotalAssets, previousAssets), ct: ct);
        }

        await mongo.Incidents.UpdateOneAsync(
            Builders<Incident>.Filter.Eq(x => x.Id, fire.Id),
            Builders<Incident>.Update.Combine(updates),
            cancellationToken: ct);
    }
}
