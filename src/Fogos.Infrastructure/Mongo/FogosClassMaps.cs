using Fogos.Domain.Aircraft;
using Fogos.Domain.Auth;
using Fogos.Domain.Geo;
using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Domain.Locations;
using Fogos.Domain.Photos;
using Fogos.Domain.Risk;
using Fogos.Domain.Social;
using Fogos.Domain.Stats;
using Fogos.Domain.Warnings;
using Fogos.Domain.Weather;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Bson.Serialization.Serializers;

namespace Fogos.Infrastructure.Mongo;

/// <summary>
/// One-time, idempotent, thread-safe registration of BSON conventions, custom serializers,
/// and per-entity class maps. Clean schema: camelCase elements, ignore nulls, ignore extra
/// elements, enums as strings, GeoJSON points, business keys as <c>_id</c> where natural.
/// </summary>
public static class FogosClassMaps
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;

        lock (Gate)
        {
            if (_registered)
                return;

            RegisterConventions();
            RegisterSerializers();
            RegisterClassMaps();

            _registered = true;
        }
    }

    private static void RegisterConventions()
    {
        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreExtraElementsConvention(true),
            new IgnoreIfNullConvention(true),
            new EnumRepresentationConvention(BsonType.String),
        };
        ConventionRegistry.Register("fogos", pack, _ => true);
    }

    private static void RegisterSerializers()
    {
        BsonSerializer.TryRegisterSerializer(new DateTimeOffsetToUtcSerializer());
        BsonSerializer.TryRegisterSerializer(new DateOnlyToUtcSerializer());
        BsonSerializer.TryRegisterSerializer(new GeoPointSerializer());
    }

    private static void RegisterClassMaps()
    {
        // ── Incidents ─────────────────────────────────────────────────────────
        // Business id (numero_sado) — plain string _id, no ObjectId duality.
        BsonClassMap.RegisterClassMap<Incident>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(c => c.Id);
        });

        BsonClassMap.RegisterClassMap<IncidentHistorySnapshot>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<IncidentStatusChange>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<IncidentPhoto>(cm => MapObjectId(cm, c => c.Id));

        // Per-incident thread state keyed by incident id.
        BsonClassMap.RegisterClassMap<SocialThread>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(c => c.IncidentId);
        });

        // ── Weather ───────────────────────────────────────────────────────────
        BsonClassMap.RegisterClassMap<WeatherStation>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(c => c.Id); // IPMA stationId (int) as _id
        });
        BsonClassMap.RegisterClassMap<WeatherObservation>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<DailyWeather>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<WeatherNormal>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<TemperatureWave>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<WeatherWarning>(cm => MapObjectId(cm, c => c.Id));

        // ── Risk ──────────────────────────────────────────────────────────────
        BsonClassMap.RegisterClassMap<ConcelhoRisk>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<RiskGeoJson>(cm => MapObjectId(cm, c => c.Id));

        // ── Warnings / Stats ────────────────────────────────────────────────────
        BsonClassMap.RegisterClassMap<Warning>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<HistoryTotal>(cm => MapObjectId(cm, c => c.Id));

        // ── Aircraft ────────────────────────────────────────────────────────────
        BsonClassMap.RegisterClassMap<FlightPosition>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<TrackedAircraft>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(c => c.Icao); // ICAO hex as _id
        });

        // ── Hotspots / Locations / Auth ─────────────────────────────────────────
        BsonClassMap.RegisterClassMap<Hotspots>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(c => c.IncidentId);
        });
        BsonClassMap.RegisterClassMap<Location>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<ApiClient>(cm => MapObjectId(cm, c => c.Id));
    }

    /// <summary>AutoMap and bind a surrogate string <c>_id</c> to an ObjectId (generated on insert).</summary>
    private static void MapObjectId<T>(BsonClassMap<T> cm, System.Linq.Expressions.Expression<Func<T, string>> idMember)
    {
        cm.AutoMap();
        cm.MapIdMember(idMember)
          .SetIdGenerator(StringObjectIdGenerator.Instance)
          .SetSerializer(new StringSerializer(BsonType.ObjectId));
    }
}
