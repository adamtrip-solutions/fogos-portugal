using System.Globalization;
using Fogos.Domain.Locations;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Ingest;

/// <summary>Resolved administrative location: title-cased names + zero-padded 4-char DICO.</summary>
public sealed record LocationInfo(string District, string Concelho, string? Freguesia, string Dico);

/// <summary>
/// Ports <c>getLocationData</c> + the casing quirks from ProcessOcorrenciasSite / ProcessANPCAllDataV2:
/// concelho name → <c>locations</c> level-2 row → DICO (concelho code, single-zero left-padded to 4)
/// → distrito via level-1 (code = first 1–2 digits of the concelho code). Spain is special-cased with
/// DICO "00"; ICNF pre-resolves district+DICO itself. Returns null when the concelho is unknown
/// (legacy returned null → the incident was skipped) after pinging ops.
/// </summary>
public sealed class LocationResolver(MongoContext mongo, IOpsNotifier ops)
{
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

        if (concelho is null)
        {
            await ops.ErrorAsync($"Concelho not found => {raw.Concelho} => {raw.Id}", ct);
            return null;
        }

        var districtCode = DeriveDistrictCode(concelho.Code);
        var distrito = await mongo.Locations
            .Find(Builders<Location>.Filter.Eq(x => x.Level, LocationLevel.Distrito) & Builders<Location>.Filter.Eq(x => x.Code, districtCode))
            .FirstOrDefaultAsync(ct);

        if (distrito is null)
        {
            await ops.ErrorAsync($"Distrito code not found => {districtCode}", ct);
            return null;
        }

        var dico = Pad(string.IsNullOrEmpty(concelho.Dico) ? concelho.Code : concelho.Dico);
        return new LocationInfo(Title(distrito.Name), Title(raw.Concelho), freguesia, dico);
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
