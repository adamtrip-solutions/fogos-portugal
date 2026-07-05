using Fogos.Domain.Incidents;

namespace Fogos.Domain.Tests;

public class IncidentStatusCatalogTests
{
    [Theory]
    [InlineData("Despacho de 1.º Alerta", 4)]
    [InlineData("Despacho de 1º Alerta", 4)]
    [InlineData("  DESPACHO DE 1º ALERTA", 4)]
    [InlineData(" Encerrada", 10)]
    [InlineData("Em Conclusão", 8)]
    [InlineData("Conclusão", 8)]
    [InlineData("Em Curso", 5)]
    [InlineData("Chegada ao TO", 6)]
    [InlineData("Em Resolução", 7)]
    [InlineData("Vigilância", 9)]
    [InlineData("Despacho", 3)]
    public void TryNormalize_maps_every_legacy_variant(string rawLabel, int expectedCode)
    {
        Assert.True(IncidentStatusCatalog.TryNormalize(rawLabel, out var status));
        Assert.Equal(expectedCode, status.Code);
    }

    [Theory]
    [InlineData("Não é um estado")]
    [InlineData("")]
    public void TryNormalize_returns_false_for_unknown(string rawLabel)
    {
        Assert.False(IncidentStatusCatalog.TryNormalize(rawLabel, out var status));
        Assert.Null(status);
    }

    [Theory]
    [InlineData(3, "FF6E02")]
    [InlineData(4, "FF6E02")]
    [InlineData(5, "B81E1F")]
    [InlineData(6, "B81E1F")]
    [InlineData(7, "6ABF59")]
    [InlineData(9, "6ABF59")]
    [InlineData(10, "6ABF59")]
    [InlineData(8, "BDBDBD")]
    [InlineData(11, "BDBDBD")]
    [InlineData(12, "BDBDBD")]
    public void ColorFor_matches_legacy_palette(int code, string expectedColor)
    {
        Assert.Equal(expectedColor, IncidentStatusCatalog.ColorFor(code));
        Assert.Equal(expectedColor, IncidentStatusCatalog.FromCode(code).Color);
    }

    [Fact]
    public void ActiveCodes_are_three_to_six()
    {
        Assert.Equal(new HashSet<int> { 3, 4, 5, 6 }, IncidentStatusCatalog.ActiveCodes);
    }

    [Fact]
    public void ImportantCheckCodes_are_one_to_six()
    {
        Assert.Equal(new HashSet<int> { 1, 2, 3, 4, 5, 6 }, IncidentStatusCatalog.ImportantCheckCodes);
    }

    [Fact]
    public void FromCode_4_label_has_no_dot()
    {
        Assert.Equal("Despacho de 1º Alerta", IncidentStatusCatalog.FromCode(4).Label);
    }

    [Fact]
    public void Feed_drop_terminal_13_is_labelled_green_and_inactive()
    {
        var status = IncidentStatusCatalog.FromCode(IncidentStatusCatalog.EncerradaSemAtualizacao);
        Assert.Equal(13, status.Code);
        Assert.Equal("Encerrada (sem atualização)", status.Label);
        Assert.Equal("6ABF59", status.Color); // same green family as Encerrada
        Assert.False(IncidentStatusCatalog.IsActive(13));
        Assert.DoesNotContain(13, IncidentStatusCatalog.ActiveCodes);
        Assert.Contains(13, IncidentStatusCatalog.InactiveCodes);
    }
}
