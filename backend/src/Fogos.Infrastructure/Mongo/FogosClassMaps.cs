using Fogos.Domain.Aircraft;
using Fogos.Domain.Alerts;
using Fogos.Domain.Auth;
using Fogos.Domain.Geo;
using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Domain.Locations;
using Fogos.Domain.Photos;
using Fogos.Domain.Reports;
using Fogos.Domain.Risk;
using Fogos.Domain.Stats;
using Fogos.Domain.Users;
using Fogos.Domain.Warnings;
using Fogos.Domain.Weather;
using Fogos.Domain.Webhooks;
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
        // Operational means: legacy element names for the ANEPC "means" fields so imported
        // real data round-trips (the Laravel app persists these exact camelCase keys).
        BsonClassMap.RegisterClassMap<Resources>(cm =>
        {
            cm.AutoMap();
            cm.GetMemberMap(r => r.ManGround).SetElementName("operacionaisTerrestres");
            cm.GetMemberMap(r => r.ManAerial).SetElementName("operacionaisAereos");
            cm.GetMemberMap(r => r.Entities).SetElementName("quantEntidades");
        });

        // Derived signals embedded on the incident document (escalation / rekindle / critical conditions).
        BsonClassMap.RegisterClassMap<IncidentSignals>(cm => cm.AutoMap());

        // Business id (numero_sado) — plain string _id, no ObjectId duality.
        BsonClassMap.RegisterClassMap<Incident>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(c => c.Id);
        });

        BsonClassMap.RegisterClassMap<IncidentHistorySnapshot>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<IncidentStatusChange>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<IncidentPhoto>(cm => MapObjectId(cm, c => c.Id));

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
        BsonClassMap.RegisterClassMap<IncidentAircraftLink>(cm => MapObjectId(cm, c => c.Id));

        // Versioned KML perimeter snapshots.
        BsonClassMap.RegisterClassMap<IncidentKmlVersion>(cm => MapObjectId(cm, c => c.Id));

        // Ignition clusters (single-linkage groupings of recent fires).
        BsonClassMap.RegisterClassMap<IgnitionCluster>(cm => MapObjectId(cm, c => c.Id));

        // ── Alerts / Webhooks / Reports (WP4) ───────────────────────────────────
        BsonClassMap.RegisterClassMap<AlertSubscription>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<AlertEvent>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<WebhookEndpoint>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<SituationReport>(cm => MapObjectId(cm, c => c.Id));

        // ── Hotspots / Locations / Auth ─────────────────────────────────────────
        BsonClassMap.RegisterClassMap<Hotspots>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(c => c.IncidentId);
        });
        BsonClassMap.RegisterClassMap<Location>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<ApiClient>(cm => MapObjectId(cm, c => c.Id));
        BsonClassMap.RegisterClassMap<User>(cm => MapObjectId(cm, c => c.Id));
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
