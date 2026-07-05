using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fogos.Domain.Aircraft;
using Fogos.Domain.Geo;
using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Domain.Locations;
using Fogos.Domain.Reports;
using Fogos.Domain.Risk;
using Fogos.Domain.Stats;
using Fogos.Domain.Time;
using Fogos.Domain.Warnings;
using Fogos.Domain.Weather;
using Fogos.Infrastructure.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.AdminCli;

/// <summary>
/// <c>demo-seed</c>: populates a throwaway database with deterministic, live-looking sample data so the
/// whole product surface (incident signals, response times, aircraft, KML versions, season stats, concelho
/// profiles, clusters, feeds, situation reports) can be explored end-to-end without a running pipeline.
///
/// Deterministic: a fixed RNG seed drives every jitter; all timestamps are RELATIVE to the run instant, so
/// the active fires always look live. Never touches the production <c>fogos</c> database (guarded).
/// </summary>
public sealed class DemoSeedCommand
{
    private const int Ok = 0;
    private const int Usage = 2;

    // A curated concelho: display name + a plausible in-concelho coordinate. DICO/district are resolved
    // from the seeded `locations` table at run time so we never hard-code a wrong code.
    private sealed record Town(string Name, double Lat, double Lng);

    private readonly MongoContext _mongo;
    private readonly IClock _clock;
    private readonly Random _rng = new(20260704);
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly List<(string Label, long Count)> _counts = [];

    // name (accent/case-folded) -> (dico, districtName), built from the seeded locations table.
    private readonly Dictionary<string, (string Dico, string District)> _byName = new();

    private DemoSeedCommand(MongoContext mongo, IClock clock)
    {
        _mongo = mongo;
        _clock = clock;
    }

    public static string DatabaseArg(string[] args) => GetOption(args, "--database") ?? "fogos_demo";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var mongo = services.GetRequiredService<MongoContext>();
        var clock = services.GetRequiredService<IClock>();
        var dbName = mongo.Database.DatabaseNamespace.DatabaseName;

        // Hard safety rail: demo-seed must never mutate the real database.
        if (string.Equals(dbName, "fogos", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Refusing to demo-seed the production 'fogos' database. Pass --database fogos_demo.");
            return Usage;
        }

        var locationsPath = GetOption(args, "--locations") ?? ResolveLocationsPath();
        if (locationsPath is null || !File.Exists(locationsPath))
        {
            Console.Error.WriteLine("Could not find dev/seed/locations.json. Pass --locations <path>.");
            return Usage;
        }

        var drop = args.Contains("--drop");
        if (drop)
        {
            Console.WriteLine($"Dropping database '{dbName}'…");
            await mongo.Database.Client.DropDatabaseAsync(dbName);
        }

        var seeder = new DemoSeedCommand(mongo, clock);
        await seeder.SeedAsync(locationsPath);
        seeder.PrintSummary(dbName);
        return Ok;
    }

    private async Task SeedAsync(string locationsPath)
    {
        // 0) Locations FIRST — an empty `locations` collection breaks ingest/enrichment everywhere.
        await SeedLocationsAsync(locationsPath);

        // 1) Weather stations (referenced by the active fires' nearest-station wiring).
        var stations = await SeedWeatherStationsAsync();

        // 2) Active incidents (8 fires + 1 urban + 1 Fma) with their status histories.
        var active = await SeedActiveIncidentsAsync(stations);

        // 3) Satellites of the big fire (a): 24h ramp, hotspots, KML versions, aircraft.
        await SeedBigFireSatellitesAsync(active);

        // 3b) Per-incident resource history for every OTHER active incident (+ the recently-closed neighbour),
        //     so the "resource evolution" panel is populated everywhere, not just on the big fire.
        await SeedActiveIncidentHistoryAsync(active);

        // 4) The ignition cluster (3 co-located active fires) + its cluster doc.
        await SeedClusterAsync(active);

        // 5) Closed incidents across the current year (stats: ignitions, causes, burn area, false alarms, response times).
        var (burnAreaHaYear, _) = await SeedClosedIncidentsAsync();

        // 5b) Two previous full years of closed incidents (YoY analytics: ignitionsByDay, burnAreaCumulative,
        //     responseTimeStats, falseAlarmStats, concelhoProfile.previousYearIgnitions), each with a lifecycle arc.
        await SeedPreviousYearsAsync();

        // 6) Weather observations + heat wave.
        await SeedWeatherObservationsAsync(stations);

        // 7) Risk (rcm_daily for all mainland concelhos + a minimal rcm_geojson).
        await SeedRiskAsync(active);

        // 8) Warnings (IPMA awareness + broadcast).
        var (weatherWarnings, broadcast) = await SeedWarningsAsync();

        // 9) Rolling nationwide totals.
        await SeedHistoryTotalsAsync(active);

        // 10) Morning situation report composed from the seeded numbers.
        await SeedSituationReportAsync(active, weatherWarnings + broadcast, burnAreaHaYear);
    }

    // ── 0) Locations ──────────────────────────────────────────────────────────────

    private async Task SeedLocationsAsync(string path)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var locations = new List<Location>();
        var districtCodeToName = new Dictionary<string, string>();

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var level = el.GetProperty("level").GetInt32();
            var code = el.GetProperty("code").GetString()!;
            var name = el.GetProperty("name").GetString()!;
            var id = el.GetProperty("id").GetString()!;
            var dico = el.TryGetProperty("dico", out var d) ? d.GetString() : null;

            locations.Add(new Location
            {
                Id = id,
                Level = level == 1 ? LocationLevel.Distrito : LocationLevel.Concelho,
                Code = code,
                Name = name,
                Dico = dico,
            });

