using Fogos.Domain.Incidents;

namespace Fogos.Domain.Tests;

public class NaturezaCatalogTests
{
    [Fact]
    public void Arrays_have_the_expected_counts()
    {
        Assert.Equal(6, NaturezaCatalog.Fire.Count);
        Assert.Equal(15, NaturezaCatalog.UrbanFire.Count);
        Assert.Equal(4, NaturezaCatalog.TransportFire.Count);
        Assert.Equal(5, NaturezaCatalog.OtherFire.Count);
        Assert.Equal(12, NaturezaCatalog.Fma.Count);
    }

    [Theory]
    [InlineData("3101", IncidentKind.Fire)]
    [InlineData("4335", IncidentKind.Fire)]
    [InlineData("2101", IncidentKind.UrbanFire)]
    [InlineData("2301", IncidentKind.TransportFire)]
    [InlineData("3107", IncidentKind.OtherFire)]
    [InlineData("2419", IncidentKind.Fma)]
    [InlineData("9999", IncidentKind.Other)]
    public void Classify_maps_spot_codes(string code, IncidentKind expected)
    {
        Assert.Equal(expected, NaturezaCatalog.Classify(code));
    }

    [Fact]
    public void Every_code_in_each_array_classifies_to_its_kind()
    {
        Assert.All(NaturezaCatalog.Fire, c => Assert.Equal(IncidentKind.Fire, NaturezaCatalog.Classify(c)));
        Assert.All(NaturezaCatalog.UrbanFire, c => Assert.Equal(IncidentKind.UrbanFire, NaturezaCatalog.Classify(c)));
        Assert.All(NaturezaCatalog.TransportFire, c => Assert.Equal(IncidentKind.TransportFire, NaturezaCatalog.Classify(c)));
        Assert.All(NaturezaCatalog.OtherFire, c => Assert.Equal(IncidentKind.OtherFire, NaturezaCatalog.Classify(c)));
        Assert.All(NaturezaCatalog.Fma, c => Assert.Equal(IncidentKind.Fma, NaturezaCatalog.Classify(c)));
    }
}
