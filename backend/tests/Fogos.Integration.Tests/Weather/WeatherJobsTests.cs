using Fogos.Domain.Weather;
using Fogos.Worker.Jobs.Weather;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Weather;

/// <summary>
/// End-to-end weather-job runs against Testcontainers Mongo/Redis with IPMA fixtures served from a
/// stub handler. Asserts the persisted documents: station GeoPoint + place, hourly/daily -99→null,
/// warning insert-once + STB dispatch, normals upsert, and wave creation with ongoing-flag reset.
/// </summary>
[Collection("fogos")]
public sealed class WeatherJobsTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Stations_job_upserts_with_correct_geopoint_and_place()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new WeatherJobHarness(fixture);
        var job = new UpdateWeatherStationsJob(h.Ipma, h.Mongo, h.Clock, h.Freshness, h.Ops, h.Locks,
            NullLogger<UpdateWeatherStationsJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        var stations = await h.Mongo.WeatherStations.Find(Builders<WeatherStation>.Filter.Empty).ToListAsync();
        Assert.Equal(2, stations.Count);

        var lisbon = stations.Single(s => s.Id == 1200535);
        Assert.Equal("Lisboa (Geofísico)", lisbon.Name);
        Assert.Equal("Portugal", lisbon.Place);
        Assert.Equal(38.7223, lisbon.Coordinates.Latitude, 4);
        Assert.Equal(-9.1393, lisbon.Coordinates.Longitude, 4);

        var funchal = stations.Single(s => s.Id == 522);
        Assert.Equal("Madeira", funchal.Place);
    }

    [SkippableFact]
    public async Task Hourly_job_maps_metrics_decodes_wind_and_nulls_minus99()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new WeatherJobHarness(fixture);
        var job = new UpdateWeatherDataJob(h.Ipma, h.Mongo, h.Freshness, h.Ops, h.Locks,
            NullLogger<UpdateWeatherDataJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        var rows = await h.Mongo.WeatherHourly.Find(Builders<WeatherObservation>.Filter.Empty).ToListAsync();
        // Station 522 payload was null → skipped; 1200535 and 999 remain.
        Assert.Equal(2, rows.Count);

        var lisbon = rows.Single(r => r.StationId == 1200535);
        Assert.Equal(31.0, lisbon.Temperature);
        Assert.Equal(40.0, lisbon.Humidity);
        Assert.Null(lisbon.Radiation);          // -99 → null
        Assert.Equal("N", lisbon.WindDirection); // idDireccVento 1 → N

        var allNull = rows.Single(r => r.StationId == 999);
        Assert.Null(allNull.Temperature);
        Assert.Null(allNull.WindDirection);      // idDireccVento -99 → null → undecoded
    }

    [SkippableFact]
    public async Task Daily_job_applies_minus99_fix()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new WeatherJobHarness(fixture);
        var job = new UpdateWeatherDataDailyJob(h.Ipma, h.Mongo, h.Freshness, h.Ops, h.Locks,
            NullLogger<UpdateWeatherDataDailyJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        var rows = await h.Mongo.WeatherDaily.Find(Builders<DailyWeather>.Filter.Empty).ToListAsync();
        Assert.Equal(2, rows.Count);

        var lisbon = rows.Single(r => r.StationId == 1200535);
        Assert.Equal(35.0, lisbon.TempMax);

        var funchal = rows.Single(r => r.StationId == 522);
        Assert.Null(funchal.TempMax);   // -99 → null (the deliberate daily-path fix)
        Assert.Equal(20.0, funchal.TempMin);
    }

    [SkippableFact]
    public async Task Warnings_job_inserts_nongreen_once_and_dedups_on_rerun()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new WeatherJobHarness(fixture);
        var job = new HandleWeatherWarningsJob(h.Ipma, h.Mongo, h.Clock, h.Freshness, h.Ops, h.Locks,
            NullLogger<HandleWeatherWarningsJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        var warnings = await h.Mongo.WeatherWarnings.Find(Builders<WeatherWarning>.Filter.Empty).ToListAsync();
        Assert.Equal(2, warnings.Count); // green LSB dropped; STB + AVR kept
        Assert.DoesNotContain(warnings, w => string.Equals(w.Level, "green", StringComparison.OrdinalIgnoreCase));

        var stb = warnings.Single(w => w.AreaCode == "STB");
        Assert.Equal("Tempo Quente", stb.AwarenessType);
        Assert.Equal("yellow", stb.Level);
        Assert.Contains("&#x00E7;", stb.Text); // %uXXXX → HTML entity, kept literal as legacy did

        // Rerun → dedup by control: no new inserts.
        await job.RunAsync(CancellationToken.None);
        Assert.Equal(2, await h.Mongo.WeatherWarnings.CountDocumentsAsync(Builders<WeatherWarning>.Filter.Empty));
    }

    [SkippableFact]
    public async Task Normals_import_upserts_both_periods()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new WeatherJobHarness(fixture);
        var job = new ImportWeatherNormalsJob(h.NormalsHttpFactory(), h.Mongo, h.Ops, h.Locks,
            NullLogger<ImportWeatherNormalsJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        var normals = await h.Mongo.WeatherNormals.Find(Builders<WeatherNormal>.Filter.Empty).ToListAsync();
        Assert.Equal(2, normals.Count); // 1991-2020 (heat) + 1971-2000 (cold)
        Assert.Contains(normals, n => n.Period == WeatherNormal.PeriodHeat);
        Assert.Contains(normals, n => n.Period == WeatherNormal.PeriodCold);

        var any = normals[0];
        Assert.Equal(12, any.TmaxMean.Length);
        Assert.Equal(28.9, any.TmaxMean[6]); // July MTX
    }

    [SkippableFact]
    public async Task Wave_detection_creates_ongoing_wave_and_resets_prior_flags()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new WeatherJobHarness(fixture);
        h.Clock.UtcNow = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var today = h.Clock.LisbonToday;
        const int stationId = 1200535;

        await h.Mongo.WeatherNormals.InsertOneAsync(new WeatherNormal
        {
            StationId = stationId,
            Period = WeatherNormal.PeriodHeat,
            TmaxMean = Enumerable.Repeat(25.0, 12).ToArray(),
            TminMean = Enumerable.Repeat(12.0, 12).ToArray(),
        });

        // Six consecutive days ending today, each +7 above the 25° normal.
        var daily = Enumerable.Range(0, 6)
            .Select(i => new DailyWeather { StationId = stationId, Date = today.AddDays(-5 + i), TempMax = 32.0, TempMin = 15.0 })
            .ToList();
        await h.Mongo.WeatherDaily.InsertManyAsync(daily);

        // A stale ongoing wave from an earlier episode that must be reset this run.
        await h.Mongo.TemperatureWaves.InsertOneAsync(new TemperatureWave
        {
            StationId = stationId,
            Type = WaveType.Heat,
            StartDate = today.AddDays(-60),
            EndDate = today.AddDays(-54),
            Ongoing = true,
        });

        var job = new DetectTemperatureWavesJob(h.Mongo, h.Clock, h.Freshness, h.Ops, h.Locks,
            NullLogger<DetectTemperatureWavesJob>.Instance);
        await job.RunAsync(CancellationToken.None);

        var waves = await h.Mongo.TemperatureWaves.Find(Builders<TemperatureWave>.Filter.Empty).ToListAsync();
        Assert.Equal(2, waves.Count);

        var current = waves.Single(w => w.StartDate == today.AddDays(-5));
        Assert.Equal(WaveType.Heat, current.Type);
        Assert.True(current.Ongoing);
        Assert.Equal(today, current.EndDate);
        Assert.Equal(6, current.Days.Count);

        var stale = waves.Single(w => w.StartDate == today.AddDays(-60));
        Assert.False(stale.Ongoing); // prior ongoing flag was reset

        Assert.NotEmpty(h.Ops.Infos); // ops Info on newly created wave
    }
}