            if (level == 1)
                districtCodeToName[code] = name;
        }

        // Build the name -> (dico, district) lookup used to place the curated towns.
        foreach (var loc in locations)
        {
            if (loc.Level != LocationLevel.Concelho || loc.Dico is not { } dico)
                continue;
            var districtCode = int.Parse(dico[..2], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            var district = districtCodeToName.GetValueOrDefault(districtCode, "");
            _byName[Normalize(loc.Name)] = (dico, district);
        }

        await _mongo.Locations.InsertManyAsync(locations);
        Record("locations", locations.Count);
    }

    // ── 1) Weather stations ─────────────────────────────────────────────────────────

    private sealed record DemoStation(int Id, string Name, double Lat, double Lng);

    private async Task<Dictionary<string, DemoStation>> SeedWeatherStationsAsync()
    {
        // Keyed by a slot name the fires reference.
        var stations = new Dictionary<string, DemoStation>
        {
            ["a"] = new(91001, "Coimbra / Arganil", 40.20, -8.05),
            ["b"] = new(91002, "Mação", 39.55, -8.00),
            ["cluster"] = new(91003, "Oleiros", 39.93, -7.90),
            ["d"] = new(91004, "Vila de Rei", 39.67, -8.14),
        };

        var docs = stations.Values.Select(s => new WeatherStation
        {
            Id = s.Id,
            Name = s.Name,
            Place = s.Name,
            Coordinates = GeoPoint.FromLatLng(s.Lat, s.Lng),
            UpdatedAt = _now.AddMinutes(-12),
        }).ToList();

        await _mongo.WeatherStations.InsertManyAsync(docs);
        Record("weather_stations", docs.Count);
        return stations;
    }

    // ── 2) Active incidents ─────────────────────────────────────────────────────────

    /// <summary>The seeded active incidents, keyed by slot ("a".."h", "urban", "fma").</summary>
    private sealed class ActiveSet
    {
        public Dictionary<string, Incident> Fires { get; } = new();
        public List<IncidentStatusChange> StatusHistory { get; } = [];

        /// <summary>The recently-concluded fire near (d) — closed, so it is kept out of <see cref="Fires"/>.</summary>
        public Incident ClosedNear { get; set; } = null!;
    }

    private async Task<ActiveSet> SeedActiveIncidentsAsync(Dictionary<string, DemoStation> stations)
    {
        var set = new ActiveSet();
        var incidents = new List<Incident>();

        // A recently-closed fire near (d), so (d) can be a PROXIMITY rekindle of it.
        var closedNear = MakeIncident("2026070400900", "Proença-a-Nova", 39.75, -7.92,
            occurredAt: _now.AddHours(-26), status: 10, kind: IncidentKind.Fire, natureza: "3103",
            resources: Res(man: 40, terrain: 12, aerial: 2), active: false);
        closedNear.UpdatedAt = _now.AddHours(-2); // "recently concluded"
        set.StatusHistory.AddRange(FullClosedHistory(closedNear.Id, closedNear.OccurredAt, dispatchMin: 12, controlMin: 90, conclusionMin: 40));
        set.ClosedNear = closedNear;
        incidents.Add(closedNear);

        // (a) Big escalating fire.
        var a = MakeIncident("2026070400001", "Arganil", 40.216, -8.056,
            occurredAt: _now.AddHours(-24), status: 5, kind: IncidentKind.Fire, natureza: "3101",
            resources: new Resources
            {
                Man = 520, Terrain = 160, Aerial = 20, Aquatic = 0,
                ManGround = 500, ManAerial = 20, Entities = 14,
                HeliFight = 3, HeliCoord = 1, PlaneFight = 2,
            },
            active: true, important: true);
        a.NearestWeatherStationId = stations["a"].Id;
        a.Signals = new IncidentSignals
        {
            Escalating = true,
            EscalationDetectedAt = _now.AddMinutes(-40),
            PeakAssets = 180,
        };
        // statusHistory: Despacho -> Chegada -> Em Curso.
        set.StatusHistory.Add(Change(a.Id, _now.AddHours(-24), 3));
        set.StatusHistory.Add(Change(a.Id, _now.AddHours(-23.5), 6));
        set.StatusHistory.Add(Change(a.Id, _now.AddHours(-23), 5));
        set.Fires["a"] = a;
        incidents.Add(a);

        // (b) Critical conditions (30-30-30 + risk max).
        var b = MakeIncident("2026070400002", "Mação", 39.554, -7.997,
            occurredAt: _now.AddHours(-6), status: 5, kind: IncidentKind.Fire, natureza: "3103",
            resources: Res(man: 90, terrain: 25, aerial: 4), active: true, important: true);
        b.NearestWeatherStationId = stations["b"].Id;
        b.Signals = new IncidentSignals
        {
            CriticalConditions = true,
            CriticalReasons = [SignalRules.TempAbove30, SignalRules.HumidityBelow30, SignalRules.RiskMaximum],
            ConditionsEvaluatedAt = _now.AddMinutes(-20),
        };
        set.StatusHistory.Add(Change(b.Id, _now.AddHours(-6), 3));
        set.StatusHistory.Add(Change(b.Id, _now.AddHours(-5.6), 6));
        set.StatusHistory.Add(Change(b.Id, _now.AddHours(-5.4), 5));
        set.Fires["b"] = b;
        incidents.Add(b);

        // (c) STATUS_REGRESSION rekindle: Em Resolução -> Em Curso.
        var c = MakeIncident("2026070400003", "Vila de Rei", 39.674, -8.143,
            occurredAt: _now.AddHours(-9), status: 5, kind: IncidentKind.Fire, natureza: "3101",
            resources: Res(man: 60, terrain: 18, aerial: 3), active: true, important: true);
        c.NearestWeatherStationId = stations["cluster"].Id;
        c.Signals = new IncidentSignals
        {
            Rekindle = true,
            RekindleKinds = ["STATUS_REGRESSION"],
            RekindleDetectedAt = _now.AddMinutes(-30),
        };
        set.StatusHistory.Add(Change(c.Id, _now.AddHours(-9), 3));
        set.StatusHistory.Add(Change(c.Id, _now.AddHours(-8.5), 6));
        set.StatusHistory.Add(Change(c.Id, _now.AddHours(-2), 7));
        set.StatusHistory.Add(Change(c.Id, _now.AddMinutes(-30), 5));
        set.Fires["c"] = c;
        incidents.Add(c);

        // (d) PROXIMITY rekindle pointing at the closed fire nearby.
        var dInc = MakeIncident("2026070400004", "Proença-a-Nova", 39.752, -7.918,
            occurredAt: _now.AddMinutes(-40), status: 5, kind: IncidentKind.Fire, natureza: "3101",
            resources: Res(man: 45, terrain: 14, aerial: 2), active: true, important: true);
        dInc.NearestWeatherStationId = stations["d"].Id;
        dInc.Signals = new IncidentSignals
        {
            Rekindle = true,
            RekindleKinds = ["PROXIMITY"],
            RekindleOfId = closedNear.Id,
            RekindleDetectedAt = _now.AddMinutes(-35),
        };
        set.StatusHistory.Add(Change(dInc.Id, _now.AddMinutes(-40), 3));
        set.StatusHistory.Add(Change(dInc.Id, _now.AddMinutes(-38), 5));
        set.Fires["d"] = dInc;
        incidents.Add(dInc);

        // (e) freshly dispatched — cluster member.
        var e = MakeIncident("2026070400005", "Oleiros", 39.930, -7.900,
            occurredAt: _now.AddMinutes(-10), status: 3, kind: IncidentKind.Fire, natureza: "3103",
            resources: Res(man: 12, terrain: 4, aerial: 0), active: true);
        e.NearestWeatherStationId = stations["cluster"].Id;
        set.StatusHistory.Add(Change(e.Id, _now.AddMinutes(-10), 3));
        set.Fires["e"] = e;
        incidents.Add(e);

        // (f) ordinary Em Curso — cluster member.
        var f = MakeIncident("2026070400006", "Oleiros", 39.951, -7.878,
            occurredAt: _now.AddMinutes(-90), status: 5, kind: IncidentKind.Fire, natureza: "3101",
            resources: Res(man: 35, terrain: 10, aerial: 2), active: true);
        f.NearestWeatherStationId = stations["cluster"].Id;
        set.StatusHistory.Add(Change(f.Id, _now.AddMinutes(-90), 3));
        set.StatusHistory.Add(Change(f.Id, _now.AddMinutes(-80), 6));
        set.StatusHistory.Add(Change(f.Id, _now.AddMinutes(-78), 5));
        set.Fires["f"] = f;
        incidents.Add(f);

        // (g) ordinary Em Resolução — cluster member. (Demo keeps it flagged active so it shows in the list.)
        var g = MakeIncident("2026070400007", "Oleiros", 39.912, -7.931,
            occurredAt: _now.AddMinutes(-150), status: 7, kind: IncidentKind.Fire, natureza: "3103",
            resources: Res(man: 40, terrain: 12, aerial: 2), active: true);
        g.NearestWeatherStationId = stations["cluster"].Id;
        set.StatusHistory.Add(Change(g.Id, _now.AddMinutes(-150), 3));
        set.StatusHistory.Add(Change(g.Id, _now.AddMinutes(-135), 6));
        set.StatusHistory.Add(Change(g.Id, _now.AddMinutes(-130), 5));
        set.StatusHistory.Add(Change(g.Id, _now.AddMinutes(-40), 7));
        set.Fires["g"] = g;
        incidents.Add(g);

        // (h) ordinary Em Curso, another concelho.
        var h = MakeIncident("2026070400008", "Sertã", 39.803, -8.101,
            occurredAt: _now.AddHours(-4), status: 5, kind: IncidentKind.Fire, natureza: "3101",
            resources: Res(man: 50, terrain: 15, aerial: 2), active: true, important: true);
        h.NearestWeatherStationId = stations["cluster"].Id;
        set.StatusHistory.Add(Change(h.Id, _now.AddHours(-4), 3));
        set.StatusHistory.Add(Change(h.Id, _now.AddHours(-3.7), 6));
        set.StatusHistory.Add(Change(h.Id, _now.AddHours(-3.6), 5));
        set.Fires["h"] = h;
        incidents.Add(h);

        // Extra active non-fire incidents (kept out of the 8-fire count; drive stats.activeOther and the map's
        // non-fire layers). activeIncidents defaults to fires only, so these do not inflate the fire list.
        var urban = MakeIncident("2026070400010", "Lisboa", 38.722, -9.139,
            occurredAt: _now.AddHours(-2), status: 5, kind: IncidentKind.UrbanFire, natureza: "2103",
            resources: Res(man: 22, terrain: 6, aerial: 0), active: true);
        set.StatusHistory.Add(Change(urban.Id, _now.AddHours(-2), 3));
        set.StatusHistory.Add(Change(urban.Id, _now.AddHours(-1.9), 5));
        set.Fires["urban"] = urban;
        incidents.Add(urban);

        var fma = MakeIncident("2026070400011", "Porto", 41.150, -8.610,
            occurredAt: _now.AddHours(-3), status: 5, kind: IncidentKind.Fma, natureza: "3301",
            resources: Res(man: 8, terrain: 3, aerial: 0), active: true);
        set.StatusHistory.Add(Change(fma.Id, _now.AddHours(-3), 3));
        set.StatusHistory.Add(Change(fma.Id, _now.AddHours(-2.9), 5));
        set.Fires["fma"] = fma;
        incidents.Add(fma);

        await _mongo.Incidents.InsertManyAsync(incidents);
        Record("incidents (active)", incidents.Count);

        await _mongo.IncidentStatusHistory.InsertManyAsync(set.StatusHistory);
        Record("incident_status_history (active)", set.StatusHistory.Count);

        return set;
    }

    // ── 3) Big-fire satellites ──────────────────────────────────────────────────────

    private async Task SeedBigFireSatellitesAsync(ActiveSet active)
    {
        var a = active.Fires["a"];
        var center = a.Coordinates!.Value;

        // 24h of resource snapshots ramping assets (terrain+aerial) ~20 -> ~180.
        var history = new List<IncidentHistorySnapshot>();
        for (var h = 24; h >= 0; h--)
        {
            var t = 1 - (h / 24.0); // 0 at 24h ago, 1 now
            var terrain = (int)Math.Round(15 + t * 145) + _rng.Next(-3, 4);
            var aerial = (int)Math.Round(5 + t * 15);
            var man = (int)Math.Round(40 + t * 480) + _rng.Next(-8, 9);
            history.Add(new IncidentHistorySnapshot
            {
                IncidentId = a.Id,
                At = _now.AddHours(-h),
                Man = Math.Max(0, man),
                Terrain = Math.Max(0, terrain),
                Aerial = Math.Max(0, aerial),
                Location = a.Location,
            });
        }
        await _mongo.IncidentHistory.InsertManyAsync(history);
        Record("incident_history", history.Count);

        // Hotspots: ~80 VIIRS + ~30 MODIS spreading outward over 12h.
        var hotspots = new Hotspots
        {
            IncidentId = a.Id,
            FetchedAt = _now.AddMinutes(-8),
            Viirs = BuildHotspotSamples(center, 80, spreadKm: 4.5),
            Modis = BuildHotspotSamples(center, 30, spreadKm: 3.0),
        };
        await _mongo.Hotspots.InsertOneAsync(hotspots);
        Record("hotspots", 1);

        // 3 growing KML perimeter versions; the newest is also mirrored inline on the incident.
        var versions = new List<IncidentKmlVersion>();
        string? latestKml = null;
        var offsets = new[] { (Hours: 6.0, RadiusKm: 0.8), (Hours: 3.0, RadiusKm: 1.6), (Hours: 0.5, RadiusKm: 2.7) };
        foreach (var (hours, radiusKm) in offsets)
        {
            var kml = BuildPolygonKml(center, radiusKm, $"Perímetro {a.Location} ({radiusKm:0.0} km)");
            latestKml = kml;
            var bytes = Encoding.UTF8.GetBytes(kml);
            versions.Add(new IncidentKmlVersion
            {
                IncidentId = a.Id,
                Vost = false,
                Kml = kml,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                SizeBytes = bytes.Length,
                CapturedAt = _now.AddHours(-hours),
            });
        }
        await _mongo.IncidentKmlVersions.InsertManyAsync(versions);
        Record("incident_kml_versions", versions.Count);

        // Mirror the latest perimeter inline so incident.hasKml is true and the map draws it.
        await _mongo.Incidents.UpdateOneAsync(
            Builders<Incident>.Filter.Eq(x => x.Id, a.Id),
            Builders<Incident>.Update.Set(x => x.Kml, latestKml));

        // 3 tracked aircraft + active links + recent flight positions near the fire.
        var fleet = new[]
        {
            new TrackedAircraft { Icao = "342029", Registration = "EC-MAB", Name = "Bombeiro 01", Type = "Canadair CL-415", Kind = "plane", Base = "Coimbra", Operator = "Babcock", Notify = true, Active = true },
            new TrackedAircraft { Icao = "342030", Registration = "EC-MAC", Name = "Bombeiro 02", Type = "Canadair CL-415", Kind = "plane", Base = "Coimbra", Operator = "Babcock", Notify = true, Active = true },
            new TrackedAircraft { Icao = "4951A2", Registration = "CS-HML", Name = "Kamov 07", Type = "Kamov Ka-32", Kind = "helicopter", Base = "Lousã", Operator = "Everjets", Notify = true, Active = true },
        };
        await _mongo.TrackedAircraft.InsertManyAsync(fleet);
        Record("tracked_aircraft", fleet.Length);

        var links = new List<IncidentAircraftLink>();
        var positions = new List<FlightPosition>();
        foreach (var ac in fleet)
        {
            links.Add(new IncidentAircraftLink
            {
                IncidentId = a.Id,
                Icao = ac.Icao,
                FirstSeenAt = _now.AddMinutes(-45),
                LastSeenAt = _now.AddMinutes(-2),
                Samples = 15,
                Active = true,
            });

            // ~6 samples over the last 10 minutes, circling the fire.
            for (var k = 0; k < 6; k++)
            {
                var angle = _rng.NextDouble() * 2 * Math.PI;
                var r = 0.01 + _rng.NextDouble() * 0.02; // ~1-3 km
                positions.Add(new FlightPosition
                {
                    Icao = ac.Icao,
                    Registration = ac.Registration,
                    Position = GeoPoint.FromLatLng(center.Latitude + r * Math.Sin(angle), center.Longitude + r * Math.Cos(angle)),
                    Altitude = ac.Kind == "helicopter" ? 350 + _rng.Next(0, 200) : 600 + _rng.Next(0, 400),
                    SampledAt = _now.AddMinutes(-(9 - k * 1.5)),
                    Source = "adsbfi",
                });
            }
        }
        await _mongo.IncidentAircraft.InsertManyAsync(links);
        Record("incident_aircraft", links.Count);
        await _mongo.FlightPositions.InsertManyAsync(positions);
        Record("flight_positions", positions.Count);
    }

    // ── 3b) Per-incident resource history (every active incident + the closed neighbour) ──────────────

    /// <summary>
    /// Builds a resource time series for every active incident EXCEPT the big fire (a) — which keeps its
    /// bespoke 24h ramp — plus a full lifecycle arc for the recently-closed neighbour. Cadence ≈ 20-40 min;
    /// the shape follows each incident's current status and the final snapshot matches its live resources.
    /// </summary>
    private async Task SeedActiveIncidentHistoryAsync(ActiveSet active)
    {
        var changesByIncident = active.StatusHistory
            .GroupBy(c => c.IncidentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<IncidentStatusChange>)g.OrderBy(c => c.At).ToList());

        IReadOnlyList<IncidentStatusChange> ChangesFor(string id) =>
            changesByIncident.GetValueOrDefault(id, []);

        var history = new List<IncidentHistorySnapshot>();

        // Every active incident except (a) — includes the non-fire urban/fma so their panels are populated too.
        foreach (var inc in active.Fires.Values)
        {
            if (ReferenceEquals(inc, active.Fires["a"]))
                continue;
            history.AddRange(BuildLiveArc(inc, ChangesFor(inc.Id)));
        }

        // The recently-concluded neighbour gets a full ramp-up → peak → ramp-down lifecycle arc.
        var near = active.ClosedNear;
        var nearChanges = ChangesFor(near.Id);
        var nearControl = nearChanges.FirstOrDefault(c => c.Code == IncidentStatusCatalog.EmResolucao)?.At ?? near.OccurredAt;
        var nearConclusion = nearChanges.FirstOrDefault(c => c.Code == IncidentStatusCatalog.Conclusao)?.At ?? near.UpdatedAt;
        history.AddRange(BuildClosedArc(near, near.OccurredAt, nearControl, nearConclusion));

        await _mongo.IncidentHistory.InsertManyAsync(history);
        Record("incident_history (active)", history.Count);
    }

    /// <summary>
    /// Snapshots from an active incident's OccurredAt to now, stepped ≈ 20-40 min. The ramp shape follows the
    /// incident's current status (fresh Despacho = a couple of low points; Em Curso = climbing; Em Resolução =
    /// climb then plateau/decline); the final snapshot is pinned to the incident's live resources.
    /// </summary>
    private List<IncidentHistorySnapshot> BuildLiveArc(Incident inc, IReadOnlyList<IncidentStatusChange> changes)
    {
        _ = changes;
        var t0 = inc.OccurredAt;
        var end = _now;
        var peak = inc.Resources;
        var span = (end - t0).TotalMinutes;
        var code = inc.Status.Code;

        var times = Timeline(t0, end);
        var snaps = new List<IncidentHistorySnapshot>(times.Count);
        for (var k = 0; k < times.Count; k++)
        {
            var at = times[k];
            var isLast = k == times.Count - 1;
            var u = span <= 0 ? 1.0 : Math.Clamp((at - t0).TotalMinutes / span, 0, 1);

            double frac = code switch
            {
                IncidentStatusCatalog.Despacho or IncidentStatusCatalog.DespachoPrimeiroAlerta => 0.40 + 0.60 * u,
                IncidentStatusCatalog.EmCurso or IncidentStatusCatalog.ChegadaAoTeatroDeOperacoes => 0.15 + 0.85 * Math.Pow(u, 0.75),
                // Em Resolução: rise to a slight overshoot ~0.6 through, then settle back to the current level.
                IncidentStatusCatalog.EmResolucao => u < 0.6 ? 0.20 + (1.12 - 0.20) * (u / 0.6) : 1.12 - 0.12 * ((u - 0.6) / 0.4),
                _ => 0.20 + 0.80 * u,
            };

            snaps.Add(Snapshot(inc, at, peak, frac, pin: isLast));
        }
        return snaps;
    }

    /// <summary>
    /// Full lifecycle arc for a closed incident: ramp up from ignition to a peak at the Em Curso→Em Resolução
    /// transition, then decline to near zero at Conclusão. Snapshot count (8–25) scales with the total duration.
    /// </summary>
    private List<IncidentHistorySnapshot> BuildClosedArc(Incident inc, DateTimeOffset t0, DateTimeOffset controlAt, DateTimeOffset conclusionAt)
    {
        var peak = inc.Resources;
        // Guard against degenerate ordering so the fractions below stay well-defined.
        if (controlAt <= t0) controlAt = t0.AddMinutes(1);
        if (conclusionAt <= controlAt) conclusionAt = controlAt.AddMinutes(1);

        var totalMin = (conclusionAt - t0).TotalMinutes;
        var count = Math.Clamp((int)Math.Round(totalMin / 25.0), 8, 25);

        var upSpan = (controlAt - t0).TotalMinutes;
        var downSpan = (conclusionAt - controlAt).TotalMinutes;

        var snaps = new List<IncidentHistorySnapshot>(count);
        for (var k = 0; k < count; k++)
        {
            var at = t0 + TimeSpan.FromMinutes(totalMin * k / (count - 1));
            double frac = at <= controlAt
                ? 0.10 + 0.90 * (at - t0).TotalMinutes / upSpan            // ignition → peak at control
                : 1.00 - 0.95 * (at - controlAt).TotalMinutes / downSpan;  // peak → near zero at conclusion
            snaps.Add(Snapshot(inc, at, peak, frac, pin: false));
        }
        return snaps;
    }

    /// <summary>
    /// One resource snapshot at fraction <paramref name="frac"/> of the incident's peak deployment, with a little
    /// deterministic jitter. Aerial scales with the fraction too, so it only appears on incidents that field it.
    /// When <paramref name="pin"/> is set the values are the exact live resources (last point of an active arc).
    /// </summary>
    private IncidentHistorySnapshot Snapshot(Incident inc, DateTimeOffset at, Resources peak, double frac, bool pin)
    {
        frac = Math.Clamp(frac, 0, 1.2);
        int Scale(int value, double jitterFrac)
        {
            if (value <= 0) return 0;
            var jitter = (int)Math.Round(value * jitterFrac * (_rng.NextDouble() * 2 - 1));
            return Math.Max(0, (int)Math.Round(value * frac) + jitter);
        }

        return new IncidentHistorySnapshot
        {
            IncidentId = inc.Id,
            At = at,
            Man = pin ? peak.Man : Scale(peak.Man, 0.05),
            Terrain = pin ? peak.Terrain : Scale(peak.Terrain, 0.06),
            Aerial = pin ? peak.Aerial : Scale(peak.Aerial, 0.10),
            Location = inc.Location,
        };
    }

    /// <summary>Timestamps from <paramref name="t0"/> to <paramref name="end"/> stepped ≈ 20-40 min, with <paramref name="end"/> always the last point.</summary>
    private List<DateTimeOffset> Timeline(DateTimeOffset t0, DateTimeOffset end)
    {
        var times = new List<DateTimeOffset> { t0 };
        var t = t0;
        while (true)
        {
            t = t.AddMinutes(20 + _rng.Next(0, 21)); // 20-40 min
            if (t >= end.AddMinutes(-5))
                break;
            times.Add(t);
        }
        if (times[^1] != end)
            times.Add(end);
        return times;
    }

    // ── 4) Ignition cluster ─────────────────────────────────────────────────────────

    private async Task SeedClusterAsync(ActiveSet active)
    {
        var members = new[] { active.Fires["e"], active.Fires["f"], active.Fires["g"] };
        var centroid = IgnitionClustering.Centroid(
            members.Select(m => new IgnitionClustering.Point(m.Id, m.Coordinates!.Value)).ToList());

        var cluster = new IgnitionCluster
        {
            IncidentIds = members.Select(m => m.Id).ToList(),
            Centroid = centroid,
            FirstAt = members.Min(m => m.OccurredAt),
            LastAt = members.Max(m => m.OccurredAt),
            Concelhos = members.Select(m => m.Concelho).Distinct().ToList(),
            Active = true,
            UpdatedAt = _now.AddMinutes(-5),
        };
        await _mongo.IgnitionClusters.InsertOneAsync(cluster);
        Record("ignition_clusters", 1);
    }

    // ── 5) Closed incidents (season stats) ──────────────────────────────────────────

    private static readonly string[] CauseFamilies =
        ["Reacendimento", "Uso do fogo", "Incendiarismo", "Causa natural", "Estruturais/Acidentais"];

    private async Task<(long BurnAreaHaYear, int Count)> SeedClosedIncidentsAsync()
    {
        // Three "busy" districts, each with a pool of concelhos. ~24 incidents each guarantees the
        // ≥20-per-district gate on falseAlarmStats and gives responseTimeStats a real median.
        var pools = new (string District, Town[] Towns)[]
        {
            ("Coimbra", [
                new("Arganil", 40.216, -8.056), new("Lousã", 40.116, -8.245), new("Góis", 40.156, -8.111),
                new("Pampilhosa da Serra", 40.046, -7.953), new("Penela", 40.031, -8.390), new("Miranda do Corvo", 40.093, -8.331),
                new("Tábua", 40.363, -8.031), new("Oliveira do Hospital", 40.360, -7.863), new("Coimbra", 40.203, -8.410),
                new("Figueira da Foz", 40.150, -8.861),
            ]),
            ("Castelo Branco", [
                new("Oleiros", 39.930, -7.900), new("Vila de Rei", 39.674, -8.143), new("Proença-a-Nova", 39.752, -7.918),
                new("Sertã", 39.803, -8.101), new("Castelo Branco", 39.822, -7.492), new("Fundão", 40.139, -7.501),
                new("Idanha-a-Nova", 39.923, -7.240), new("Covilhã", 40.281, -7.505), new("Vila Velha de Ródão", 39.658, -7.669),
                new("Penamacor", 40.166, -7.170),
            ]),
            ("Santarém", [
                new("Mação", 39.554, -7.997), new("Abrantes", 39.463, -8.198), new("Ourém", 39.653, -8.581),
                new("Tomar", 39.601, -8.412), new("Sardoal", 39.535, -8.159), new("Ferreira do Zêzere", 39.700, -8.290),
                new("Constância", 39.478, -8.337), new("Santarém", 39.234, -8.687), new("Torres Novas", 39.480, -8.540),
                new("Chamusca", 39.361, -8.480),
            ]),
        };

        // A few extra districts for spread (below the 20 gate — realistic long tail).
        var extras = new (string District, Town Town)[]
        {
            ("Guarda", new("Guarda", 40.537, -7.267)),
            ("Viseu", new("Viseu", 40.657, -7.914)),
            ("Vila Real", new("Vila Real", 41.301, -7.744)),
            ("Braga", new("Braga", 41.545, -8.426)),
            ("Faro", new("Loulé", 37.137, -8.021)),
            ("Lisboa", new("Sintra", 38.800, -9.388)),
        };

        var incidents = new List<Incident>();
        var statusHistory = new List<IncidentStatusChange>();
        var history = new List<IncidentHistorySnapshot>();
        var seq = 20000;
        long burnAreaHaYear = 0;
        var icnfAssigned = 0;

        foreach (var (district, towns) in pools)
        {
            const int perDistrict = 24;
            const int falseAlarmsPerDistrict = 3;
            for (var i = 0; i < perDistrict; i++)
            {
                var town = towns[i % towns.Length];
                var occurredAt = SpreadOverYear(i, perDistrict);
                var id = $"20260000{seq++}";
                var isFalse = i < falseAlarmsPerDistrict;

                if (isFalse)
                {
                    var code = i % 2 == 0 ? 11 : 12;
                    var inc = MakeIncident(id, town.Name, town.Lat, town.Lng, occurredAt, code, IncidentKind.Fire, "3101",
                        Res(man: _rng.Next(1, 6), terrain: 1, aerial: 0), active: false);
                    inc.UpdatedAt = occurredAt.AddMinutes(30);
                    statusHistory.Add(Change(id, occurredAt, 3));
                    statusHistory.Add(Change(id, occurredAt.AddMinutes(_rng.Next(10, 40)), code));
                    incidents.Add(inc);
                    continue;
                }

                var man = _rng.Next(8, 120);
                var closed = MakeIncident(id, town.Name, town.Lat, town.Lng, occurredAt, 10, IncidentKind.Fire, "3103",
                    Res(man: man, terrain: man / 4, aerial: _rng.Next(0, 4)), active: false);

                var dispatchMin = _rng.Next(4, 40);
                var controlMin = _rng.Next(25, 300);
                var conclusionMin = _rng.Next(10, 240);
                statusHistory.AddRange(FullClosedHistory(id, occurredAt, dispatchMin, controlMin, conclusionMin));
                closed.UpdatedAt = occurredAt.AddMinutes(dispatchMin + controlMin + conclusionMin);
                var controlAt = occurredAt.AddMinutes(dispatchMin + controlMin);
                history.AddRange(BuildClosedArc(closed, occurredAt, controlAt, closed.UpdatedAt));

                // ICNF enrichment on roughly a third — varied cause families + burn area.
                if (i % 3 == 0)
                {
                    var burn = Math.Round(0.4 + _rng.NextDouble() * 480, 1);
                    burnAreaHaYear += (long)burn;
                    icnfAssigned++;
                    closed.Icnf = new IcnfData
                    {
                        CauseFamily = CauseFamilies[icnfAssigned % CauseFamilies.Length],
                        CauseType = "Negligente",
                        Cause = "Queima de sobrantes",
                        BurnArea = new BurnArea(
                            Povoamento: Math.Round(burn * 0.6, 1),
                            Agricola: Math.Round(burn * 0.1, 1),
                            Mato: Math.Round(burn * 0.3, 1),
                            Total: burn),
                        IcnfId = $"NCCO{seq}",
                        UpdatedAt = closed.UpdatedAt,
                    };
                }
                incidents.Add(closed);
            }
        }

        var extraIdx = 0;
        foreach (var (district, town) in extras)
        {
            _ = district;
            for (var i = 0; i < 3; i++)
            {
                var occurredAt = SpreadOverYear(extraIdx++, extras.Length * 3);
                var id = $"20260000{seq++}";
                var man = _rng.Next(6, 60);
                var closed = MakeIncident(id, town.Name, town.Lat, town.Lng, occurredAt, 10, IncidentKind.Fire, "3101",
                    Res(man: man, terrain: man / 4, aerial: _rng.Next(0, 3)), active: false);
                var dispatchMin = _rng.Next(5, 35);
                var controlMin = _rng.Next(30, 200);
                var conclusionMin = _rng.Next(15, 90);
                statusHistory.AddRange(FullClosedHistory(id, occurredAt, dispatchMin, controlMin, conclusionMin));
                closed.UpdatedAt = occurredAt.AddMinutes(dispatchMin + controlMin + conclusionMin);
                var controlAt = occurredAt.AddMinutes(dispatchMin + controlMin);
                history.AddRange(BuildClosedArc(closed, occurredAt, controlAt, closed.UpdatedAt));
                incidents.Add(closed);
            }
        }

        await _mongo.Incidents.InsertManyAsync(incidents);
        Record("incidents (closed)", incidents.Count);
        await _mongo.IncidentStatusHistory.InsertManyAsync(statusHistory);
        Record("incident_status_history (closed)", statusHistory.Count);
        await _mongo.IncidentHistory.InsertManyAsync(history);
        Record("incident_history (closed)", history.Count);
        return (burnAreaHaYear, incidents.Count);
    }

    // ── 5b) Previous years (YoY analytics) ────────────────────────────────────────────

    /// <summary>
    /// Seeds TWO previous full calendar years of closed incidents so the YoY surfaces have something to compare
    /// against: ~300 per year, summer-heavy, spread across the same district/concelho pool. Each carries a full
    /// status log (varied response/control durations) and a lifecycle resource arc; ~30% carry ICNF cause +
    /// burn-area (previous years accumulate a clearly larger total than the current partial year), and every
    /// main district clears the ≥20-total false-alarm gate. Arganil (0601) and Oleiros are guaranteed several
    /// early-season ignitions so <c>concelhoProfile.previousYearIgnitions</c> is non-zero for both.
    /// </summary>
    private async Task SeedPreviousYearsAsync()
    {
        // The three "busy" districts drive the false-alarm gate + response-time medians; Arganil and Oleiros
        // (indexes 0) are cycled first so the profile concelhos always get several ignitions.
        var mainPools = new (string District, Town[] Towns)[]
        {
            ("Coimbra", [
                new("Arganil", 40.216, -8.056), new("Lousã", 40.116, -8.245), new("Góis", 40.156, -8.111),
                new("Pampilhosa da Serra", 40.046, -7.953), new("Penela", 40.031, -8.390), new("Miranda do Corvo", 40.093, -8.331),
                new("Tábua", 40.363, -8.031), new("Oliveira do Hospital", 40.360, -7.863), new("Coimbra", 40.203, -8.410),
                new("Figueira da Foz", 40.150, -8.861),
            ]),
            ("Castelo Branco", [
                new("Oleiros", 39.930, -7.900), new("Vila de Rei", 39.674, -8.143), new("Proença-a-Nova", 39.752, -7.918),
                new("Sertã", 39.803, -8.101), new("Castelo Branco", 39.822, -7.492), new("Fundão", 40.139, -7.501),
                new("Idanha-a-Nova", 39.923, -7.240), new("Covilhã", 40.281, -7.505), new("Vila Velha de Ródão", 39.658, -7.669),
                new("Penamacor", 40.166, -7.170),
            ]),
            ("Santarém", [
                new("Mação", 39.554, -7.997), new("Abrantes", 39.463, -8.198), new("Ourém", 39.653, -8.581),
                new("Tomar", 39.601, -8.412), new("Sardoal", 39.535, -8.159), new("Ferreira do Zêzere", 39.700, -8.290),
                new("Constância", 39.478, -8.337), new("Santarém", 39.234, -8.687), new("Torres Novas", 39.480, -8.540),
                new("Chamusca", 39.361, -8.480),
            ]),
        };

        // A wider spread pool (a few extras beyond the busy districts). Guarded against the locations table so a
        // name that isn't seeded is simply skipped rather than throwing.
        var spread = new Town[]
        {
            new("Guarda", 40.537, -7.267), new("Viseu", 40.657, -7.914), new("Vila Real", 41.301, -7.744),
            new("Braga", 41.545, -8.426), new("Loulé", 37.137, -8.021), new("Sintra", 38.800, -9.388),
            new("Bragança", 41.806, -6.757), new("Portalegre", 39.293, -7.431), new("Évora", 38.571, -7.913),
            new("Beja", 38.015, -7.863), new("Viana do Castelo", 41.694, -8.834), new("Leiria", 39.749, -8.807),
            new("Setúbal", 38.524, -8.894), new("Aveiro", 40.641, -8.653), new("Guimarães", 41.444, -8.296),
        }.Where(t => _byName.ContainsKey(Normalize(t.Name))).ToArray();

        var incidents = new List<Incident>();
        var statusHistory = new List<IncidentStatusChange>();
        var history = new List<IncidentHistorySnapshot>();
        var burnByYear = new Dictionary<int, long>();
        var countByYear = new Dictionary<int, int>();
        var thisYear = _clock.LisbonToday.Year;

        // A closed incident in a specific previous year, dated on the given Lisbon day.
        void AddClosed(int year, Town town, DateTimeOffset occurredAt, ref int seq, bool allowIcnf, bool allowFalse)
        {
            var id = $"{year}90{seq++:D5}";

            // ~10% false alarms in the busy districts so falseAlarmStats has non-zero numerators past the gate.
            if (allowFalse && _rng.NextDouble() < 0.10)
            {
                var code = _rng.Next(0, 2) == 0 ? IncidentStatusCatalog.FalsoAlarme : IncidentStatusCatalog.FalsoAlerta;
                var fa = MakeIncident(id, town.Name, town.Lat, town.Lng, occurredAt, code, IncidentKind.Fire, "3101",
                    Res(man: _rng.Next(1, 6), terrain: 1, aerial: 0), active: false);
                fa.UpdatedAt = occurredAt.AddMinutes(_rng.Next(15, 45));
                statusHistory.Add(Change(id, occurredAt, 3));
                statusHistory.Add(Change(id, occurredAt.AddMinutes(_rng.Next(10, 40)), code));
                incidents.Add(fa);
                countByYear[year] = countByYear.GetValueOrDefault(year) + 1;
                return;
            }

            var man = _rng.Next(8, 160);
            var natureza = _rng.Next(0, 2) == 0 ? "3101" : "3103";
            var closed = MakeIncident(id, town.Name, town.Lat, town.Lng, occurredAt, IncidentStatusCatalog.Encerrada,
                IncidentKind.Fire, natureza, Res(man: man, terrain: man / 4, aerial: man > 60 ? _rng.Next(0, 5) : 0), active: false);

            var dispatchMin = _rng.Next(4, 45);
            var controlMin = _rng.Next(25, 340);
            var conclusionMin = _rng.Next(10, 260);
            statusHistory.AddRange(FullClosedHistory(id, occurredAt, dispatchMin, controlMin, conclusionMin));
            closed.UpdatedAt = occurredAt.AddMinutes(dispatchMin + controlMin + conclusionMin);
            var controlAt = occurredAt.AddMinutes(dispatchMin + controlMin);
            history.AddRange(BuildClosedArc(closed, occurredAt, controlAt, closed.UpdatedAt));

            // ~30% ICNF-enriched. Burn areas run a bit larger than the current partial year so the YoY gap is clear.
            if (allowIcnf && _rng.NextDouble() < 0.30)
            {
                var burn = Math.Round(0.5 + _rng.NextDouble() * 360, 1);
                burnByYear[year] = burnByYear.GetValueOrDefault(year) + (long)burn;
                closed.Icnf = new IcnfData
                {
                    CauseFamily = CauseFamilies[_rng.Next(0, CauseFamilies.Length)],
                    CauseType = _rng.Next(0, 2) == 0 ? "Negligente" : "Intencional",
                    Cause = "Queima de sobrantes",
                    BurnArea = new BurnArea(
                        Povoamento: Math.Round(burn * 0.6, 1),
                        Agricola: Math.Round(burn * 0.1, 1),
                        Mato: Math.Round(burn * 0.3, 1),
                        Total: burn),
                    IcnfId = $"NCCO{id}",
                    UpdatedAt = closed.UpdatedAt,
                };
            }

            incidents.Add(closed);
            countByYear[year] = countByYear.GetValueOrDefault(year) + 1;
        }

        foreach (var year in new[] { thisYear - 1, thisYear - 2 })
        {
            var seq = 0;

            // Busy districts: 60 each = 180 → clears the ≥20 false-alarm gate with room to spare.
            foreach (var (district, towns) in mainPools)
            {
                _ = district;
                for (var i = 0; i < 60; i++)
                {
                    var town = towns[i % towns.Length];
                    AddClosed(year, town, SeasonalDay(year), ref seq, allowIcnf: true, allowFalse: true);
                }
            }

            // Spread pool: ~8 each → wider district coverage in the long tail.
            foreach (var town in spread)
                for (var i = 0; i < 8; i++)
                    AddClosed(year, town, SeasonalDay(year), ref seq, allowIcnf: true, allowFalse: false);

            // Guarantee Arganil (0601) and Oleiros get several EARLY-season ignitions (before ~Jul), so the
            // concelho profile's previous-year counter — a Jan-1→same-day-last-year window — is always non-zero.
            foreach (var town in new[] { new Town("Arganil", 40.216, -8.056), new Town("Oleiros", 39.930, -7.900) })
                for (var i = 0; i < 4; i++)
                    AddClosed(year, town, EarlySeasonDay(year), ref seq, allowIcnf: true, allowFalse: false);
        }

        await _mongo.Incidents.InsertManyAsync(incidents);
        Record("incidents (previous years)", incidents.Count);
        await _mongo.IncidentStatusHistory.InsertManyAsync(statusHistory);
        Record("incident_status_history (previous years)", statusHistory.Count);
        await _mongo.IncidentHistory.InsertManyAsync(history);
        Record("incident_history (previous years)", history.Count);

        foreach (var year in new[] { thisYear - 1, thisYear - 2 })
            Console.WriteLine($"  · {year}: {countByYear.GetValueOrDefault(year)} incidents, ~{burnByYear.GetValueOrDefault(year)} ha accounted burn area");
    }

    // ── 6) Weather observations + heat wave ──────────────────────────────────────────

    private async Task SeedWeatherObservationsAsync(Dictionary<string, DemoStation> stations)
    {
        var obs = new List<WeatherObservation>
        {
            Obs(stations["a"].Id, temp: 33.1, hum: 24, wind: 28, dir: "NE"),
            // Fire (b)'s station: 36 / 18 / 42 — consistent with its critical-conditions reasons.
            Obs(stations["b"].Id, temp: 36.4, hum: 18, wind: 42, dir: "NE"),
            Obs(stations["cluster"].Id, temp: 31.2, hum: 27, wind: 25, dir: "E"),
            Obs(stations["d"].Id, temp: 30.4, hum: 30, wind: 20, dir: "SE"),
        };
        await _mongo.WeatherHourly.InsertManyAsync(obs);
        Record("weather_hourly", obs.Count);

        // Ongoing heat wave over fire (b)'s station/district.
        var today = _clock.LisbonToday;
        var days = new List<WaveDay>();
        for (var d = 5; d >= 0; d--)
        {
            var date = today.AddDays(-d);
            var normal = 29.5;
            var observed = 35 + _rng.NextDouble() * 3;
            days.Add(new WaveDay(date, Math.Round(observed, 1), normal, Math.Round(observed - normal, 1)));
        }
        var wave = new TemperatureWave
        {
            StationId = stations["b"].Id,
            Type = WaveType.Heat,
            StartDate = today.AddDays(-5),
            EndDate = today,
            Ongoing = true,
            Days = days,
            UpdatedAt = _now.AddHours(-1),
        };
        await _mongo.TemperatureWaves.InsertOneAsync(wave);
        Record("temperature_waves", 1);
    }

    // ── 7) Risk ─────────────────────────────────────────────────────────────────────

    private async Task SeedRiskAsync(ActiveSet active)
    {
        var today = _clock.LisbonToday;
        var maxRiskDicos = new HashSet<string> { active.Fires["a"].Dico, active.Fires["b"].Dico };

        var docs = new List<ConcelhoRisk>();
        // Every mainland concelho (dico prefix 01-18) gets a risk row for today.
        foreach (var (name, (dico, district)) in _byName)
        {
            _ = name;
            var prefix = int.Parse(dico[..2], CultureInfo.InvariantCulture);
            if (prefix is < 1 or > 18)
                continue; // islands: skip

            if (maxRiskDicos.Contains(dico))
            {
                docs.Add(new ConcelhoRisk { Dico = dico, Concelho = district, Date = today, Today = 5, Tomorrow = 5, After = 4, After2 = 3, After3 = 3 });
            }
            else
            {
                var baseLevel = _rng.Next(2, 4); // 2 or 3
                docs.Add(new ConcelhoRisk
                {
                    Dico = dico,
                    Concelho = district,
                    Date = today,
                    Today = baseLevel,
                    Tomorrow = Math.Clamp(baseLevel + _rng.Next(-1, 2), 1, 5),
                    After = Math.Clamp(baseLevel + _rng.Next(-1, 2), 1, 5),
                    After2 = Math.Clamp(baseLevel + _rng.Next(-1, 1), 1, 5),
                    After3 = Math.Clamp(baseLevel + _rng.Next(-1, 1), 1, 5),
                });
            }
        }
        await _mongo.RcmDaily.InsertManyAsync(docs);
        Record("rcm_daily", docs.Count);

        // Minimal but valid GeoJSON per horizon (backs the map-level fireRisk query).
        var geo = new List<RiskGeoJson>();
        foreach (var (when, offset) in new[] { (RiskDay.Today, 0), (RiskDay.Tomorrow, 1), (RiskDay.After, 2) })
        {
            geo.Add(new RiskGeoJson
            {
                When = when,
                ForecastDate = today.AddDays(offset),
                RunAt = _now.AddHours(-6),
                GeoJson = MinimalRiskGeoJson(),
                UpdatedAt = _now.AddHours(-6),
            });
        }
        await _mongo.RcmGeoJson.InsertManyAsync(geo);
        Record("rcm_geojson", geo.Count);
    }

    // ── 8) Warnings ──────────────────────────────────────────────────────────────────

    private async Task<(int WeatherCount, int BroadcastCount)> SeedWarningsAsync()
    {
        var endsAt = _now.AddDays(2);
        var weather = new[]
        {
            NewWeatherWarning("CBR", "orange", endsAt), // Coimbra — fire (a)'s district
            NewWeatherWarning("STR", "red", endsAt),    // Santarém — fire (b)'s district
            NewWeatherWarning("CTB", "yellow", endsAt), // Castelo Branco — cluster district
        };
        await _mongo.WeatherWarnings.InsertManyAsync(weather);
        Record("weather_warnings", weather.Length);

        var broadcast = new[]
        {
            new Warning
            {
                Kind = WarningKind.Manual,
                Message = "Risco máximo de incêndio rural no interior centro. Está proibido o uso de fogo, foguetes e queimadas.",
                Url = "https://fogos.pt",
                IssuedBy = "Proteção Civil",
                CreatedAt = _now.AddHours(-3),
            },
            new Warning
            {
                Kind = WarningKind.Site,
                Message = "Aviso especial em vigor até domingo devido à onda de calor. Mantenha-se hidratado e evite deslocações às horas de maior calor.",
                Url = null,
                IssuedBy = "Fogos.pt",
                CreatedAt = _now.AddHours(-6),
            },
        };
        await _mongo.Warnings.InsertManyAsync(broadcast);
        Record("warnings", broadcast.Length);
        return (weather.Length, broadcast.Length);
    }

    // ── 9) History totals ─────────────────────────────────────────────────────────────

    private async Task SeedHistoryTotalsAsync(ActiveSet active)
    {
        var fires = active.Fires.Values.Where(i => i.Kind == IncidentKind.Fire).ToList();
        var curMan = fires.Sum(i => i.Resources.Man);
        var curTerrain = fires.Sum(i => i.Resources.Terrain);
        var curAerial = fires.Sum(i => i.Resources.Aerial);

        var totals = new List<HistoryTotal>();
        // 14 days at 30-min steps, ramping up to the current nationwide numbers with a mild day/night cycle
        // (assets peak in the afternoon, ebb overnight).
        const int steps = 14 * 48; // 672 half-hours
        for (var step = steps; step >= 0; step--)
        {
            var t = 1 - (step / (double)steps);
            var at = _now.AddMinutes(-30 * step);
            // Diurnal factor: ~1.0 mid-afternoon, ~0.7 pre-dawn. Peak near 16:00 Lisbon.
            var hour = _clock.ToLisbon(at).Hour + _clock.ToLisbon(at).Minute / 60.0;
            var diurnal = 0.85 + 0.15 * Math.Cos((hour - 16) / 24.0 * 2 * Math.PI);
            var envelope = (0.25 + 0.75 * t) * diurnal;
            var man = Math.Max(0, (int)Math.Round(curMan * envelope) + _rng.Next(-10, 11));
            var terrain = Math.Max(0, (int)Math.Round(curTerrain * envelope) + _rng.Next(-4, 5));
            var aerial = Math.Max(0, (int)Math.Round(curAerial * envelope));
            totals.Add(new HistoryTotal
            {
                At = at,
                Man = man,
                Terrain = terrain,
                Aerial = aerial,
                Total = man + terrain + aerial,
            });
        }
        await _mongo.HistoryTotals.InsertManyAsync(totals);
        Record("history_totals", totals.Count);
    }

    // ── 10) Situation report ──────────────────────────────────────────────────────────

    private async Task SeedSituationReportAsync(ActiveSet active, int warnings12h, long burnAreaHaYear)
    {
        var fires = active.Fires.Values.Where(i => i.Kind == IncidentKind.Fire).ToList();
        var man = fires.Sum(i => i.Resources.Man);
        var terrain = fires.Sum(i => i.Resources.Terrain);
        var aerial = fires.Sum(i => i.Resources.Aerial);
        var escalating = fires.Count(i => i.Signals?.Escalating == true);
        var top = fires
            .OrderByDescending(i => i.Resources.Terrain + i.Resources.Aerial)
            .Take(3)
            .Select(i => (i.Concelho, Assets: i.Resources.Terrain + i.Resources.Aerial))
            .ToList();

        var today = _clock.LisbonToday;
        var at = _clock.FromLisbon(new DateTime(today.Year, today.Month, today.Day, 9, 0, 0));
        var dateLabel = today.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var body = ComposeSituationBody("morning", dateLabel, fires.Count, man, terrain, aerial, escalating, warnings12h, burnAreaHaYear, top);

        var report = new SituationReport
        {
            At = at,
            Slot = "morning",
            Body = body,
            ActiveFires = fires.Count,
            TotalMan = man,
            TotalTerrain = terrain,
            TotalAerial = aerial,
            TopIncidentIds = fires
                .OrderByDescending(i => i.Resources.Terrain + i.Resources.Aerial)
                .Take(3)
                .Select(i => i.Id)
                .ToList(),
        };
        await _mongo.SituationReports.InsertOneAsync(report);
        Record("situation_reports", 1);
    }

    /// <summary>Mirrors Fogos.Worker's SituationReportCopy tone (emoji-led European Portuguese).</summary>
    private static string ComposeSituationBody(
        string slot, string dateLabel, int activeFires, int man, int terrain, int aerial,
        int escalating, int warnings12h, long burnAreaHaYear, IReadOnlyList<(string Concelho, int Assets)> topFires)
    {
        var slotLabel = slot == "morning" ? "manhã" : "noite";
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"📋 Ponto de situação — {slotLabel} de {dateLabel}\r\n\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"🔥 Incêndios ativos: {activeFires}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"🚨 Em escalada: {escalating}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"👩‍🚒 Operacionais: {man}  🚒 Veículos: {terrain}  🚁 Meios aéreos: {aerial}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"⚠️ Avisos nas últimas 12 h: {warnings12h}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"🌳 Área ardida no ano: {burnAreaHaYear} ha\r\n");
        if (topFires.Count > 0)
        {
            sb.Append("\r\nMaiores ocorrências:\r\n");
            foreach (var (concelho, assets) in topFires)
                sb.Append(CultureInfo.InvariantCulture, $" - {concelho}: {assets} meios\r\n");
        }
        return sb.ToString().TrimEnd();
    }

    // ── Builders / helpers ────────────────────────────────────────────────────────────

    private Incident MakeIncident(
        string id, string townName, double lat, double lng, DateTimeOffset occurredAt,
        int status, IncidentKind kind, string natureza, Resources resources, bool active, bool important = false)
    {
        if (!_byName.TryGetValue(Normalize(townName), out var loc))
            throw new InvalidOperationException($"Concelho '{townName}' not found in locations table.");

        // small deterministic jitter so co-located points don't perfectly overlap
        var jLat = lat + (_rng.NextDouble() - 0.5) * 0.01;
        var jLng = lng + (_rng.NextDouble() - 0.5) * 0.01;

        return new Incident
        {
            Id = id,
            OccurredAt = occurredAt,
            CreatedAt = occurredAt,
            UpdatedAt = _now,
            Location = townName,
            DetailLocation = $"Zona rural de {townName}",
            District = loc.District,
            Concelho = townName,
            Freguesia = townName,
            Dico = loc.Dico,
            Coordinates = GeoPoint.FromLatLng(jLat, jLng),
            Status = IncidentStatusCatalog.FromCode(status),
            Kind = kind,
            NaturezaCode = natureza,
            Natureza = NaturezaLabel(natureza),
            Resources = resources,
            Active = active,
            Important = important,
        };
    }

    private static Resources Res(int man, int terrain, int aerial) =>
        new() { Man = man, Terrain = terrain, Aerial = aerial, ManGround = man, ManAerial = aerial, Entities = Math.Max(1, terrain / 3) };

    private IncidentStatusChange Change(string incidentId, DateTimeOffset at, int code) =>
        new() { IncidentId = incidentId, At = at, Code = code, Label = IncidentStatusCatalog.FromCode(code).Label };

    /// <summary>Despacho(3) → Chegada(6) → Em Resolução(7) → Conclusão(8), with the given gaps in minutes.</summary>
    private IEnumerable<IncidentStatusChange> FullClosedHistory(string id, DateTimeOffset t0, int dispatchMin, int controlMin, int conclusionMin)
    {
        var arrival = t0.AddMinutes(dispatchMin);
        var control = arrival.AddMinutes(controlMin);
        var conclusion = control.AddMinutes(conclusionMin);
        return
        [
            Change(id, t0, 3),
            Change(id, arrival, 6),
            Change(id, control, 7),
            Change(id, conclusion, 8),
        ];
    }

    private WeatherObservation Obs(int stationId, double temp, double hum, double wind, string dir) => new()
    {
        StationId = stationId,
        At = _now.AddMinutes(-15),
        Temperature = temp,
        Humidity = hum,
        WindSpeedKmh = wind,
        WindDirection = dir,
        PrecipitationMm = 0,
        Pressure = 1008,
        Radiation = 820,
    };

    private WeatherWarning NewWeatherWarning(string areaCode, string level, DateTimeOffset endsAt) => new()
    {
        AreaCode = areaCode,
        AwarenessType = "Tempo Quente",
        Level = level,
        StartsAt = _now.AddHours(-3),
        EndsAt = endsAt,
        Text = "Tempo quente com valores elevados da temperatura máxima e mínima.",
        Control = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{areaCode}:{level}:{endsAt:O}"))).ToLowerInvariant()[..24],
        CreatedAt = _now.AddHours(-3),
    };

    private DateTimeOffset SpreadOverYear(int index, int total)
    {
        var yearStart = _clock.FromLisbon(new DateTime(_clock.LisbonToday.Year, 1, 1, 0, 0, 0));
        var span = _now - yearStart - TimeSpan.FromHours(6);
        var frac = total <= 1 ? 0.5 : (double)index / (total - 1);
        var jitterHours = (_rng.NextDouble() - 0.5) * 72;
        var at = yearStart + TimeSpan.FromTicks((long)(span.Ticks * frac)) + TimeSpan.FromHours(jitterHours);
        if (at < yearStart) at = yearStart.AddHours(1);
        if (at > _now) at = _now.AddHours(-1);
        return at;
    }

    // Jan..Dec ignition weights — a summer-heavy Portuguese fire season peaking in Jul/Aug.
    private static readonly int[] MonthWeights = [1, 1, 2, 3, 6, 12, 20, 22, 14, 6, 2, 1];

    /// <summary>A random Lisbon instant in <paramref name="year"/>, biased toward the summer fire season.</summary>
    private DateTimeOffset SeasonalDay(int year)
    {
        var total = MonthWeights.Sum();
        var pick = _rng.Next(0, total);
        var month = 12;
        var acc = 0;
        for (var m = 0; m < 12; m++)
        {
            acc += MonthWeights[m];
            if (pick < acc) { month = m + 1; break; }
        }
        var day = 1 + _rng.Next(0, DateTime.DaysInMonth(year, month));
        return _clock.FromLisbon(new DateTime(year, month, day, _rng.Next(0, 24), _rng.Next(0, 60), 0));
    }

    /// <summary>A random Lisbon instant in Mar–May of <paramref name="year"/> — always before the concelho-profile's mid-year window.</summary>
    private DateTimeOffset EarlySeasonDay(int year)
    {
        var month = 3 + _rng.Next(0, 3);
        var day = 1 + _rng.Next(0, DateTime.DaysInMonth(year, month));
        return _clock.FromLisbon(new DateTime(year, month, day, _rng.Next(0, 24), _rng.Next(0, 60), 0));
    }

    private List<HotspotSample> BuildHotspotSamples(GeoPoint center, int count, double spreadKm)
    {
        var samples = new List<HotspotSample>(count);
        for (var i = 0; i < count; i++)
        {
            var t = (double)i / count; // spreads outward over time
            var acquiredAt = _now.AddHours(-12 * (1 - t));
            var angle = _rng.NextDouble() * 2 * Math.PI;
            var distKm = t * spreadKm * (0.4 + _rng.NextDouble());
            var dLat = distKm / 111.0;
            var dLng = distKm / (111.0 * Math.Cos(center.Latitude * Math.PI / 180));
            samples.Add(new HotspotSample(
                GeoPoint.FromLatLng(center.Latitude + dLat * Math.Sin(angle), center.Longitude + dLng * Math.Cos(angle)),
                acquiredAt,
                Math.Round(300 + _rng.NextDouble() * 67, 1),
                _rng.NextDouble() > 0.6 ? "high" : "nominal"));
        }
        return samples;
    }

    private static string BuildPolygonKml(GeoPoint center, double radiusKm, string name)
    {
        var sb = new StringBuilder();
        var coords = new StringBuilder();
        const int points = 24;
        var dLat = radiusKm / 111.0;
        var dLng = radiusKm / (111.0 * Math.Cos(center.Latitude * Math.PI / 180));
        for (var i = 0; i <= points; i++)
        {
            var angle = 2 * Math.PI * i / points;
            var lat = center.Latitude + dLat * Math.Sin(angle);
            var lng = center.Longitude + dLng * Math.Cos(angle);
            coords.Append(CultureInfo.InvariantCulture, $"{lng:0.#####},{lat:0.#####},0 ");
        }
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>");
        sb.Append(CultureInfo.InvariantCulture, $"<name>{name}</name>");
        sb.Append("<Placemark><name>Perímetro</name><Polygon><outerBoundaryIs><LinearRing><coordinates>");
        sb.Append(coords.ToString().TrimEnd());
        sb.Append("</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark></Document></kml>");
        return sb.ToString();
    }

    private static string MinimalRiskGeoJson() =>
        "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{\"dico\":\"0601\",\"risk\":5},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[-8.1,40.1],[-8.0,40.1],[-8.0,40.3],[-8.1,40.3],[-8.1,40.1]]]}}]}";

    private static string NaturezaLabel(string code) => code switch
    {
        "3101" => "Incêndio em Povoamento Florestal",
        "3103" => "Incêndio em Mato",
        "3105" => "Incêndio Agrícola",
        "2103" => "Incêndio em Habitação",
        "3301" => "Inundação",
        _ => "Ocorrência",
    };

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private void Record(string label, long count) => _counts.Add((label, count));

    private static string? ResolveLocationsPath()
    {
        // Walk up from the CWD and the binary location looking for dev/seed/locations.json.
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "dev", "seed", "locations.json");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }
        return null;
    }

    // ── Options parsing (mirrors KeyCommands) ────────────────────────────────────────

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        return null;
    }

    // ── Summary + footer ──────────────────────────────────────────────────────────────

    private void PrintSummary(string dbName)
    {
        Console.WriteLine();
        Console.WriteLine($"Demo data seeded into MongoDB database '{dbName}'.");
        Console.WriteLine();
        Console.WriteLine($"  {"COLLECTION",-34} {"DOCS",8}");
        Console.WriteLine($"  {new string('─', 34),-34} {new string('─', 8),8}");
        long total = 0;
        foreach (var (label, count) in _counts)
        {
            Console.WriteLine($"  {label,-34} {count,8}");
            total += count;
        }
        Console.WriteLine($"  {new string('─', 34),-34} {new string('─', 8),8}");
        Console.WriteLine($"  {"TOTAL",-34} {total,8}");
        Console.WriteLine();
        Console.WriteLine("Skipped: incident_photos (needs object-storage binaries — nothing to seed without S3/MinIO objects).");
        Console.WriteLine();
        Console.WriteLine("── How to run against it ─────────────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine("Point Fogos.Api at the demo database (env vars — the API binds Mongo:Database from config):");
        Console.WriteLine($"  Mongo__Database={dbName}");
        Console.WriteLine("  Mongo__ConnectionString=mongodb://localhost:27017/?directConnection=true");
        Console.WriteLine("  Redis__ConnectionString=localhost:6379     # GraphQL needs Redis; REST/feeds do not");
        Console.WriteLine("  ASPNETCORE_URLS=http://localhost:5079       # spare port — leave your running API on :5077 alone");
        Console.WriteLine();
        Console.WriteLine("  # one-liner (--no-launch-profile so the :5077 launch profile does not override the port):");
        Console.WriteLine("  ASPNETCORE_ENVIRONMENT=Development \\");
        Console.WriteLine("  Mongo__ConnectionString=mongodb://localhost:27017/?directConnection=true \\");
        Console.WriteLine($"  Mongo__Database={dbName} Redis__ConnectionString=localhost:6379 ASPNETCORE_URLS=http://localhost:5079 \\");
        Console.WriteLine("    dotnet run --no-launch-profile --project src/Fogos.Api");
        Console.WriteLine();
        Console.WriteLine("Point fogos-frontend at that API (fogos-frontend/.env.local):");
        Console.WriteLine("  FOGOS_API_URL=http://localhost:5079");
        Console.WriteLine();
        Console.WriteLine("The Worker jobs are NOT needed to browse — all data is pre-seeded and static.");
    }
}
