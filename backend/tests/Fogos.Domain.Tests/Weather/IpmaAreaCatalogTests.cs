using Fogos.Domain.Weather;

namespace Fogos.Domain.Tests.Weather;

public class IpmaAreaCatalogTests
{
    [Theory]
    [InlineData("AVR", "Aveiro")]
    [InlineData("BJA", "Beja")]
    [InlineData("BGC", "Bragança")]
    [InlineData("BRG", "Braga")]
    [InlineData("CBR", "Coimbra")]
    [InlineData("CTB", "Castelo Branco")]
    [InlineData("EVR", "Évora")]
    [InlineData("FAR", "Faro")]
    [InlineData("GDA", "Guarda")]
    [InlineData("LRA", "Leiria")]
    [InlineData("LSB", "Lisboa")]
    [InlineData("PTG", "Portalegre")]
    [InlineData("PTO", "Porto")]
    [InlineData("STR", "Santarém")]
    [InlineData("STB", "Setúbal")]
    [InlineData("VCT", "Viana do Castelo")]
    [InlineData("VRL", "Vila Real")]
    [InlineData("VIS", "Viseu")]
    public void Covers_the_18_mainland_districts(string area, string district)
    {
        Assert.Equal(district, IpmaAreaCatalog.District(area));
        Assert.Contains(area, IpmaAreaCatalog.AreaCodesForDistrict(district));
    }

    [Fact]
    public void Unknown_area_and_district_return_null_and_empty()
    {
        Assert.Null(IpmaAreaCatalog.District("ZZZ"));
        Assert.Empty(IpmaAreaCatalog.AreaCodesForDistrict("Atlantis"));
    }

    [Theory]
    [InlineData("Viana do Castelo")]
    [InlineData("Viana Do Castelo")]
    [InlineData("VIANA DO CASTELO")]
    [InlineData("viana do castelo")]
    public void District_lookup_is_case_and_accent_insensitive(string district)
    {
        Assert.Contains("VCT", IpmaAreaCatalog.AreaCodesForDistrict(district));
    }

    [Fact]
    public void Evora_matches_regardless_of_accent()
    {
        Assert.Contains("EVR", IpmaAreaCatalog.AreaCodesForDistrict("Evora"));
        Assert.Contains("EVR", IpmaAreaCatalog.AreaCodesForDistrict("Évora"));
    }

    [Fact]
    public void Madeira_groups_its_four_areas_under_one_district()
    {
        Assert.Equal("Madeira", IpmaAreaCatalog.District("MCN"));
        var codes = IpmaAreaCatalog.AreaCodesForDistrict("Madeira");
        Assert.Equal(4, codes.Count);
        Assert.Contains("PSA", codes);
    }
}
