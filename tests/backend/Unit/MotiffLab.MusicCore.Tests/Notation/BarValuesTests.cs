using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>The note values a span of a bar is written in (<see cref="BarValues"/>).</summary>
public class BarValuesTests
{
    private static ValueGrid Grid(TimeSignature signature, long origin = 0) => new(origin, BarValues.Pulse(signature), BarValues.Middle(signature));

    private static (long Start, int Dur, int Dots)[] Values(long from, long to, ValueGrid grid) =>
        [.. BarValues.Decompose(from, to, grid).Select(value => (value.Start, value.Dur, value.Dots))];

    [Theory]
    [InlineData(6, 8, true, 720, 0)]
    [InlineData(12, 8, true, 720, 1440)]
    [InlineData(9, 8, true, 720, 0)]
    [InlineData(3, 8, false, 240, 0)]
    [InlineData(4, 4, false, 480, 960)]
    [InlineData(3, 4, false, 480, 0)]
    [InlineData(2, 2, false, 960, 0)]
    public void Compound_meters_pulse_in_dotted_beats_and_bars_of_four_have_a_middle(int numerator, int denominator, bool compound, long pulse, long middle)
    {
        var signature = new TimeSignature(numerator, denominator);

        Assert.Equal((compound, pulse, middle), (BarValues.IsCompound(signature), BarValues.Pulse(signature), BarValues.Middle(signature)));
    }

    [Fact]
    public void A_value_starts_on_a_multiple_of_its_length_or_the_beat_and_crosses_the_middle_only_from_the_bar_start()
    {
        ValueGrid common = Grid(TimeSignature.CommonTime);

        Assert.Equal([(0L, 1, 0)], Values(0, 1920, common));
        Assert.Equal([(0L, 2, 1)], Values(0, 1440, common));
        Assert.Equal([(480L, 4, 0), (960L, 2, 0)], Values(480, 1920, common));
        Assert.Equal([(240L, 8, 0), (480L, 4, 0)], Values(240, 960, common));
    }

    [Fact]
    public void A_compound_bar_is_written_in_dotted_values()
    {
        ValueGrid sixEight = Grid(new TimeSignature(6, 8));

        Assert.Equal([(0L, 2, 1)], Values(0, 1440, sixEight));
        Assert.Equal([(720L, 4, 1)], Values(720, 1440, sixEight));
    }

    [Fact]
    public void A_pickup_counts_from_where_a_full_bar_would_start()
    {
        Assert.Equal([(1440L, 4, 0)], Values(1440, 1920, Grid(TimeSignature.CommonTime)));
        Assert.Equal([(0L, 4, 0)], Values(0, 480, Grid(TimeSignature.CommonTime, origin: -1440)));
    }

    [Fact]
    public void In_a_triplet_beat_three_values_take_the_time_of_two()
    {
        TupletSpan triplet = new(480, 480, 3, 2);

        (long Start, long Ticks, int Dur, TupletSpan? Tuplet)[] Pieces(long to) => [.. BarValues.Pieces(0, to, Grid(TimeSignature.CommonTime), [triplet])
            .Select(piece => (piece.Start, piece.Ticks, piece.Dur, piece.Tuplet))];

        Assert.Equal([(0L, 480L, 4, null), (480L, 160L, 8, triplet)], Pieces(640));
        Assert.Equal([(0L, 480L, 4, null), (480L, 320L, 4, triplet)], Pieces(800));
    }

    [Fact]
    public void A_rest_fills_the_time_and_a_lower_voice_fills_it_with_spaces()
    {
        Rest rest = Assert.IsType<Rest>(Assert.Single(BarValues.Fill(0, 480, Grid(TimeSignature.CommonTime), space: false)));
        Rest space = Assert.IsType<Rest>(Assert.Single(BarValues.Fill(0, 480, Grid(TimeSignature.CommonTime), space: true, [])));

        Assert.Equal((4, false, true), (rest.Dur, rest.IsSpace, space.IsSpace));
    }
}