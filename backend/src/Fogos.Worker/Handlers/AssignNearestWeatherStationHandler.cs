using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Weather;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Queue;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Ports <c>AssignNearestWeatherStation</c>: on a new incident with coordinates, finds the nearest
/// station via a Mongo <c>$near</c> query on the <c>weather_stations</c> 2dsphere index and stores its
/// id on the incident. Re-fetches the incident first (lost-update protection), like the legacy job.
/// </summary>
public sealed class AssignNearestWeatherStationHandler(MongoContext mongo) : IEventHandler<IncidentCreated>
{
    public async Task HandleAsync(IncidentCreated evt, CancellationToken ct)
    {
        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, evt.IncidentId))
            .FirstOrDefaultAsync(ct);

        if (incident?.Coordinates is not { } point)
            return;

        var nearFilter = new BsonDocument("coordinates", new BsonDocument("$near", new BsonDocument
        {
            ["$geometry"] = new BsonDocument
            {
                ["type"] = "Point",
                ["coordinates"] = new BsonArray { point.Longitude, point.Latitude },
            },
        }));

        var nearest = await mongo.WeatherStations
            .Find(new BsonDocumentFilterDefinition<WeatherStation>(nearFilter))
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        if (nearest is null)
            return;

        await mongo.Incidents.UpdateOneAsync(
            Builders<Incident>.Filter.Eq(x => x.Id, incident.Id),
            Builders<Incident>.Update.Set(x => x.NearestWeatherStationId, nearest.Id),
            cancellationToken: ct);
    }
}
