using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Time;

public class TimeSignatureTests
{
    [Theory]
    [InlineData(4, 4, 480, 1920)]
    [InlineData(3, 4, 480, 1440)]
    [InlineData(6, 8, 240, 1440)]
    [InlineData(2, 2, 960, 1920)]
    [InlineData(5, 16, 120, 600)]
    [InlineData(1, 128, 15, 15)]
    [InlineData(255, 1, 1920, 489600)]
    public void Beat_and_bar_lengths_are_whole_ticks(int numerator, int denominator, long beatTicks, long barTicks)
    {
        TimeSignature signature = new(numerator, denominator);

        Assert.Equal(numerator, signature.Numerator);
        Assert.Equal(denominator, signature.Denominator);
        Assert.Equal(beatTicks, signature.BeatTicks);
        Assert.Equal(barTicks, signature.BarTicks);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(256, 4)]
    [InlineData(4, 0)]
    [InlineData(4, 3)]
    [InlineData(4, 12)]
    [InlineData(4, 256)]
    [InlineData(4, -4)]
    public void Invalid_signatures_are_rejected(int numerator, int denominator)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeSignature(numerator, denominator));
    }

    [Fact]
    public void Default_signature_is_valid()
    {
        TimeSignature signature = default;

        Assert.Equal(new TimeSignature(1, 1), signature);
        Assert.Equal(1920, signature.BarTicks);
    }

    [Fact]
    public void Signatures_compare_by_value()
    {
        Assert.Equal(TimeSignature.CommonTime, new TimeSignature(4, 4));
        Assert.NotEqual(new TimeSignature(6, 8), new TimeSignature(3, 4));
        Assert.Equal("6/8", new TimeSignature(6, 8).ToString());
    }
}