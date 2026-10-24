using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>Recurrences, phrase spans and new material (S1-A4).</summary>
public class StructureAnalyzerTests
{
    private const long Quarter = 480;
    private const long Bar = 4 * Quarter;

    private static readonly MeterMap s_meter = MeterMap.Default;

    private static Note Note(string id, int pitch, long onset, long duration, int track = 0) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = track,
        Attribution = Attribution.Imported,
    };

    /// <summary>Quarter notes in 4/4, one array per bar; a 0 pitch is a rest.</summary>
    private static Note[] Piece(params int[][] bars) =>
        [.. bars.SelectMany((pitches, bar) => pitches
            .Select((pitch, beat) => (pitch, beat))
            .Where(entry => entry.pitch > 0)
            .Select(entry => Note($"b{bar + 1}.{entry.beat + 1}", entry.pitch, (bar * Bar) + (entry.beat * Quarter), Quarter)))];

    /// <summary>C D E C | C D E C | E F G G | F G A F | C D E C: bar 2 and 5 repeat bar 1, bar 4 is bar 1 a 4th higher.</summary>
    private static Note[] Song() => Piece([60, 62, 64, 60], [60, 62, 64, 60], [64, 65, 67, 67], [65, 67, 69, 65], [60, 62, 64, 60]);

    private static StructureAnalysis Analyze(Note[] notes, int firstBar, int lastBar, IEnumerable<PhraseBoundary>? phrases = null, SelectionFilter? filter = null) =>
        StructureAnalyzer.Analyze(
            notes,
            s_meter,
            MotifSelector.Select(notes, s_meter, TickSpan.FromBounds(new Tick((firstBar - 1) * Bar), new Tick(lastBar * Bar)), filter ?? SelectionFilter.All),
            phrases ?? []);

    private static MotifFact Fact(StructureAnalysis analysis, string id) => Assert.Single(analysis.Facts, fact => fact.Id == id);

    [Fact]
    public void Exact_and_transposed_recurrences_are_found()
    {
        StructureAnalysis analysis = Analyze(Song(), 1, 1);

        Assert.Equal([(2, 0), (4, 5), (5, 0)], analysis.Recurrences.Select(recurrence => (recurrence.FirstBar, recurrence.Transposition)));
        Assert.Equal(["b4.1", "b4.2", "b4.3", "b4.4"], analysis.Recurrences[1].NoteIds);
        MotifFact fact = Fact(analysis, "structure.recurrences");
        Assert.Equal("The motif recurs 3 times (2 exact, 1 transposed): exactly in bar 2, a perfect 4th higher in bar 4 and exactly in bar 5.", fact.Text);
        Assert.Equal(FactCertainty.Detected, fact.Certainty);
        Assert.Equal(FactLens.Structure, fact.Lens);
    }

    [Fact]
    public void A_motif_heard_earlier_says_where_it_is_first_heard()
    {
        MotifFact fact = Fact(Analyze(Song(), 2, 2), "structure.recurrences");

        Assert.Equal(
            "The motif recurs 3 times (2 exact, 1 transposed): exactly in bar 1, a perfect 4th higher in bar 4 and exactly in bar 5. It is first heard in bar 1.",
            fact.Text);
    }

    [Fact]
    public void A_motif_heard_once_or_too_short_says_so()
    {
        Note[] song = Song();
        Note[] two = Piece([60, 62, 0, 0], [60, 62, 0, 0]);

        Assert.Equal("The motif does not recur elsewhere, exactly or transposed.", Fact(Analyze(song, 3, 3), "structure.recurrences").Text);
        StructureAnalysis tooShort = Analyze(two, 1, 1);
        Assert.Empty(tooShort.Recurrences);
        Assert.Equal("With 2 notes in its line, the motif is too short to search for recurrences; it takes 3.", Fact(tooShort, "structure.recurrences").Text);
    }

    [Fact]
    public void Rhythm_must_match_but_lengths_need_not()
    {
        Note[] notes =
        [
            .. Piece([60, 62, 64, 60]),
            Note("short1", 60, Bar, Quarter / 2), Note("short2", 62, Bar + Quarter, Quarter / 2), Note("short3", 64, Bar + (2 * Quarter), Quarter / 2), Note("short4", 60, Bar + (3 * Quarter), Quarter / 2),
            Note("late1", 60, 2 * Bar, Quarter), Note("late2", 62, (2 * Bar) + Quarter, Quarter), Note("late3", 64, (2 * Bar) + (3 * Quarter), Quarter), Note("late4", 60, 3 * Bar, Quarter),
        ];

        StructureAnalysis analysis = Analyze(notes, 1, 1);

        Assert.Equal(["short1", "short2", "short3", "short4"], Assert.Single(analysis.Recurrences).NoteIds);
    }

    [Fact]
    public void Recurrences_do_not_overlap_the_motif_or_each_other()
    {
        Note[] notes = Piece([60, 60, 60, 60], [60, 60, 60, 60], [60, 60, 60, 60]);

        StructureAnalysis analysis = Analyze(notes, 1, 1);

        Assert.Equal([2, 3], analysis.Recurrences.Select(recurrence => recurrence.FirstBar));
    }

    [Fact]
    public void Only_notes_that_pass_the_filter_are_searched()
    {
        Note[] notes = [.. Song(), .. Piece([60, 62, 64, 60]).Select(note => note with { Id = "lh" + note.Id, Track = 1, Span = new TickSpan(note.Span.Onset.Offset(5 * Bar), note.Span.Duration) })];

        StructureAnalysis all = Analyze(notes, 1, 1);
        StructureAnalysis track = Analyze(notes, 1, 1, filter: SelectionFilter.ForTrack(0));

        Assert.Equal(4, all.Recurrences.Count);
        Assert.Equal(3, track.Recurrences.Count);
    }

    [Fact]
    public void New_material_is_every_bar_that_repeats_no_earlier_bar()
    {
        StructureAnalysis analysis = Analyze(Song(), 1, 1);

        Assert.Equal([1, 3], analysis.NewMaterialBars);
        Assert.Equal("New material in bars 1 and 3; the other 3 bars repeat an earlier bar, exactly or transposed.", Fact(analysis, "structure.new-material").Text);
        Assert.Equal(
            "Every bar with notes brings new material (2 bars); none repeats an earlier bar.",
            Fact(Analyze(Piece([60, 62, 64, 60], [67, 65, 64, 62]), 1, 1), "structure.new-material").Text);
    }

    [Fact]
    public void Phrase_spans_run_between_boundaries_and_name_the_motif_s_phrase()
    {
        PhraseBoundary[] slur = [new(new Tick(2 * Bar), [PhraseEvidence.Slur], Attribution.Imported)];
        PhraseBoundary[] rest = [new(new Tick(2 * Bar), [PhraseEvidence.Rest], Attribution.Inferred(PhraseBoundaryFinder.RestConfidence))];

        StructureAnalysis marked = Analyze(Song(), 4, 4, slur);
        MotifFact fromRest = Fact(Analyze(Song(), 1, 1, rest), "structure.phrases");
        MotifFact none = Fact(Analyze(Song(), 1, 1), "structure.phrases");

        Assert.Equal([(1, 2), (3, 5)], marked.Phrases.Select(phrase => (phrase.FirstBar, phrase.LastBar)));
        MotifFact phrases = Fact(marked, "structure.phrases");
        Assert.Equal("2 phrases: bars 1–2, bars 3–5 (2 and 3 bars). The motif starts in phrase 2.", phrases.Text);
        Assert.Equal(FactCertainty.Detected, phrases.Certainty);
        Assert.Equal(FactCertainty.Inferred, fromRest.Certainty);
        Assert.Equal("No phrase boundary is marked, so the notes read as one span, bars 1–5.", none.Text);
    }

    [Fact]
    public void Chords_make_the_line_an_inference()
    {
        Note[] notes = [.. Song(), Note("low", 48, 0, Quarter)];

        StructureAnalysis analysis = Analyze(notes, 1, 1);

        Assert.All(new[] { "structure.recurrences", "structure.new-material" }, id => Assert.Equal(FactCertainty.Inferred, Fact(analysis, id).Certainty));
        Assert.Equal(3, analysis.Recurrences.Count);
    }

    [Fact]
    public void An_empty_selection_has_no_structure_and_analysis_is_deterministic()
    {
        Note[] notes = Song();
        PhraseBoundary[] phrases = [new(new Tick(2 * Bar), [PhraseEvidence.Slur], Attribution.Imported)];

        Assert.Empty(Analyze(notes, 6, 6).Facts);
        StructureAnalysis first = Analyze(notes, 1, 1, phrases);
        StructureAnalysis second = Analyze([.. notes.Reverse()], 1, 1, phrases);
        Assert.Equal(first.Facts, second.Facts);
        Assert.Equal(first.Recurrences.Select(recurrence => recurrence.NoteIds), second.Recurrences.Select(recurrence => recurrence.NoteIds));
    }
}