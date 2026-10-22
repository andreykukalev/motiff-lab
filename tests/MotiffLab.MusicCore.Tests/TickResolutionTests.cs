using MotiffLab.MusicCore;

namespace MotiffLab.MusicCore.Tests;

public class TickResolutionTests
{
    [Theory]
    [InlineData(480, 480, 480)]
    [InlineData(96, 96, 480)]
    [InlineData(48, 96, 240)]
    [InlineData(1, 3, 160)]
    [InlineData(1, 7, 69)]
    [InlineData(0, 960, 0)]
    public void FromSource_rescales_to_shared_resolution(long sourceTicks, int sourcePpq, long expected)
    {
        Assert.Equal(expected, TickResolution.FromSource(sourceTicks, sourcePpq));
    }

    [Fact]
    public void FromSource_rejects_non_positive_resolution()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TickResolution.FromSource(10, 0));
    }
}