using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Time;

public class QuarterFractionTests
{
    [Fact]
    public void Default_is_zero()
    {
        Assert.Equal(QuarterFraction.Zero, default);
        Assert.Equal(0, default(QuarterFraction).Numerator);
        Assert.Equal(1, default(QuarterFraction).Denominator);
    }

    [Theory]
    [InlineData(2, 4, 1, 2)]
    [InlineData(-3, 9, -1, 3)]
    [InlineData(3, -9, -1, 3)]
    [InlineData(0, 7, 0, 1)]
    [InlineData(480, 480, 1, 1)]
    public void Values_are_reduced_with_positive_denominator(long numerator, long denominator, long expectedNumerator, long expectedDenominator)
    {
        QuarterFraction value = new(numerator, denominator);

        Assert.Equal(expectedNumerator, value.Numerator);
        Assert.Equal(expectedDenominator, value.Denominator);
    }

    [Fact]
    public void Zero_denominator_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuarterFraction(1, 0));
    }

    [Fact]
    public void Equal_values_compare_equal_whatever_their_spelling()
    {
        Assert.Equal(new QuarterFraction(1, 3), new QuarterFraction(160, 480));
        Assert.Equal(new QuarterFraction(1, 3).GetHashCode(), new QuarterFraction(160, 480).GetHashCode());
        Assert.True(new QuarterFraction(1, 3) < new QuarterFraction(1, 2));
        Assert.True(new QuarterFraction(-1, 2) < QuarterFraction.Zero);
    }

    [Fact]
    public void Addition_and_subtraction_are_exact()
    {
        QuarterFraction third = new(1, 3);
        QuarterFraction sum = third + third + third;

        Assert.Equal(QuarterFraction.FromQuarters(1), sum);
        Assert.Equal(new QuarterFraction(1, 6), new QuarterFraction(1, 2) - third);
    }

    [Fact]
    public void Arithmetic_that_cannot_be_represented_throws()
    {
        QuarterFraction huge = QuarterFraction.FromQuarters(long.MaxValue);

        Assert.Throws<OverflowException>(() => huge + QuarterFraction.FromQuarters(1));
        Assert.Throws<OverflowException>(() => new QuarterFraction(1, long.MaxValue) + new QuarterFraction(1, long.MaxValue - 1));
    }
}