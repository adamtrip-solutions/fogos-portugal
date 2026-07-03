namespace Fogos.Infrastructure.Options;

/// <summary>Connection settings for the project's own MongoDB (replica set in dev for change streams).</summary>
public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "";

    public string Database { get; set; } = "fogos";

    /// <summary>TTL for raw <c>flight_positions</c> samples — noise after a season.</summary>
    public int FlightPositionTtlDays { get; set; } = 180;
}
