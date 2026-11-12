using System.Xml.Linq;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// The harmonic explorer (version 0.5, inside the Reference): a key's chords, a progression built from them in any position,
/// and a texture in the right hand over the bass in the left, each chord placed near the one before. Nothing reads a piece.
/// </summary>
public class HarmonyExplorerTests
{
    private static readonly XNamespace s_mei = "http://www.music-encoding.org/ns/mei";

    private static readonly MusicalKey s_aMinor = new(0, KeyMode.Minor);

    private static ExplorerProgression Cadence(ExplorerTexture texture, ChordSpacing spacing = ChordSpacing.Close) =>
        HarmonyExplorer.Play(s_aMinor, [new("i", 0), new("iv", 1), new("V7", 1), new("i", 0)], texture, spacing, KeyReference.MicrosecondsPerQuarter);

    private static int[] Bar(ReferenceMusic music, int bar) =>
        [.. music.Notes.Where(note => note.Span.Onset.Value / 1920 == bar).OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).Select(note => note.Pitch.MidiNumber)];

    [Fact]
    public void A_minor_offers_the_natural_minor_chords_with_the_harmonic_dominant()
    {
        IReadOnlyList<ExplorerChord> chords = HarmonyExplorer.Chords(s_aMinor);

        Assert.Equal(["i", "ii°", "III", "iv", "v", "V", "V7", "VI", "VII"], chords.Select(chord => chord.Numeral));
        Assert.Equal(["Am", "Bdim", "C", "Dm", "Em", "E", "E7", "F", "G"], chords.Select(chord => chord.Symbol));
        Assert.Equal(4, chords.Single(chord => chord.Id == "V7").Positions);
        Assert.Equal(3, chords[0].Positions);
    }

    [Fact]
    public void A_major_key_offers_its_seven_triads_and_the_dominant_seventh()
    {
        IReadOnlyList<ExplorerChord> chords = HarmonyExplorer.Chords(new MusicalKey(-3, KeyMode.Major));

        Assert.Equal(["I", "ii", "iii", "IV", "V", "V7", "vi", "vii°"], chords.Select(chord => chord.Numeral));
        Assert.Equal(["E♭", "Fm", "Gm", "A♭", "B♭", "B♭7", "Cm", "Ddim"], chords.Select(chord => chord.Symbol));
    }

    [Fact]
    public void A_key_starts_with_tonic_subdominant_dominant_tonic()
    {
        Assert.Equal([new ExplorerStep("i", 0), new("iv", 0), new("V", 0), new("i", 0)], HarmonyExplorer.Start(s_aMinor));
        Assert.Equal(30, HarmonyExplorer.Keys.Count);
    }

    [Fact]
    public void Each_bar_names_its_chord_with_the_figure_and_bass_of_its_position()
    {
        ExplorerProgression progression = Cadence(ExplorerTexture.Alberti);

        Assert.Equal([null, "6", "6/5", null], progression.Bars.Select(bar => bar.Figure));
        Assert.Equal(["Am", "Dm/F", "E7/G♯", "Am"], progression.Bars.Select(bar => bar.Symbol));
    }

    [Fact]
    public void Alberti_plays_low_high_middle_high_over_the_bass_and_the_hand_moves_little()
    {
        ReferenceMusic music = Cadence(ExplorerTexture.Alberti).Music;

        // The bass held a bar, then A3 E4 C4 E4 twice.
        Assert.Equal([45, 57, 64, 60, 64, 57, 64, 60, 64], Bar(music, 0));
        Note bass = music.Notes.First(note => note.Pitch.MidiNumber == 45);
        Assert.Equal(1920, bass.Span.Duration);

        // Dm over F: the right hand's D F A stacked nearest A C E.
        Assert.Equal(41, Bar(music, 1)[0]);
        Assert.Equal([57, 65, 62, 65], Bar(music, 1)[1..5]);

        // E7 over G♯: the right hand takes B D E, nearest D F A.
        Assert.Equal(44, Bar(music, 2)[0]);
        Assert.Equal([59, 64, 62, 64], Bar(music, 2)[1..5]);
    }

    [Theory]
    [InlineData(ExplorerTexture.Arpeggio, new[] { 57, 60, 64, 69, 64, 60, 57, 60 })]
    [InlineData(ExplorerTexture.Broken, new[] { 57, 64, 60, 69, 57, 64, 60, 69 })]
    public void The_textures_play_their_figures(ExplorerTexture texture, int[] figure)
    {
        Assert.Equal([45, .. figure], Bar(Cadence(texture).Music, 0));
    }

    [Fact]
    public void Together_strikes_the_hand_once_and_open_spreads_it()
    {
        Assert.Equal([45, 57, 60, 64], Bar(Cadence(ExplorerTexture.Together).Music, 0));
        Assert.Equal([45, 57, 64, 72], Bar(Cadence(ExplorerTexture.Together, ChordSpacing.Open).Music, 0));
    }

    [Fact]
    public void The_mei_keeps_the_key_and_writes_the_leading_note_sharp()
    {
        ExplorerProgression progression = Cadence(ExplorerTexture.Alberti);
        XDocument mei = XDocument.Parse(progression.Music.Mei);
        XElement[] measures = [.. mei.Descendants(s_mei + "measure")];

        Assert.Equal(4, measures.Length);
        Assert.Equal("0", (string?)mei.Descendants(s_mei + "scoreDef").Single().Attribute("keysig"));
        XElement leading = measures[2].Elements(s_mei + "staff").Last().Descendants(s_mei + "note").Single();
        Assert.Equal(("g", "s", "2"), ((string?)leading.Attribute("pname"), (string?)leading.Attribute("accid"), (string?)leading.Attribute("oct")));
        Assert.Equal(4 * 1920L, progression.Music.End.Value);
        Assert.Empty(progression.Music.Keys);
    }

    [Fact]
    public void Steps_the_key_has_not_are_left_out_and_eight_bars_at_most()
    {
        ExplorerStep[] steps = [new("i", 0), new("IX", 0), new("i", 5), .. Enumerable.Repeat(new ExplorerStep("VI", 2), 12)];

        ExplorerProgression progression = HarmonyExplorer.Play(s_aMinor, steps, ExplorerTexture.Together, ChordSpacing.Close, KeyReference.MicrosecondsPerQuarter);

        Assert.Equal(HarmonyExplorer.MaxBars, progression.Bars.Count);
        Assert.Equal("F/C", progression.Bars[^1].Symbol);
        Assert.Equal("6/4", progression.Bars[^1].Figure);
    }

    [Fact]
    public void The_same_progression_gives_the_same_music()
    {
        Assert.Equal(Cadence(ExplorerTexture.Broken).Music.Mei, Cadence(ExplorerTexture.Broken).Music.Mei);
    }
}