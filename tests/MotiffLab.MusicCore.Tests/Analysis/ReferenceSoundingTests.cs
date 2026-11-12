using System.Xml.Linq;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// How the Reference's chords and scales sound (version 0.5, «Аккорды и гаммы»): a position, close or open, played at once,
/// up, down, up and down or broken; a scale up, down or both over one or two octaves; written in MEI with each note spelled.
/// </summary>
public class ReferenceSoundingTests
{
    private static readonly XNamespace s_mei = "http://www.music-encoding.org/ns/mei";

    private static string Pitches(IEnumerable<WrittenTone> tones) => string.Join(" ", tones.Select(tone => $"{tone.Name.Letter}{tone.Octave}"));

    private static string Heard(ReferenceMusic music) =>
        string.Join(" ", music.Notes.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).Select(note => note.Pitch.MidiNumber));

    private static ChordSounding G7(int inversion, ChordSpacing spacing, ChordPattern pattern) =>
        ReferenceSounding.Chord(ChordReference.For(new SpelledName(4, 0), ChordType.Dominant7), inversion, spacing, pattern);

    [Theory]
    [InlineData(0, ChordSpacing.Close, "G3 B3 D4 F4")]
    [InlineData(1, ChordSpacing.Close, "B3 D4 F4 G4")]
    [InlineData(1, ChordSpacing.Open, "B2 F3 D4 G4")]
    [InlineData(3, ChordSpacing.Open, "F3 B3 G4 D5")]
    public void A_seventh_takes_its_position_close_or_open(int inversion, ChordSpacing spacing, string expected)
    {
        ChordSounding sounding = G7(inversion, spacing, ChordPattern.Together);

        Assert.Equal(expected, Pitches(sounding.Tones));
        Assert.Equal(inversion, sounding.Position.Inversion);
        Assert.True(sounding.Tones.Zip(sounding.Tones.Skip(1)).All(pair => pair.First.Midi < pair.Second.Midi));
    }

    [Theory]
    [InlineData(0, "C3 G3 E4")]
    [InlineData(1, "E3 C4 G4")]
    [InlineData(2, "G3 E4 C5")]
    public void An_open_triad_spreads_over_more_than_an_octave(int inversion, string expected)
    {
        ChordSounding sounding = ReferenceSounding.Chord(ChordReference.For(new SpelledName(0, 0), ChordType.Major), inversion, ChordSpacing.Open, ChordPattern.Together);

        Assert.Equal(expected, Pitches(sounding.Tones));
    }

    [Fact]
    public void An_added_ninth_stays_on_top_when_spread()
    {
        ChordSounding sounding = ReferenceSounding.Chord(ChordReference.For(new SpelledName(0, 0), ChordType.Add9), 0, ChordSpacing.Open, ChordPattern.Together);

        Assert.Equal("C3 G3 E4 D5", Pitches(sounding.Tones));
    }

    [Theory]
    [InlineData(ChordPattern.Together, "47 53 62 67")]
    [InlineData(ChordPattern.Up, "47 53 62 67 47 53 62 67")]
    [InlineData(ChordPattern.Down, "47 53 62 67 67 62 53 47")]
    [InlineData(ChordPattern.UpAndDown, "47 53 62 67 47 53 62 67 62 53 47")]
    [InlineData(ChordPattern.Broken, "47 53 62 67 47 62 53 67")]
    public void The_chord_is_struck_for_a_bar_then_played_the_chosen_way(ChordPattern pattern, string expected)
    {
        ReferenceMusic music = G7(1, ChordSpacing.Open, pattern).Music;

        Assert.Equal(expected, Heard(music));
        Assert.Equal(new Tick(pattern == ChordPattern.Together ? 1920 : 3840), music.End);
        Assert.All(music.Notes, note => Assert.Equal(Provenance.Inferred, note.Attribution.Provenance));
    }

    [Fact]
    public void A_broken_chord_rings_until_its_last_note_and_the_keyboard_marks_its_keys()
    {
        ReferenceMusic music = G7(1, ChordSpacing.Open, ChordPattern.Up).Music;
        Note[] run = [.. music.Notes.Where(note => note.Span.Onset.Value >= 1920).OrderBy(note => note.Span.Onset)];

        Assert.Equal([1920L, 2160, 2400, 2640], run.Select(note => note.Span.Onset.Value));
        Assert.All(run, note => Assert.Equal(1920 + 960, note.Span.End.Value));
        Assert.Equal(36, music.KeyboardLow);
        Assert.Equal(71, music.KeyboardHigh);
        Assert.Equal("B F D G", string.Join(" ", music.Keys.Select(key => key.Name)));
    }

    [Fact]
    public void The_mei_writes_the_chord_on_two_staves_and_the_run_in_one_layer_crossing_them()
    {
        XDocument mei = XDocument.Parse(G7(1, ChordSpacing.Open, ChordPattern.Up).Music.Mei);
        XElement[] measures = [.. mei.Descendants(s_mei + "measure")];

        Assert.Equal(2, measures.Length);
        Assert.Equal(2, measures[0].Descendants(s_mei + "chord").Count());
        XElement lower = measures[1].Elements(s_mei + "staff").Single(staff => (string?)staff.Attribute("n") == "2");
        Assert.Equal(4, lower.Descendants(s_mei + "note").Count());
        Assert.Equal(["1", "1"], lower.Descendants(s_mei + "note").Select(note => (string?)note.Attribute("staff")).OfType<string>());
        Assert.Single(measures[1].Descendants(s_mei + "mSpace"));
        Assert.Single(lower.Descendants(s_mei + "beam"));
        Assert.Equal(["ref-0", "ref-1", "ref-2", "ref-3"], measures[0].Descendants(s_mei + "note").Select(note => (string?)note.Attribute(XNamespace.Xml + "id")).Order());
    }

    [Fact]
    public void Spelling_is_kept_in_the_mei_double_sharps_and_all()
    {
        ChordSounding sounding = ReferenceSounding.Chord(ChordReference.For(new SpelledName(4, 1), ChordType.Augmented), 0, ChordSpacing.Close, ChordPattern.Together);
        XElement[] notes = [.. XDocument.Parse(sounding.Music.Mei).Descendants(s_mei + "note")];

        Assert.Equal("G♯3 B♯3 D𝄪4", Pitches(sounding.Tones));
        Assert.Equal(["b", "d", "g"], notes.Select(note => (string?)note.Attribute("pname")));
        Assert.Equal(["s", "x", "s"], notes.Select(note => (string?)note.Attribute("accid")));
        Assert.Equal(["3", "4", "3"], notes.Select(note => (string?)note.Attribute("oct")));
    }

    [Theory]
    [InlineData(ScalePattern.Up, 1, 8, 1)]
    [InlineData(ScalePattern.Down, 1, 8, 1)]
    [InlineData(ScalePattern.UpAndDown, 1, 15, 2)]
    [InlineData(ScalePattern.UpAndDown, 2, 29, 4)]
    public void A_scale_is_played_its_way_over_its_octaves_in_bars_of_quavers(ScalePattern pattern, int octaves, int count, int bars)
    {
        ScaleSounding sounding = ReferenceSounding.Scale(ScaleReference.For(new SpelledName(1, 0), ScaleForm.HarmonicMinor), pattern, octaves);

        Assert.Equal(count, sounding.Music.Notes.Count);
        Assert.Equal(bars * 1920L, sounding.Music.End.Value);
        Assert.All(sounding.Music.Notes, note => Assert.Equal(240, note.Span.Duration));
        Assert.Equal((7 * octaves) + 1, sounding.Music.Keys.Count);
    }

    [Fact]
    public void D_harmonic_minor_is_written_with_one_flat_and_a_sharp_leading_note()
    {
        ScaleSounding sounding = ReferenceSounding.Scale(ScaleReference.For(new SpelledName(1, 0), ScaleForm.HarmonicMinor), ScalePattern.UpAndDown, 1);
        XDocument mei = XDocument.Parse(sounding.Music.Mei);
        XElement[] notes = [.. mei.Descendants(s_mei + "note")];

        Assert.Equal("1f", (string?)mei.Descendants(s_mei + "scoreDef").Single().Attribute("keysig"));
        Assert.Equal("62 64 65 67 69 70 73 74 73 70 69 67 65 64 62", Heard(sounding.Music));
        Assert.Equal("c", (string?)notes[6].Attribute("pname"));
        Assert.Equal("s", (string?)notes[6].Attribute("accid"));
        Assert.Equal("s", (string?)notes[8].Attribute("accid"));
        Assert.Equal("f", (string?)notes[5].Attribute("accid.ges"));
        Assert.Equal(["B♭", "C♯"], sounding.Music.Keys.Where(key => key.Name.Length > 1).Select(key => key.Name));
    }

    [Fact]
    public void The_melodic_minor_comes_down_natural()
    {
        ScaleSounding sounding = ReferenceSounding.Scale(ScaleReference.For(new SpelledName(5, 0), ScaleForm.MelodicMinor), ScalePattern.UpAndDown, 1);

        Assert.Equal("57 59 60 62 64 66 68 69 67 65 64 62 60 59 57", Heard(sounding.Music));
    }

    [Fact]
    public void Music_placed_in_a_slower_piece_keeps_its_own_tempo_in_whole_ticks()
    {
        ReferenceMusic music = G7(0, ChordSpacing.Close, ChordPattern.Up).Music;

        PlacedMusic placed = music.Place(new Tick(9600), 1_200_000);

        Assert.Equal(new TickSpan(new Tick(9600), 1920), placed.Span);
        Assert.Equal([9600L, 10560, 10680, 10800, 10920], placed.Notes.Select(note => note.Span.Onset.Value).Distinct().Order());
        Assert.Equal(music.Notes.Count, placed.Notes.Count);
        Assert.Equal(3840, music.LengthOn(KeyReference.MicrosecondsPerQuarter));
    }

    [Fact]
    public void The_same_choices_give_the_same_music()
    {
        Assert.Equal(G7(2, ChordSpacing.Open, ChordPattern.Broken).Music.Mei, G7(2, ChordSpacing.Open, ChordPattern.Broken).Music.Mei);
    }

    [Fact]
    public void Chords_and_scales_are_offered_in_their_groups()
    {
        Assert.Equal(
            [ChordFamily.Triad, ChordFamily.Triad, ChordFamily.Triad, ChordFamily.Triad, ChordFamily.Triad, ChordFamily.Triad,
             ChordFamily.Seventh, ChordFamily.Seventh, ChordFamily.Seventh, ChordFamily.Seventh, ChordFamily.Seventh,
             ChordFamily.SixthOrNinth, ChordFamily.SixthOrNinth, ChordFamily.SixthOrNinth],
            ChordReference.Types().Select(choice => choice.Family));
        IReadOnlyList<ScaleFormChoice> forms = ScaleReference.Forms(new Words(TextLanguage.Russian));
        Assert.Equal(4, forms.Count(choice => choice.Family == ScaleFamily.MajorAndMinor));
        Assert.Equal(5, forms.Count(choice => choice.Family == ScaleFamily.Mode));
        Assert.Equal(["мажорная", "минорная"], forms.Where(choice => choice.Family == ScaleFamily.Pentatonic).Select(choice => choice.Short));
        Assert.Equal("дорийский", forms.Single(choice => choice.Form == ScaleForm.Dorian).Short);
    }
}