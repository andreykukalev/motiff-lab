using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Tests.Events;

public class PitchTests
{
    [Theory]
    [InlineData(60, 0, 4, "C4")]
    [InlineData(69, 9, 4, "A4")]
    [InlineData(61, 1, 4, "C♯4")]
    [InlineData(0, 0, -1, "C-1")]
    [InlineData(21, 9, 0, "A0")]
    [InlineData(108, 0, 8, "C8")]
    [InlineData(127, 7, 9, "G9")]
    public void Pitch_class_octave_and_name(int midiNumber, int pitchClass, int octave, string name)
    {
        Pitch pitch = new(midiNumber);

        Assert.Equal(midiNumber, pitch.MidiNumber);
        Assert.Equal(pitchClass, pitch.PitchClass);
        Assert.Equal(octave, pitch.Octave);
        Assert.Equal(name, pitch.ToString());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void Out_of_range_pitches_are_rejected(int midiNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pitch(midiNumber));
    }

    [Fact]
    public void Pitches_order_by_height()
    {
        Assert.True(new Pitch(59) < new Pitch(60));
        Assert.True(new Pitch(72) >= new Pitch(72));
        Assert.Equal(new Pitch(64), new Pitch(64));
        Assert.Equal(-1, new Pitch(40).CompareTo(new Pitch(41)));
    }
}