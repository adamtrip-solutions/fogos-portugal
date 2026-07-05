using Fogos.Domain.Weather;
using Fogos.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>Read queries for weather stations and observations (incl. batched latest-per-station).</summary>
public sealed class WeatherReads(MongoContext context)
{
    public async Task<IReadOnlyList<WeatherStation>> StationsAsync(string? place, CancellationToken ct = default)
    {
        var filter = Builders<WeatherStation>.Filter.Empty;
        if (!string.IsNullOrWhiteSpace(place))
        {
            var rx = new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(place.Trim()), "i");
            filter = Builders<WeatherStation>.Filter.Or(
                Builders<WeatherStation>.Filter.Regex(x => x.Name, rx),
                Builders<WeatherStation>.Filter.Regex(x => x.Place, rx));
        }
        return await context.WeatherStations.Find(filter).Sort(Builders<WeatherStation>.Sort.Ascending(x => x.Name)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DailyWeather>> DailyAsync(DateOnly date, CancellationToken ct = default) =>
        await context.WeatherDaily
            .Find(Builders<DailyWeather>.Filter.Eq(x => x.Date, date))
            .Sort(Builders<DailyWeather>.Sort.Ascending(x => x.StationId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TemperatureWave>> WavesAsync(bool ongoingOnly, CancellationToken ct = default)
    {
        var filter = ongoingOnly
            ? Builders<TemperatureWave>.Filter.Eq(x => x.Ongoing, true)
            : Builders<TemperatureWave>.Filter.Empty;
        return await context.TemperatureWaves
            .Find(filter)
            .Sort(Builders<TemperatureWave>.Sort.Descending(x => x.StartDate))
            .ToListAsync(ct);
    }

    /// <summary>IPMA awareness warnings for the given area codes still in force at <paramref name="now"/> (ends in the future).</summary>
    public async Task<IReadOnlyList<WeatherWarning>> WarningsByAreasEndingAfterAsync(
        IReadOnlyList<string> areaCodes, DateTimeOffset now, CancellationToken ct = default)
    {
        if (areaCodes.Count == 0)
            return [];
        var f = Builders<WeatherWarning>.Filter;
        return await context.WeatherWarnings
            .Find(f.In(x => x.AreaCode, areaCodes) & f.Gt(x => x.EndsAt, now))
            .Sort(Builders<WeatherWarning>.Sort.Descending(x => x.StartsAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<int, WeatherStation>> StationsByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        var items = await context.WeatherStations
            .Find(Builders<WeatherStation>.Filter.In(x => x.Id, ids))
            .ToListAsync(ct);
        return items.ToDictionary(x => x.Id);
    }

    /// <summary>Latest hourly observation per station via a single sort+group aggregation.</summary>
    public async Task<IReadOnlyDictionary<int, WeatherObservation>> LatestByStationsAsync(IReadOnlyList<int> stationIds, CancellationToken ct = default)
    {
        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument("stationId", new BsonDocument("$in", new BsonArray(stationIds)))),
            new("$sort", new BsonDocument("at", -1)),
            new("$group", new BsonDocument { { "_id", "$stationId" }, { "doc", new BsonDocument("$first", "$$ROOT") } }),
        };
        var rows = await context.WeatherHourly
            .Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
            .ToListAsync(ct);
        return rows
            .Select(r => BsonSerializer.Deserialize<WeatherObservation>(r["doc"].AsBsonDocument))
            .ToDictionary(o => o.StationId);
    }
}
