using MotiffLab.MusicCore.Notation;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>The names the piano roll writes on its rows and notes (0018 section 4): spelled for the key, as letters or as Russian syllables.</summary>
public class NoteNamesTests
{
    [Theory]
    [InlineData(60, 0, "C4")]
    [InlineData(66, 1, "F♯4")]
    [InlineData(66, -6, "G♭4")]
    [InlineData(75, 0, "D♯5")]
    [InlineData(70, -1, "B♭4")]
    [InlineData(21, 0, "A0")]
    [InlineData(59, -7, "C♭4")]
    public void Letters_spell_for_the_key_as_the_score_does(int pitch, int fifths, string name) =>
        Assert.Equal(name, NoteNames.Name(pitch, fifths, NoteNaming.Letters));

    [Theory]
    [InlineData(60, 0, "до¹")]
    [InlineData(72, 0, "до²")]
    [InlineData(108, 0, "до⁵")]
    [InlineData(57, 0, "ля")]
    [InlineData(48, 0, "до")]
    [InlineData(45, 0, "Ля")]
    [InlineData(33, 0, "Ля₁")]
    [InlineData(21, 0, "Ля₂")]
    [InlineData(66, 1, "фа♯¹")]
    [InlineData(66, -6, "соль♭¹")]
    [InlineData(75, 0, "ре♯²")]
    [InlineData(59, -7, "до♭¹")]
    public void Syllables_mark_the_octave_as_Russian_schools_write_it(int pitch, int fifths, string name) =>
        Assert.Equal(name, NoteNames.Name(pitch, fifths, NoteNaming.Syllables));

    [Fact]
    public void Auto_follows_the_language_and_a_choice_wins_over_it()
    {
        Assert.Equal(NoteNaming.Syllables, NoteNames.For("auto", Words.Russian));
        Assert.Equal(NoteNaming.Letters, NoteNames.For("auto", Words.English));
        Assert.Equal(NoteNaming.Letters, NoteNames.For("letters", Words.Russian));
        Assert.Equal(NoteNaming.Syllables, NoteNames.For("syllables", Words.English));
        Assert.Equal(NoteNaming.Letters, NoteNames.For(null, Words.English));
    }

    [Fact]
    public void The_naming_setting_is_on_the_device_and_follows_the_language_by_default()
    {
        var setting = MotiffLab.MusicCore.Settings.SettingsCatalogue.Get(NoteNames.Setting);
        Assert.Equal("auto", setting.Default);
        Assert.Equal(["auto", "letters", "syllables"], setting.Choices);
    }

    [Theory]
    [InlineData(60, "первая октава")]
    [InlineData(59, "малая октава")]
    [InlineData(21, "субконтроктава")]
    public void The_octave_is_named_in_words(int pitch, string russian)
    {
        Assert.Equal(russian, NoteNames.OctaveOf(pitch, Words.Russian));
        Assert.Equal($"octave {(pitch / 12) - 1}", NoteNames.OctaveOf(pitch, Words.English));
    }

    [Fact]
    public void A_key_outside_the_piano_or_the_signatures_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NoteNames.Name(128, 0, NoteNaming.Letters));
        Assert.Throws<ArgumentOutOfRangeException>(() => NoteNames.Name(60, 8, NoteNaming.Letters));
    }
}