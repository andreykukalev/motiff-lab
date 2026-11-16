using MotiffLab.MusicCore.Analysis;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>The scale-step pitch space the diatonic melody operations move in.</summary>
public class ScaleStepsTests
{
    private static readonly MusicalKey s_cMajor = new(0, KeyMode.Major);
    private static readonly MusicalKey s_aMinor = new(0, KeyMode.Minor);

    [Fact]
    public void Scale_notes_have_no_alteration_and_others_sit_on_the_scale_note_below()
    {
        Assert.Equal(new ScalePosition(35, 0), ScaleSteps.Locate(s_cMajor, 60));
        Assert.Equal(new ScalePosition(36, 1), ScaleSteps.Locate(s_cMajor, 63));

        // G♯4 in A minor is G raised: degree 7 of the natural minor, in the octave from A3.
        Assert.Equal(new ScalePosition((4 * 7) + 6, 1), ScaleSteps.Locate(s_aMinor, 68));
    }

    [Theory]
    [InlineData(64, 2, 67)] // E4 up a 3rd is G4
    [InlineData(64, -2, 60)] // E4 down a 3rd is C4
    [InlineData(71, 1, 72)] // B4 up a 2nd is C5, a semitone
    [InlineData(60, 7, 72)] // seven steps make an octave
    [InlineData(61, 1, 63)] // C♯4 up a 2nd is D♯4: the alteration moves with it
    public void Moving_counts_scale_steps_in_C_major(int from, int steps, int to)
    {
        Assert.Equal(to, ScaleSteps.Move(s_cMajor, from, steps));
    }

    [Fact]
    public void A_raised_note_that_would_land_on_the_next_scale_note_becomes_the_plain_scale_note()
    {
        // G♯4 down one step in A minor would be F raised, F♯4, which is not in the scale, so it stays raised;
        // up two steps it would be B raised, which is C5, the next scale note, so it is plain B4.
        Assert.Equal(66, ScaleSteps.Move(s_aMinor, 68, -1));
        Assert.Equal(71, ScaleSteps.Move(s_aMinor, 68, 2));
    }

    [Fact]
    public void Every_note_in_every_key_comes_back_from_its_position()
    {
        for (int fifths = -7; fifths <= 7; fifths++)
        {
            foreach (KeyMode mode in Enum.GetValues<KeyMode>())
            {
                var key = new MusicalKey(fifths, mode);
                for (int midi = 0; midi <= 127; midi++)
                {
                    Assert.Equal(midi, ScaleSteps.MidiNumber(key, ScaleSteps.Locate(key, midi)));
                }
            }
        }
    }
}