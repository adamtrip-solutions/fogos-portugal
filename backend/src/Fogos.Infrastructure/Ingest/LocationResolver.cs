using System.Collections.Concurrent;
using System.Globalization;
using Fogos.Domain.Locations;
using Fogos.Infrastructure.Geo;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Ingest;

/// <summary>
/// Resolved administrative location: title-cased names + zero-padded 4-char DICO. <see cref="Inferred"/> is
/// true when the district/concelho/DICO were not taken from the authoritative <c>locations</c> table but
/// derived from the incident's coordinates (polygon fallback) or the "Desconhecido"/"0000" last resort.
/// </summary>
public sealed record LocationInfo(string District, string Concelho, string? Freguesia, string Dico, bool Inferred = false);

/// <summary>
/// Ports <c>getLocationData</c> + the casing quirks from ProcessOcorrenciasSite / ProcessANPCAllDataV2, then
/// layers a coordinate fallback on top so an incident is <b>never</b> dropped over location resolution
/// ("most information, not most correct"). Resolution order:
/// <list type="number">
///   <item>Spain override (DICO "00").</item>
///   <item>ICNF pre-resolved district + DICO (INE) used directly.</item>
///   <item>Concelho name → <c>locations</c> level-2 row → DICO → distrito via level-1. Authoritative
///         (<see cref="LocationInfo.Inferred"/> = false).</item>
///   <item>Coordinate fallback: point-in-polygon over the concelho set (<see cref="ConcelhoLocator"/>). On a
///         hit, return the polygon's identity (Inferred = true) AND self-heal by upserting an alias row into
///         <c>locations</c> so the next sweep resolves this name via step 3.</item>
///   <item>Last resort: no name match and no polygon hit → the "Desconhecido"/"0000" sentinel (Inferred =
///         true). Never returns null.</item>
/// </list>
/// The signature stays nullable and the caller keeps its null-check for safety, but no path returns null now.
/// Ops notices for steps 4/5 are deduplicated per feed-concelho-name for the process lifetime so an empty
/// (unseeded) table does not spam one ping per incident on every 5-minute sweep.
/// </summary>
public sealed class LocationResolver(MongoContext mongo, IOpsNotifier ops, ConcelhoLocator locator)
{
    /// <summary>Concelho names already announced as inferred-from-coordinates (dedup across the process lifetime).</summary>
    private static readonly ConcurrentDictionary<string, byte> AnnouncedInferences = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Concelho names already announced as unresolved (the "Desconhecido" sentinel), deduped likewise.</summary>
    private static readonly ConcurrentDictionary<string, byte> AnnouncedUnknowns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Legacy UTF8::ucwords(mb_strtolower(x)): lower-case, then capitalize every word (accents kept).</summary>
    public static string Title(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().ToLowerInvariant());
    }

    public async Task<LocationInfo?> ResolveAsync(RawIncident raw, CancellationToken ct = default)
    {
        var freguesia = raw.Freguesia is null ? null : Title(raw.Freguesia);

        if (raw.SpainOverride)
            return new LocationInfo("Espanha", "Espanha", "Espanha", "00");

        // ICNF already carries INE (dico) + distrito — use them directly.
        if (raw.PreResolvedDico is not null && raw.PreResolvedDistrict is not null)
            return new LocationInfo(Title(raw.PreResolvedDistrict), Title(raw.Concelho), freguesia, Pad(raw.PreResolvedDico));

        var concelho = await mongo.Locations
            .Find(Builders<Location>.Filter.Eq(x => x.Level, LocationLevel.Concelho) & Builders<Location>.Filter.Eq(x => x.Name, raw.Concelho))
            .FirstOrDefaultAsync(ct);

        if (concelho is not null)
        {
            var districtCode = DeriveDistrictCode(concelho.Code);
            var distrito = await mongo.Locations
                .Find(Builders<Location>.Filter.Eq(x => x.Level, LocationLevel.Distrito) & Builders<Location>.Filter.Eq(x => x.Code, districtCode))
                .FirstOrDefaultAsync(ct);

            // A resolvable concelho whose distrito is missing is a genuine data gap — fall through to the
            // coordinate fallback rather than dropping the incident.
            if (distrito is not null)
            {
                var dico = Pad(string.IsNullOrEmpty(concelho.Dico) ? concelho.Code : concelho.Dico);
                return new LocationInfo(Title(distrito.Name), Title(raw.Concelho), freguesia, dico);
            }
        }

        // ── Step 4: coordinate fallback ─────────────────────────────────────────────────────────
        if (TryUsableCoords(raw, out var lat, out var lng) && locator.Locate(lat, lng) is { } match)
        {
            var dico = Pad(match.Dico);
            await SelfHealAliasAsync(raw.Concelho, dico, ct);

            if (AnnouncedInferences.TryAdd(raw.Concelho, 0))
                await ops.InfoAsync(
                    $"Concelho inferred from coordinates => {raw.Concelho} → {Title(match.Concelho)} ({dico}) => {raw.Id}", ct);

            return new LocationInfo(Title(match.Distrito), Title(match.Concelho), freguesia, dico, Inferred: true);
        }

        // ── Step 5: last resort — never drop the incident ───────────────────────────────────────
        if (AnnouncedUnknowns.TryAdd(raw.Concelho, 0))
            await ops.InfoAsync($"Location unresolved (no name match, no polygon hit) => {raw.Concelho} => {raw.Id}", ct);

        var concelhoName = string.IsNullOrWhiteSpace(raw.Concelho) ? "Desconhecido" : Title(raw.Concelho);
        return new LocationInfo("Desconhecido", concelhoName, freguesia, "0000", Inferred: true);
    }

    /// <summary>
    /// Upserts an alias row so the next sweep resolves this feed name via the fast name path (step 3). The
    /// alias <c>Code</c> is the padded 4-char DICO: <see cref="DeriveDistrictCode"/> then reads its first two
    /// digits as the district code, matching the seeded convention. Upsert (not insert) is race-safe across
    /// concurrent resolves of the same name; <c>Inferred = true</c> marks it as self-healed, not seeded.
    /// </summary>
    private async Task SelfHealAliasAsync(string feedConcelho, string dico, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(feedConcelho))
            return;

        var filter = Builders<Location>.Filter.Eq(x => x.Level, LocationLevel.Concelho)
                     & Builders<Location>.Filter.Eq(x => x.Name, feedConcelho);
        // Level and Name come from the filter's equality on insert — setting them again would collide.
        var update = Builders<Location>.Update
            .Set(x => x.Code, dico)
            .Set(x => x.Dico, dico)
            .Set(x => x.Inferred, true);

        await mongo.Locations.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, ct);
    }

    /// <summary>
    /// True when the raw coordinates are a usable fix: both present, not the (0,0) "no fix" placeholder, and in
    /// range. Mirrors <c>IncidentMapper.ResolveCoordinates</c> so the fallback and the stored point agree.
    /// </summary>
    private static bool TryUsableCoords(RawIncident raw, out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        if (raw.Lat is not { } la || raw.Lng is not { } lo)
            return false;
        if (la is < -90 or > 90 || lo is < -180 or > 180)
            return false;
        if (la == 0 && lo == 0)
            return false;
        lat = la;
        lng = lo;
        return true;
    }

    /// <summary>Legacy: 3-digit concelho code → first digit distrito; otherwise first two digits. As a string.</summary>
    private static string DeriveDistrictCode(string concelhoCode) =>
        concelhoCode.Length == 3
            ? int.Parse(concelhoCode[..1], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : int.Parse(concelhoCode[..Math.Min(2, concelhoCode.Length)], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    /// <summary>Legacy pad: while a DICO is not 4 chars, prepend a single '0' (in practice one pad).</summary>
    private static string Pad(string dico)
    {
        while (dico.Length < 4)
            dico = "0" + dico;
        return dico;
    }
}
