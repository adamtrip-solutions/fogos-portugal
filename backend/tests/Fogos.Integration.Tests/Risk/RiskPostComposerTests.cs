using Fogos.Worker.Jobs.Risk;

namespace Fogos.Integration.Tests.Risk;

/// <summary>Pure tests for the Portuguese risk-post composition.</summary>
public sealed class RiskPostComposerTests
{
    [Theory]
    [InlineData(1, "🟢", "Reduzido")]
    [InlineData(4, "🟠", "Muito Elevado")]
    [InlineData(5, "🔴", "Máximo")]
    public void ProjectRiskToday_composes_expected_pt_message(int level, string emoji, string label)
    {
        var message = RiskPostComposer.ProjectRiskToday(level);
        Assert.Equal($"Risco de incêndio para hoje: {emoji} {label}", message);
    }

    [Fact]
    public void ComposeRiskMap_lists_top_bands_and_appends_queimadas_warning()
    {
        var levels = new[]
        {
            ("Águeda", 5),
            ("Lisboa", 4),
            ("Porto", 2),   // below the reported bands
        };

        var text = RiskPostComposer.ComposeRiskMap(new DateOnly(2026, 7, 4), tomorrow: false, levels);

        Assert.Contains("04-07-2026 Risco de incêndio para hoje #FogosPT", text);
        Assert.Contains("🔴 Máximo: Águeda", text);
        Assert.Contains("🟠 Muito Elevado: Lisboa", text);
        Assert.DoesNotContain("Porto", text);
        Assert.Contains("PROIBIDO fazer Queimadas", text);
    }

    [Fact]
    public void ComposeRiskMap_tomorrow_uses_amanha_wording_and_reports_none_when_calm()
    {
        var levels = new[] { ("Porto", 2), ("Lisboa", 1) };
        var text = RiskPostComposer.ComposeRiskMap(new DateOnly(2026, 7, 4), tomorrow: true, levels);

        Assert.Contains("Risco de incêndio para AMANHÃ", text);
        Assert.Contains("Sem registo de concelhos com risco Máximo ou Muito Elevado", text);
        Assert.DoesNotContain("PROIBIDO", text);
    }
}
