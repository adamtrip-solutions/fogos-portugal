using Fogos.Domain.Time;
using Fogos.Domain.Warnings;
using Fogos.Importer.Mapping.Mappers;

namespace Fogos.Importer.Mapping;

/// <summary>
/// The catalogue of per-legacy-collection mappers. "Default" collections are everything ported;
/// the dead collections (pplanes / warningMadeira / users) are present as skip-only mappers and
/// are processed solely when explicitly requested via <c>--collections</c>.
/// </summary>
public sealed class MapperRegistry
{
    private readonly Dictionary<string, ILegacyCollectionMapper> _mappers;
    private readonly HashSet<string> _deadCollections;

    public MapperRegistry(IClock clock)
    {
        var mappers = new List<ILegacyCollectionMapper>
        {
            new IncidentMapper(clock),
            new IncidentHistoryMapper(clock),
            new StatusHistoryMapper(clock),
            new IncidentPhotoMapper(clock),
            new HotspotMapper(clock),

            new WeatherStationMapper(),
            new WeatherHourlyMapper(clock),
            new WeatherDailyMapper(clock),
            new WeatherNormalMapper(),
            new TemperatureWaveMapper(clock),
            new WeatherWarningMapper(clock),

            new RcmDailyMapper(clock),
            new RcmGeoJsonMapper(clock),

            new WarningMapper("warning", WarningKind.Manual, clock),
            new WarningMapper("warning_agif", WarningKind.Agif, clock),
            new WarningMapper("warningSite", WarningKind.Site, clock),

            new FlightPositionMapper(clock),
            new TrackedAircraftMapper(),

            new LocationMapper(),
            new HistoryTotalMapper(clock),
        };

        var dead = new List<ILegacyCollectionMapper>
        {
            new SkipMapper("pplanes", "legacy ADSB Exchange raw docs, superseded by flight_positions"),
            new SkipMapper("warningMadeira", "Madeira warnings not ported"),
            new SkipMapper("users", "auth stub, effectively unused"),
        };

        _mappers = mappers.Concat(dead).ToDictionary(m => m.Name, StringComparer.Ordinal);
        _deadCollections = dead.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>All collections imported by default (dead ones excluded).</summary>
    public IReadOnlyList<string> DefaultCollections =>
        _mappers.Keys.Where(k => !_deadCollections.Contains(k)).ToList();

    public bool TryGet(string collection, out ILegacyCollectionMapper mapper) =>
        _mappers.TryGetValue(collection, out mapper!);

    public IReadOnlyCollection<string> KnownCollections => _mappers.Keys;
}
