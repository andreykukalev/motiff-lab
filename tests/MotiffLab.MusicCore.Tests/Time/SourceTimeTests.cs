using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Time;

public class MidiTimeBaseTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-96)]
    [InlineData(MidiTimeBase.MaxTicksPerQuarter + 1)]
    public void Invalid_ppq_is_rejected(int ppq)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MidiTimeBase(ppq));
    }

    [Fact]
    public void Header_division_reads_ppq_and_rejects_smpte()
    {
        Assert.Equal(96, MidiTimeBase.FromHeaderDivision(96).TicksPerQuarter);
        Assert.Throws<NotSupportedException>(() => MidiTimeBase.FromHeaderDivision(0xE728)); // -25 fps, 40 ticks per frame
    }

    [Theory]
    [InlineData(96, 96, 480, true)]
    [InlineData(96, 24, 120, true)]
    [InlineData(960, 3, 2, false)] // 1.5 ticks rounds up
    [InlineData(960, 1921, 961, false)]
    [InlineData(120, 1, 4, true)]
    public void Ticks_convert_through_exact_quarters(int ppq, long midiTick, long expectedTick, bool exact)
    {
        ConvertedTick result = new MidiTimeBase(ppq).ToCanonical(midiTick);

        Assert.Equal(new Tick(expectedTick), result.Tick);
        Assert.Equal(exact, result.IsExact);
    }

    [Fact]
    public void Negative_ticks_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MidiTimeBase(480).ToQuarters(-1));
    }

    [Fact]
    public void Note_span_uses_note_on_and_note_off()
    {
        ConvertedSpan result = new MidiTimeBase(96).ToCanonicalSpan(96, 144);

        Assert.Equal(new TickSpan(new Tick(480), 240), result.Span);
        Assert.True(result.IsExact);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MidiTimeBase(96).ToCanonicalSpan(96, 96));
    }
}

public class MusicXmlDivisionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_divisions_are_rejected(long divisions)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MusicXmlDivisions(divisions));
    }

    [Fact]
    public void Durations_are_lengths_in_quarter_notes()
    {
        MusicXmlDivisions divisions = new(4);

        Assert.Equal(new QuarterFraction(1, 4), divisions.ToQuarters(1));
        Assert.Equal(QuarterFraction.FromQuarters(2), divisions.ToQuarters(8));
        Assert.Throws<ArgumentOutOfRangeException>(() => divisions.ToQuarters(-1));
    }

    [Fact]
    public void Positions_stay_exact_across_a_divisions_change()
    {
        // Bar 1 in 4/4 with divisions=2: eight eighths. Bar 2 switches to divisions=3: a triplet
        // eighth, then a backup of that note and a forward to beat 2.
        MusicXmlDivisions bar1 = new(2);
        MusicXmlDivisions bar2 = new(3);

        QuarterFraction position = QuarterFraction.Zero;
        for (int i = 0; i < 8; i++)
        {
            position += bar1.ToQuarters(1);
        }

        QuarterFraction tripletOnset = position;
        position += bar2.ToQuarters(1);
        position -= bar2.ToQuarters(1); // <backup>
        position += bar2.ToQuarters(3); // <forward>

        Assert.Equal(new Tick(1920), CanonicalTime.ToTick(tripletOnset).Tick);
        Assert.Equal(new TickSpan(new Tick(1920), 160), CanonicalTime.ToSpan(tripletOnset, tripletOnset + bar2.ToQuarters(1)).Span);
        Assert.Equal(new Tick(2400), CanonicalTime.ToTick(position).Tick);
    }

    [Fact]
    public void Same_number_means_different_time_in_each_format()
    {
        // 4 MIDI ticks at PPQ 480 and 4 MusicXML divisions at divisions=480 agree only because
        // both resolutions happen to be 480. With divisions=1 the same number is four quarters.
        Assert.Equal(new Tick(4), new MidiTimeBase(480).ToCanonical(4).Tick);
        Assert.Equal(new Tick(1920), CanonicalTime.ToTick(new MusicXmlDivisions(1).ToQuarters(4)).Tick);
    }
}