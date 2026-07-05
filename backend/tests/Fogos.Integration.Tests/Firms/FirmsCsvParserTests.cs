using Fogos.Worker.Jobs.Firms;

namespace Fogos.Integration.Tests.Firms;

/// <summary>Pure tests for the FIRMS CSV parser and bounding-box math.</summary>
public sealed class FirmsCsvParserTests
{
    // VIIRS uses bright_ti4 + letter confidence (l/n/h).
    private const string Viirs = """
        country_id,latitude,longitude,bright_ti4,scan,track,acq_date,acq_time,satellite,instrument,confidence,version,bright_ti5,frp,daynight
        PRT,40.1234,-8.5678,320.5,0.5,0.4,2026-07-04,1305,N,VIIRS,n,2.0NRT,290.1,12.3,D
        PRT,40.2000,-8.6000,331.0,0.5,0.4,2026-07-04,0007,N,VIIRS,h,2.0NRT,291.0,20.0,N
        """;

    // MODIS uses brightness + numeric confidence.
    private const string Modis = """
        country_id,latitude,longitude,brightness,scan,track,acq_date,acq_time,satellite,instrument,confidence,version,bright_t31,frp,daynight
        PRT,40.3000,-8.7000,315.2,1.0,1.0,2026-07-04,0930,Terra,MODIS,75,6.1NRT,295.0,8.4,D
        """;

    [Fact]
    public void Parse_viirs_reads_position_time_brightness_and_confidence()
    {
        var samples = FirmsCsvParser.Parse(Viirs);

        Assert.Equal(2, samples.Count);
        var first = samples[0];
        Assert.Equal(40.1234, first.Position.Latitude, 4);
        Assert.Equal(-8.5678, first.Position.Longitude, 4);
        Assert.Equal(320.5, first.Brightness);
        Assert.Equal("n", first.Confidence);
        Assert.Equal(new DateTimeOffset(2026, 7, 4, 13, 5, 0, TimeSpan.Zero), first.AcquiredAt);

        // acq_time "0007" → 00:07 UTC.
        Assert.Equal(new DateTimeOffset(2026, 7, 4, 0, 7, 0, TimeSpan.Zero), samples[1].AcquiredAt);
    }

    [Fact]
    public void Parse_modis_reads_brightness_column_and_numeric_confidence()
    {
        var samples = FirmsCsvParser.Parse(Modis);

        var s = Assert.Single(samples);
        Assert.Equal(315.2, s.Brightness);
        Assert.Equal("75", s.Confidence);
        Assert.Equal(new DateTimeOffset(2026, 7, 4, 9, 30, 0, TimeSpan.Zero), s.AcquiredAt);
    }

    [Fact]
    public void Parse_returns_empty_for_header_only_or_blank()
    {
        Assert.Empty(FirmsCsvParser.Parse("latitude,longitude,acq_date,acq_time\n"));
        Assert.Empty(FirmsCsvParser.Parse(""));
        Assert.Empty(FirmsCsvParser.Parse("   "));
    }

    [Fact]
    public void Bbox_is_a_tenth_degree_square_west_south_east_north()
    {
        Assert.Equal("-8.1,39.9,-7.9,40.1", FirmsBbox.Around(40.0, -8.0));
    }

    [Fact]
    public void Bbox_rounds_to_six_decimals()
    {
        // 40.1234567 - 0.10 = 40.0234567 → 40.023457 (rounded, away-from-zero).
        var bbox = FirmsBbox.Around(40.1234567, -8.7654321);
        Assert.Equal("-8.865432,40.023457,-8.665432,40.223457", bbox);
    }
}
