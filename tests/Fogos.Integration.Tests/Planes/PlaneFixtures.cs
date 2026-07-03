namespace Fogos.Integration.Tests.Planes;

/// <summary>Provider response fixtures for the plane pollers, shaped like the real APIs.</summary>
internal static class PlaneFixtures
{
    public const string Hex1 = "4ca7b1";
    public const string Reg1 = "CS-ABC";
    public const string Hex2 = "4ca7b2";
    public const string Reg2 = "CS-DEF";

    /// <summary>FR24 <c>live/flight-positions/light</c> response, two tracked aircraft.</summary>
    public const string Fr24TwoAircraft = """
    {
      "data": [
        { "fr24_id": "2ef1a01", "hex": "4CA7B1", "reg": "CS-ABC", "type": "AT8T", "lat": 40.12, "lon": -8.21, "alt": 5200, "gspeed": 180, "track": 270, "timestamp": "2026-06-15T11:59:00Z" },
        { "fr24_id": "2ef1a02", "hex": "4CA7B2", "reg": "CS-DEF", "type": "EC45", "lat": 41.03, "lon": -7.55, "alt": 3100, "gspeed": 120, "track": 90,  "timestamp": "2026-06-15T11:59:00Z" }
      ]
    }
    """;

    /// <summary>adsb.fi / airplanes.live <c>/hex/{list}</c> response. Aircraft 2 nests lastPosition; a third is stale.</summary>
    public const string AdsbTwoAircraftPlusStale = """
    {
      "ac": [
        { "hex": "4ca7b1", "r": "CS-ABC", "t": "AT8T", "lat": 40.30, "lon": -8.40, "alt_baro": 4800, "gs": 175, "seen_pos": 8 },
        { "hex": "4ca7b2", "r": "CS-DEF", "t": "EC45", "alt_baro": "ground", "seen_pos": 20, "lastPosition": { "lat": 41.10, "lon": -7.60, "seen_pos": 20 } },
        { "hex": "4ca7b9", "r": "CS-STALE", "t": "AT8T", "lat": 39.00, "lon": -9.00, "alt_baro": 6000, "seen_pos": 720 }
      ]
    }
    """;
}
