using Fogos.Worker.Jobs.Risk;

namespace Fogos.Integration.Tests.Risk;

/// <summary>Pure fixture tests for the brittle IPMA RCM page parser — the two failure modes are distinct.</summary>
public sealed class RcmPageParserTests
{
    // A realistic slice of the rcm.pt JSP page: the rcmF[] assignments the legacy regex targets.
    private const string Page = """
        <html><head><title>Risco de Incêndio</title></head><body>
        <script type="text/javascript">
        var rcmF = [];
        rcmF[0] = {"dataPrev":"2026-07-04","dataRun":"2026-07-04 09:00","fileDate":"20260704","local":{"0101":{"data":{"dico":"0101","rcm":3}},"1106":{"data":{"dico":"1106","rcm":5}},"1312":{"data":{"dico":"1312","rcm":1}}}};
        rcmF[1] = {"dataPrev":"2026-07-04","local":{"0101":{"data":{"rcm":4}},"1106":{"data":{"rcm":2}},"1312":{"data":{"rcm":1}}}};
        rcmF[2] = {"dataPrev":"2026-07-04","local":{"0101":{"data":{"rcm":2}},"1106":{"data":{"rcm":3}},"1312":{"data":{"rcm":1}}}};
        rcmF[3] = {"dataPrev":"2026-07-04","local":{"0101":{"data":{"rcm":1}},"1106":{"data":{"rcm":1}},"1312":{"data":{"rcm":1}}}};
        rcmF[4] = {"dataPrev":"2026-07-04","local":{"0101":{"data":{"rcm":1}},"1106":{"data":{"rcm":1}},"1312":{"data":{"rcm":1}}}};
        </script></body></html>
        """;

    [Fact]
    public void Parse_extracts_all_five_horizons_with_levels()
    {
        var result = RcmPageParser.Parse(Page);

        Assert.Equal(new DateOnly(2026, 7, 4), result.ForecastDate);
        Assert.NotNull(result.RunAt);
        Assert.Equal(5, result.Horizons.Count);

        // Today (rcmF[0]).
        Assert.Equal(3, result.Horizon(0)!.Level("0101"));
        Assert.Equal(5, result.Horizon(0)!.Level("1106"));
        Assert.Equal(1, result.Horizon(0)!.Level("1312"));

        // Tomorrow / after horizons pick up their own levels.
        Assert.Equal(4, result.Horizon(1)!.Level("0101"));
        Assert.Equal(2, result.Horizon(2)!.Level("0101"));
    }

    [Fact]
    public void Level_is_null_for_unknown_dico()
    {
        var result = RcmPageParser.Parse(Page);
        Assert.Null(result.Horizon(0)!.Level("9999"));
    }

    [Fact]
    public void Parse_throws_PageShapeChanged_when_assignments_absent()
    {
        const string moved = "<html><body>the page has been redesigned, no rcmF here</body></html>";
        var ex = Assert.Throws<RcmParseException>(() => RcmPageParser.Parse(moved));
        Assert.Equal(RcmParseFailure.PageShapeChanged, ex.Failure);
    }

    [Fact]
    public void Parse_throws_EmptyData_when_local_map_is_empty()
    {
        const string empty = """
            rcmF[0] = {"dataPrev":"2026-07-04","local":{}};
            rcmF[1] = {"dataPrev":"2026-07-04","local":{"0101":{"data":{"rcm":1}}}};
            rcmF[2] = {"dataPrev":"2026-07-04","local":{"0101":{"data":{"rcm":1}}}};
            """;
        var ex = Assert.Throws<RcmParseException>(() => RcmPageParser.Parse(empty));
        Assert.Equal(RcmParseFailure.EmptyData, ex.Failure);
    }

    [Fact]
    public void Parse_throws_PageShapeChanged_on_malformed_json()
    {
        const string broken = "rcmF[0] = {not valid json};";
        var ex = Assert.Throws<RcmParseException>(() => RcmPageParser.Parse(broken));
        Assert.Equal(RcmParseFailure.PageShapeChanged, ex.Failure);
    }
}
