using Fogos.Worker.Jobs.Planes;

namespace Fogos.Integration.Tests.Planes;

/// <summary>Pure parser behaviour — field mapping, coordinate fallbacks, staleness filter (no containers).</summary>
public sealed class PlanePositionParserTests
{
    [Fact]
    public void Fr24_parser_maps_two_rows_with_lowercased_hex_and_ids()
    {
        var samples = Fr24PositionParser.Parse(PlaneFixtures.Fr24TwoAircraft);

        Assert.Equal(2, samples.Count);
        var first = samples[0];
        Assert.Equal("4ca7b1", first.Icao); // lowercased
        Assert.Equal("CS-ABC", first.Registration);
        Assert.Equal(40.12, first.Latitude, 3);
        Assert.Equal(-8.21, first.Longitude, 3);
        Assert.Equal(5200, first.Altitude);
        Assert.Equal("2ef1a01", first.Fr24Id);
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 11, 59, 0, TimeSpan.Zero), first.SampledAt);
    }

    [Fact]
    public void Fr24_parser_accepts_epoch_timestamps_and_ignores_rows_without_coords()
    {
        const string json = """
        { "data": [
          { "hex": "ABC123", "reg": "CS-XYZ", "lat": 38.0, "lon": -9.0, "timestamp": 1750000000 },
          { "hex": "NOCOORD", "reg": "CS-NIL" }
        ] }
        """;

        var samples = Fr24PositionParser.Parse(json);

        Assert.Single(samples);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1750000000), samples[0].SampledAt);
    }

    [Fact]
    public void Adsb_parser_prefers_lastPosition_drops_stale_and_nulls_ground_altitude()
    {
        var now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var samples = AdsbPositionParser.Parse(PlaneFixtures.AdsbTwoAircraftPlusStale, now);

        // Third aircraft (seen_pos 720 > 600) is dropped.
        Assert.Equal(2, samples.Count);
        Assert.DoesNotContain(samples, s => s.Icao == "4ca7b9");

        var nested = samples.Single(s => s.Icao == "4ca7b2");
        Assert.Equal(41.10, nested.Latitude, 3); // taken from lastPosition
        Assert.Equal(-7.60, nested.Longitude, 3);
        Assert.Null(nested.Altitude); // "ground" → null
        Assert.Equal(now.AddSeconds(-20), nested.SampledAt); // now − seen_pos

        var direct = samples.Single(s => s.Icao == "4ca7b1");
        Assert.Equal(4800, direct.Altitude);
        Assert.Equal(now.AddSeconds(-8), direct.SampledAt);
    }
}
