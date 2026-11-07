using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>The harmonic motion and the texture that the Insights stream leads with (testing item 14), all inferred.</summary>
public class HarmonicMotionTests
{
    private const long Quarter = 480;
    private const long Bar = 4 * Quarter;

    private static readonly MusicalKey s_cMajor = new(0, KeyMode.Major);
    private static readonly MusicalKey s_aMinor = new(0, KeyMode.Minor);
    private static readonly CharacterContext s_context = new([new PlaybackTempo(Tick.Zero, PlaybackTempo.DefaultMicrosecondsPerQuarter)], TempoAssumed: true, FromNotation: true);

    private static ChordLabel C(int root, ChordQuality quality = ChordQuality.Major) => new(root, quality);

    private static Note N(string id, int pitch, long onset, long duration, Hand? hand = null) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
        Hand = hand is { } h ? new Attributed<Hand>(h, Attribution.Imported) : null,
    };

    private static MotifSelection All(Note[] notes) => MotifSelector.Select(notes, MeterMap.Default, TickSpan.FromBounds(Tick.Zero, new Tick(notes.Max(note => note.Span.End.Value))), SelectionFilter.All);

    private static ImportedPiece FurElise() =>
        PieceImporter.Import("fur-elise.musicxml", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", "fur-elise-original.musicxml"))).Piece!;

    private static MotifSelection Bars(ImportedPiece piece, int first, int last) =>
        MotifSelector.Select(piece.Notes, piece.Meter, TickSpan.FromBounds(piece.Meter.GetBar(first).Span.Onset, piece.Meter.GetBar(last).Span.End), SelectionFilter.All);

    [Theory]
    [InlineData(0, ChordQuality.Major, "I")]
    [InlineData(5, ChordQuality.Major, "IV")]
    [InlineData(2, ChordQuality.Minor, "ii")]
    [InlineData(7, ChordQuality.Dominant7, "V7")]
    [InlineData(11, ChordQuality.Diminished, "vii°")]
    [InlineData(8, ChordQuality.Major, "♭VI")]
    [InlineData(10, ChordQuality.Major, "♭VII")]
    public void Numerals_in_c_major(int root, ChordQuality quality, string numeral) => Assert.Equal(numeral, HarmonyAnalyzer.Numeral(C(root, quality), s_cMajor));

    [Theory]
    [InlineData(9, ChordQuality.Minor, "i")]
    [InlineData(4, ChordQuality.Major, "V")]
    [InlineData(2, ChordQuality.Minor, "iv")]
    [InlineData(5, ChordQuality.Major, "VI")]
    [InlineData(7, ChordQuality.Major, "VII")]
    [InlineData(8, ChordQuality.Diminished7, "vii°7")]
    [InlineData(11, ChordQuality.HalfDiminished7, "iiø7")]
    public void Numerals_in_a_minor(int root, ChordQuality quality, string numeral) => Assert.Equal(numeral, HarmonyAnalyzer.Numeral(C(root, quality), s_aMinor));

    [Fact]
    public void Functions_are_given_only_to_chords_in_the_key()
    {
        Assert.Equal(HarmonicFunction.Tonic, HarmonyAnalyzer.FunctionOf(C(0), s_cMajor));
        Assert.Equal(HarmonicFunction.Subdominant, HarmonyAnalyzer.FunctionOf(C(5), s_cMajor));
        Assert.Equal(HarmonicFunction.Subdominant, HarmonyAnalyzer.FunctionOf(C(2, ChordQuality.Minor), s_cMajor));
        Assert.Equal(HarmonicFunction.Dominant, HarmonyAnalyzer.FunctionOf(C(7, ChordQuality.Dominant7), s_cMajor));
        Assert.Equal(HarmonicFunction.Dominant, HarmonyAnalyzer.FunctionOf(C(11, ChordQuality.Diminished), s_cMajor));
        Assert.Equal(HarmonicFunction.None, HarmonyAnalyzer.FunctionOf(C(9, ChordQuality.Minor), s_cMajor));
        // C7 holds B♭, outside C major: it leads to F, so it is no tonic.
        Assert.Equal(HarmonicFunction.None, HarmonyAnalyzer.FunctionOf(C(0, ChordQuality.Dominant7), s_cMajor));
        // E major in A minor holds the raised seventh, G♯: the dominant.
        Assert.Equal(HarmonicFunction.Dominant, HarmonyAnalyzer.FunctionOf(C(4), s_aMinor));
        Assert.Equal(HarmonicFunction.Dominant, HarmonyAnalyzer.FunctionOf(C(8, ChordQuality.Diminished7), s_aMinor));
    }

    [Fact]
    public void Cadences_are_read_from_the_last_two_chords()
    {
        Assert.Equal(Cadence.Authentic, HarmonyAnalyzer.CadenceOf([C(0), C(5), C(7, ChordQuality.Dominant7), C(0)], s_cMajor));
        Assert.Equal(Cadence.Plagal, HarmonyAnalyzer.CadenceOf([C(0), C(5), C(0)], s_cMajor));
        Assert.Equal(Cadence.Half, HarmonyAnalyzer.CadenceOf([C(0), C(5), C(7)], s_cMajor));
        Assert.Equal(Cadence.Deceptive, HarmonyAnalyzer.CadenceOf([C(0), C(7), C(9, ChordQuality.Minor)], s_cMajor));
        Assert.Equal(Cadence.Deceptive, HarmonyAnalyzer.CadenceOf([C(9, ChordQuality.Minor), C(4), C(5)], s_aMinor));
        Assert.Equal(Cadence.Home, HarmonyAnalyzer.CadenceOf([C(0), C(9, ChordQuality.Minor), C(0)], s_cMajor));
        Assert.Equal(Cadence.Open, HarmonyAnalyzer.CadenceOf([C(0), C(9, ChordQuality.Minor)], s_cMajor));
        Assert.Equal(Cadence.Static, HarmonyAnalyzer.CadenceOf([C(0), C(0)], s_cMajor));
    }

    [Fact]
    public void A_cadence_in_block_chords_is_tonic_subdominant_dominant_tonic()
    {
        Note[] notes = HarmonyAnalyzerTests.Cadence();
        MotifFact motion = HarmonyAnalyzer.Analyze(notes, MeterMap.Default, All(notes), s_cMajor).Facts[0];

        Assert.Equal("harmony.motion", motion.Id);
        Assert.Equal(FactLens.Harmony, motion.Lens);
        Assert.Equal(
            "Harmonic motion: C → F → G7 → C, I → IV → V7 → I (tonic → subdominant → dominant → tonic). It comes to rest: the dominant pulls to the tonic and resolves into it, an authentic cadence.",
            motion.Text);
        Assert.Equal(["C (I, tonic), from bar 1", "F (IV, subdominant), from bar 2", "G7 (V7, dominant), from bar 3", "C (I, tonic), from bar 4"], motion.Places.Select(place => place.Label));
        Assert.Contains("C major", motion.Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_key_there_is_no_motion()
    {
        Note[] notes = HarmonyAnalyzerTests.Cadence();
        Assert.DoesNotContain(HarmonyAnalyzer.Analyze(notes, MeterMap.Default, All(notes), null).Facts, fact => fact.Id == "harmony.motion");
    }

    [Fact]
    public void Fur_elise_moves_between_tonic_and_dominant_and_closes_on_the_tonic()
    {
        ImportedPiece piece = FurElise();
        MotifFact motion = HarmonyAnalyzer.Analyze(piece.Notes, piece.Meter, Bars(piece, 1, 8), s_aMinor).Facts.Single(fact => fact.Id == "harmony.motion");
        Assert.StartsWith("Harmonic motion: Am → E → Am → E → Am, i → V → i → V → i (tonic → dominant → tonic → dominant → tonic).", motion.Text, StringComparison.Ordinal);
        Assert.Contains("authentic cadence", motion.Text, StringComparison.Ordinal);
        Assert.Equal(FactCertainty.Uncertain, motion.Certainty);

        MotifFact half = HarmonyAnalyzer.Analyze(piece.Notes, piece.Meter, Bars(piece, 2, 3), s_aMinor, Words.Russian).Facts.Single(fact => fact.Id == "harmony.motion");
        Assert.Equal(
            "Гармоническое движение: Am → E, i → V (тоника → доминанта). Отрывок останавливается на доминанте и остаётся открытым — половинный каданс: музыка ждёт ответа.",
            half.Text);
    }

    [Fact]
    public void Fur_elise_is_a_melody_over_broken_chords_taking_turns()
    {
        ImportedPiece piece = FurElise();
        MotifSelection selection = Bars(piece, 1, 8);
        MotifFact texture = MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, null, CharacterContext.FromPiece(piece)).Single(fact => fact.Id == "character.texture");

        Assert.Equal(FactLens.Character, texture.Lens);
        Assert.Equal(FactCertainty.Inferred, texture.Certainty);
        Assert.Equal(
            "Texture: a melody over broken chords in the left hand. The two parts take turns: the accompaniment sounds in the melody's pauses. The broken chords keep the harmony moving under the melody.",
            texture.Text);
        Assert.Equal(["the melody", "the accompaniment"], texture.Places.Select(place => place.Label));

        MotifFact russian = MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, null, CharacterContext.FromPiece(piece), Words.Russian).Single(fact => fact.Id == "character.texture");
        Assert.StartsWith("Фактура: мелодия над разложенными аккордами в левой руке.", russian.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Block_chords_under_a_melody_sound_with_it()
    {
        Note[] notes = HarmonyAnalyzerTests.Cadence();
        MotifFact texture = MotifAnalyzer.AnalyzeCharacter(notes, MeterMap.Default, All(notes), null, s_context).Single(fact => fact.Id == "character.texture");
        Assert.Equal("Texture: a melody over chords. The two parts sound together, onset with onset. The chords mark the beats and the harmony at once.", texture.Text);
    }

    [Fact]
    public void A_single_line_has_no_accompaniment()
    {
        Note[] line = [N("a", 60, 0, Quarter), N("b", 64, Quarter, Quarter), N("c", 67, 2 * Quarter, Quarter), N("d", 72, 3 * Quarter, Quarter)];
        MotifFact texture = MotifAnalyzer.AnalyzeCharacter(line, MeterMap.Default, All(line), null, s_context).Single(fact => fact.Id == "character.texture");
        Assert.Equal(FactCertainty.Detected, texture.Certainty);
        Assert.StartsWith("Texture: one line with no accompaniment.", texture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hand_alone_says_nothing_of_the_texture()
    {
        Note[] notes = [N("r", 72, 0, Bar, Hand.Right), N("l1", 48, 0, Bar / 2, Hand.Left), N("l2", 55, Bar / 2, Bar / 2, Hand.Left)];
        MotifSelection right = MotifSelector.Select(notes, MeterMap.Default, TickSpan.FromBounds(Tick.Zero, new Tick(Bar)), SelectionFilter.ForHand(Hand.Right));
        Assert.DoesNotContain(MotifAnalyzer.AnalyzeCharacter(notes, MeterMap.Default, right, null, s_context), fact => fact.Id == "character.texture");
    }

    [Fact]
    public void Main_leads_with_the_harmonic_motion_then_the_texture_then_another_lens()
    {
        ImportedPiece piece = FurElise();
        MotifSelection selection = Bars(piece, 1, 8);
        KeySignature? key = KeySignature.InForce(piece.Keys, selection.Span.Onset);
        MotifAnalysis motif = MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, key);
        MotifFact[] all =
        [
            .. motif.Facts,
            .. MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, key, CharacterContext.FromPiece(piece)),
            .. HarmonyAnalyzer.Analyze(piece.Notes, piece.Meter, selection, motif.Key?.Key).Facts,
        ];

        IReadOnlyList<MotifFact> main = FactRanking.Main(all);
        Assert.Equal(3, main.Count);
        Assert.Equal(["harmony.motion", "character.texture"], main.Take(2).Select(fact => fact.Id));
        Assert.DoesNotContain(main[2].Lens, new[] { FactLens.Harmony, FactLens.Character });
        Assert.Equal(["character.texture"], FactRanking.Main([.. all.Where(fact => fact.Id != "harmony.motion")], 1).Select(fact => fact.Id));
        Assert.Empty(FactRanking.Main(all, 0));
    }
}