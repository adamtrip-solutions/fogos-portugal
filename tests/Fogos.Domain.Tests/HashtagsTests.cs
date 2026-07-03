using Fogos.Domain.Social;

namespace Fogos.Domain.Tests;

public class HashtagsTests
{
    [Theory]
    [InlineData("Viana do Castelo", "#IRVianadoCastelo")]
    [InlineData("Póvoa de Varzim", "#IRPóvoadeVarzim")]
    [InlineData("Vila Nova de Foz-Côa", "#IRVilaNovadeFozCôa")]
    public void ForConcelho_strips_whitespace_and_hyphens_keeps_accents(string concelho, string expected)
    {
        Assert.Equal(expected, Hashtags.ForConcelho(concelho));
    }

    [Theory]
    [InlineData("Viana do Castelo", "#VianadoCastelo")]
    [InlineData("Vila Nova de Foz-Côa", "#VilaNovadeFozCôa")]
    public void Plain_variant_drops_the_IR_prefix(string concelho, string expected)
    {
        Assert.Equal(expected, Hashtags.Plain(concelho));
    }
}
