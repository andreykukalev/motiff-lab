using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>Chords and bass where notes sound together (S1-A3, #46): labels with inversion and alternatives, all inferred.</summary>
public class HarmonyAnalyzerTests
{
    private const long Quarter = 480;
    private const long Bar = 4 * Quarter;

    private static readonly MusicalKey s_cMajor = new(0, KeyMode.Major);

    private static Note N(string id, int pitch, long onset, long duration, int track = 0) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = track,
        Attribution = Attribution.Imported,
    };

    /// <summary>C – F/C – G7/B – C in whole-bar block chords under a melody: a plain cadence.</summary>
    public static Note[] Cadence() =>
    [
        N("c1", 48, 0, Bar), N("c2", 55, 0, Bar), N("c3", 64, 0, Bar), N("m1", 72, 0, Bar),
        N("f1", 48, Bar, Bar), N("f2", 57, Bar, Bar), N("f3", 65, Bar, Bar), N("m2", 72, Bar, Bar),
        N("g1", 47, 2 * Bar, Bar), N("g2", 53, 2 * Bar, Bar), N("g3", 55, 2 * Bar, Bar), N("m3", 74, 2 * Bar, Bar),
        N("e1", 48, 3 * Bar, Bar), N("e2", 55, 3 * Bar, Bar), N("e3", 64, 3 * Bar, Bar), N("m4", 72, 3 * Bar, Bar),
    ];

    private static MotifSelection All(Note[] notes) => MotifSelector.Select(notes, MeterMap.Default, TickSpan.FromBounds(Tick.Zero, new Tick(notes.Max(note => note.Span.End.Value))), SelectionFilter.All);

    [Fact]
    public void A_cadence_reads_as_its_chords_with_inversions()
    {
        Note[] notes = Cadence();
        IReadOnlyList<ChordSpan> chords = HarmonyAnalyzer.FindChords(notes, MeterMap.Default, All(notes).Span);

        Assert.Equal(["C", "F/C", "G7/B", "C"], chords.Select(chord => chord.Symbol(s_cMajor)));
        Assert.Equal([0, 2, 1, 0], chords.Select(chord => chord.Inversion));
        Assert.Equal([0, Bar, 2 * Bar, 3 * Bar], chords.Select(chord => chord.Span.Onset.Value));
    }

    [Fact]
    public void Fur_elise_opening_reads_as_a_minor_and_e_major()
    {
        ImportedPiece piece = PieceImporter.Import("fur-elise.musicxml", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", "fur-elise-original.musicxml"))).Piece!;
        TickSpan span = TickSpan.FromBounds(piece.Meter.GetBar(1).Span.Onset, piece.Meter.GetBar(8).Span.End);
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, span, SelectionFilter.All);
        HarmonyAnalysis harmony = HarmonyAnalyzer.Analyze(piece.Notes, piece.Meter, selection, new MusicalKey(0, KeyMode.Minor));

        MotifFact chords = harmony.Facts.Single(fact => fact.Id == "harmony.chords");
        Assert.Equal(FactLens.Harmony, chords.Lens);
        Assert.Equal("6 chords read from the notes: Am from bar 2, E from bar 3, Am from bar 4, Am from bar 6, E from bar 7, Am from bar 8.", chords.Text);
        Assert.Equal(["Am", "E", "Am", "Am", "E", "Am"], harmony.Chords.Select(chord => chord.Symbol(new MusicalKey(0, KeyMode.Minor))));
        Assert.Contains("A or Asus4", chords.Basis, StringComparison.Ordinal);
        Assert.Equal(FactCertainty.Uncertain, chords.Certainty);
        Assert.Equal("The chord changes 4 times in 8 bars: about every 2 bars.", harmony.Facts.Single(fact => fact.Id == "harmony.rhythm").Text);
        Assert.StartsWith("The bass plays 6 notes: A2 E2 A2 A2 E2 A2.", harmony.Facts.Single(fact => fact.Id == "harmony.bass").Text, StringComparison.Ordinal);
        Assert.Contains("Am–E: E", harmony.Facts.Single(fact => fact.Id == "harmony.shared").Text, StringComparison.Ordinal);

        HarmonyAnalysis russian = HarmonyAnalyzer.Analyze(piece.Notes, piece.Meter, selection, new MusicalKey(0, KeyMode.Minor), Words.Russian);
        Assert.Equal(harmony.Facts.Select(fact => fact.Id), russian.Facts.Select(fact => fact.Id));
        Assert.All(russian.Facts, fact => Assert.Matches("[А-Яа-я]", fact.Text));
    }

    [Fact]
    public void A_single_line_has_no_chords()
    {
        Note[] line = [N("a", 60, 0, Quarter), N("b", 64, Quarter, Quarter), N("c", 67, 2 * Quarter, Quarter), N("d", 72, 3 * Quarter, Quarter)];

        Assert.Empty(HarmonyAnalyzer.FindChords(line, MeterMap.Default, All(line).Span, s_cMajor));
        Assert.Empty(HarmonyAnalyzer.Analyze(line, MeterMap.Default, All(line), s_cMajor).Facts);
    }

    [Fact]
    public void Finding_chords_is_deterministic_and_marks_its_facts_inferred_or_uncertain()
    {
        Note[] notes = Cadence();
        HarmonyAnalysis first = HarmonyAnalyzer.Analyze(notes, MeterMap.Default, All(notes), s_cMajor);
        HarmonyAnalysis second = HarmonyAnalyzer.Analyze([.. notes.Reverse()], MeterMap.Default, All(notes), s_cMajor);

        Assert.Equal(first.Facts.Select(fact => fact.Text), second.Facts.Select(fact => fact.Text));
        Assert.All(first.Facts, fact => Assert.Equal(FactLens.Harmony, fact.Lens));
        Assert.All(first.Facts.Where(fact => fact.Id is not ("harmony.voicing" or "harmony.chords")), fact => Assert.Equal(FactCertainty.Inferred, fact.Certainty));

        // G7 over B has a near rival, so the chords fact is uncertain and names it.
        IReadOnlyList<ChordSpan> chords = HarmonyAnalyzer.FindChords(notes, MeterMap.Default, All(notes).Span, s_cMajor);
        Assert.True(chords[0].Confidence >= HarmonyAnalyzer.ClearConfidence);
        Assert.Equal(FactCertainty.Uncertain, first.Facts.Single(fact => fact.Id == "harmony.chords").Certainty);
        Assert.True(chords[2].Confidence < HarmonyAnalyzer.ClearConfidence);
        Assert.NotEmpty(chords[2].Alternatives);
        Assert.Contains("G7/B from bar 3: ", first.Facts.Single(fact => fact.Id == "harmony.chords").Basis, StringComparison.Ordinal);
    }
}