using System.Security.Cryptography;
using System.Text;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Incidents;

/// <summary>
/// The single write path for KML perimeter versioning. Every place that sets <c>Incident.Kml</c> or
/// <c>Incident.KmlVost</c> calls <see cref="AppendIfChangedAsync"/>: a new <c>incident_kml_versions</c>
/// row is appended only when the KML's SHA-256 differs from the latest stored version for that
/// (incident, slot) pair. The inline latest-wins fields are owned by the callers and left untouched.
/// </summary>
public sealed class KmlVersionStore(MongoContext mongo, IClock clock)
{
    /// <summary>
    /// Appends a version for <paramref name="incidentId"/> / <paramref name="vost"/> when
    /// <paramref name="kml"/> is non-empty and its hash differs from the latest stored version.
    /// Returns the appended version, or null when nothing changed (dedup) or the payload was empty.
    /// </summary>
    public async Task<IncidentKmlVersion?> AppendIfChangedAsync(
        string incidentId, bool vost, string? kml, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(kml))
            return null;

        var sha = Sha256Hex(kml);

        var latest = await mongo.IncidentKmlVersions
            .Find(Builders<IncidentKmlVersion>.Filter.Eq(x => x.IncidentId, incidentId)
                  & Builders<IncidentKmlVersion>.Filter.Eq(x => x.Vost, vost))
            .Sort(Builders<IncidentKmlVersion>.Sort.Descending(x => x.CapturedAt))
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        if (latest is not null && latest.Sha256 == sha)
            return null; // identical perimeter already stored — dedup.

        var version = new IncidentKmlVersion
        {
            IncidentId = incidentId,
            Vost = vost,
            Kml = kml,
            Sha256 = sha,
            SizeBytes = Encoding.UTF8.GetByteCount(kml),
            CapturedAt = clock.UtcNow,
        };
        await mongo.IncidentKmlVersions.InsertOneAsync(version, cancellationToken: ct);
        return version;
    }

    private static string Sha256Hex(string kml) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(kml)));
}
