using System.Globalization;
using Fogos.Domain.Locations;
using Fogos.Infrastructure.Ingest;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>A resolved concelho: its DICO, title-cased name, and district name.</summary>
public sealed record ConcelhoLocation(string Dico, string Name, string District);

/// <summary>Read queries over the administrative <c>locations</c> table (concelho ⇄ district resolution).</summary>
public sealed class LocationReads(MongoContext context)
{
    /// <summary>
    /// Resolves a concelho by DICO to its name + district, or null when unknown. Mirrors the ingest
    /// district-code derivation (3-digit code → first digit; else first two digits).
    /// </summary>
    public async Task<ConcelhoLocation?> ByDicoAsync(string dico, CancellationToken ct = default)
    {
        var concelho = await context.Locations
            .Find(Builders<Location>.Filter.Eq(x => x.Level, LocationLevel.Concelho) & Builders<Location>.Filter.Eq(x => x.Dico, dico))
            .FirstOrDefaultAsync(ct);
        if (concelho is null)
            return null;

        var districtCode = DeriveDistrictCode(concelho.Code);
        var district = await context.Locations
            .Find(Builders<Location>.Filter.Eq(x => x.Level, LocationLevel.Distrito) & Builders<Location>.Filter.Eq(x => x.Code, districtCode))
            .FirstOrDefaultAsync(ct);

        return new ConcelhoLocation(dico, LocationResolver.Title(concelho.Name), LocationResolver.Title(district?.Name ?? ""));
    }

    /// <summary>Legacy: 3-digit concelho code → first digit distrito; otherwise the first two digits.</summary>
    private static string DeriveDistrictCode(string concelhoCode) =>
        concelhoCode.Length == 3
            ? int.Parse(concelhoCode[..1], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : int.Parse(concelhoCode[..Math.Min(2, concelhoCode.Length)], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
}
