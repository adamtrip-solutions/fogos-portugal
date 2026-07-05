using Fogos.Domain.Time;

namespace Fogos.Domain.Tests;

public class FogosClockTests
{
    private readonly FogosClock _clock = new();

    [Fact]
    public void FromLisbon_summer_is_utc_plus_one()
    {
        var result = _clock.FromLisbon(new DateTime(2026, 7, 1, 12, 0, 0));
        Assert.Equal(new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc), result.UtcDateTime);
    }

    [Fact]
    public void FromLisbon_winter_is_utc()
    {
        var result = _clock.FromLisbon(new DateTime(2026, 1, 15, 12, 0, 0));
        Assert.Equal(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc), result.UtcDateTime);
    }
}
