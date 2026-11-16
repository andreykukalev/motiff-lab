using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>Beat grouping, syncopation, accents and repeated rhythms (S1-A1).</summary>
public class RhythmFactTests
{
    private const long Quarter = 480;
    private const long Eighth = 240;
    private const long Sixteenth = 120;

    // Für Elise in 3/8 with a two-sixteenth pickup: bar 0 is [0, 240), bar 1 starts at 240.
    private static readonly MeterMap s_threeEight = new([new MeterChange(Tick.Zero, new TimeSignature(3, 8))], pickupDuration: Eighth);

    private static readonly KeySignature s_aMinor = new(Tick.Zero, 0, KeyMode.Minor);

    private static Note Note(string id, int pitch, long onset, long duration) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    /// <summary>E5 D♯5 | E5 D♯5 E5 B4 D5 C5 | A4 (eighth), then a sixteenth rest, then C4 E4 A4.</summary>
    private static Note[] FurElise()
    {
        int[] pitches = [76, 75, 76, 75, 76, 71, 74, 72];
        var notes = pitches.Select((pitch, i) => Note($"n{i + 1}", pitch, i * Sixteenth, Sixteenth)).ToList();
        notes.Add(Note("n9", 69, 8 * Sixteenth, Eighth));
        notes.Add(Note("n10", 60, 11 * Sixteenth, Sixteenth));
        notes.Add(Note("n11", 64, 12 * Sixteenth, Sixteenth));
        notes.Add(Note("n12", 69, 13 * Sixteenth, Sixteenth));
        return [.. notes];
    }

    /// <summary>Notes in 4/4 from (onset, length) pairs, all on C4.</summary>
    private static Note[] Rhythm(params (long Onset, long Length)[] notes) =>
        [.. notes.Select((note, i) => Note($"r{i + 1}", 60, note.Onset, note.Length))];

    private static MotifAnalysis Analyze(Note[] notes, MeterMap meter, long from, long to, KeySignature? signature = null, IEnumerable<AccentMark>? accents = null) =>
        MotifAnalyzer.Analyze(notes, meter, MotifSelector.Select(notes, meter, TickSpan.FromBounds(new Tick(from), new Tick(to)), SelectionFilter.All), signature, accents);

    private static MotifFact Fact(MotifAnalysis analysis, string id) => Assert.Single(analysis.Facts, fact => fact.Id == id);

    /// <summary>Each place of a fact as "label: note ids", or the label alone for a stretch of time.</summary>
    private static string[] Places(MotifFact fact) =>
        [.. fact.Places.Select(place => place.NoteIds.Count == 0 ? place.Label : $"{place.Label}: {string.Join(" ", place.NoteIds)}")];

    [Fact]
    public void Onsets_are_counted_per_beat_bar_by_bar()
    {
        MotifAnalysis analysis = Analyze(FurElise(), s_threeEight, 0, 14 * Sixteenth, s_aMinor);

        MotifFact grouping = Fact(analysis, "rhythm.grouping");
        Assert.Equal("Onsets per beat, bar by bar: 2 | 2 2 2 | 1 1 2. Most beats split in two (5 of 7).", grouping.Text);
        Assert.Equal(FactCertainty.Detected, grouping.Certainty);
    }

    [Fact]
    public void Even_beats_are_named_and_silent_beats_count_zero()
    {
        MotifAnalysis even = Analyze(Rhythm((0, Quarter), (Quarter, Quarter), (2 * Quarter, Quarter), (3 * Quarter, Quarter)), MeterMap.Default, 0, 4 * Quarter);
        MotifAnalysis gap = Analyze(Rhythm((0, Quarter), (3 * Quarter, Eighth), (3 * Quarter + Eighth, Eighth)), MeterMap.Default, 0, 4 * Quarter);

        Assert.Equal("Onsets per beat, bar by bar: 1 1 1 1. Every beat holds one onset.", Fact(even, "rhythm.grouping").Text);
        Assert.Equal("Onsets per beat, bar by bar: 1 0 0 2.", Fact(gap, "rhythm.grouping").Text);
    }

    [Fact]
    public void Off_beat_notes_held_over_a_beat_and_weak_beats_held_over_the_bar_line_are_syncopations()
    {
        // Eighth, quarter from the off-beat, eighth, quarter, then beat 4 held into the next bar.
        Note[] notes = Rhythm((0, Eighth), (Eighth, Quarter), (3 * Eighth, Eighth), (2 * Quarter, Quarter), (3 * Quarter, 2 * Quarter), (5 * Quarter, 3 * Quarter));

        MotifFact syncopation = Fact(Analyze(notes, MeterMap.Default, 0, 8 * Quarter), "rhythm.syncopation");

        Assert.Equal("2 syncopations: bar 1 after beat 1, held into beat 2; bar 1 beat 4, held across the bar line.", syncopation.Text);
        Assert.Equal(FactCertainty.Detected, syncopation.Certainty);
        Assert.Equal(["bar 1 after beat 1, held into beat 2: r2", "bar 1 beat 4, held across the bar line: r5"], Places(syncopation));
    }

    [Fact]
    public void Lengths_silences_and_the_start_name_their_places()
    {
        MotifAnalysis analysis = Analyze(FurElise(), s_threeEight, 0, 14 * Sixteenth, s_aMinor);

        Assert.Equal("Lengths: 11 sixteenths, 1 eighth.", Fact(analysis, "rhythm.durations").Text);
        Assert.Equal(["sixteenths: n1 n2 n3 n4 n5 n6 n7 n8 n10 n11 n12", "eighth: n9"], Places(Fact(analysis, "rhythm.durations")));
        MotifFact silences = Fact(analysis, "rhythm.silences");
        Assert.Equal(["silence, a sixteenth"], Places(silences));
        Assert.Equal(new TickSpan(new Tick(10 * Sixteenth), Sixteenth), silences.Places[0].Span);
        Assert.Equal(["start, bar 0 beat 3: n1"], Places(Fact(analysis, "rhythm.grid")));
        Assert.Empty(Fact(analysis, "rhythm.grouping").Places);
        Assert.Empty(Fact(analysis, "rhythm.syncopation").Places);
    }

    [Fact]
    public void A_selection_that_opens_with_silence_marks_it()
    {
        Note[] notes = Rhythm((Quarter, Quarter), (2 * Quarter, Quarter));

        MotifFact silences = Fact(Analyze(notes, MeterMap.Default, 0, 4 * Quarter), "rhythm.silences");

        Assert.Equal("No silence between notes; the selection opens with a quarter of silence.", silences.Text);
        Assert.Equal(["opening silence, a quarter"], Places(silences));
        Assert.Equal(new TickSpan(Tick.Zero, Quarter), silences.Places[0].Span);
    }

    [Fact]
    public void A_rhythm_without_syncopation_says_so()
    {
        MotifFact syncopation = Fact(Analyze(FurElise(), s_threeEight, 0, 14 * Sixteenth, s_aMinor), "rhythm.syncopation");

        Assert.StartsWith("No syncopation", syncopation.Text);
    }

    [Fact]
    public void Repeated_bar_rhythms_and_beat_figures_are_found()
    {
        // Bars 1, 2 and 4: quarter, two eighths, half. Bar 3: four quarters.
        (long, long)[] bar = [(0, Quarter), (Quarter, Eighth), (Quarter + Eighth, Eighth), (2 * Quarter, 2 * Quarter)];
        (long, long)[] quarters = [(0, Quarter), (Quarter, Quarter), (2 * Quarter, Quarter), (3 * Quarter, Quarter)];
        Note[] notes = Rhythm([.. new[] { bar, bar, quarters, bar }.SelectMany((pattern, i) => pattern.Select(note => (note.Item1 + (i * 4 * Quarter), note.Item2)))]);

        MotifFact patterns = Fact(Analyze(notes, MeterMap.Default, 0, 16 * Quarter), "rhythm.patterns");

        Assert.Equal("Bars 1, 2 and 4 have the same rhythm. The figure eighth–eighth fills 3 beats.", patterns.Text);
        Assert.Equal(
            ["bar 1", "bar 2", "bar 4", "eighth–eighth, bar 1 beat 2", "eighth–eighth, bar 2 beat 2", "eighth–eighth, bar 4 beat 2"],
            patterns.Places.Select(place => place.Label));
        Assert.Equal(new TickSpan(new Tick(12 * Quarter), 4 * Quarter), patterns.Places[2].Span);
        Assert.Equal(["r2", "r3"], patterns.Places[3].NoteIds);
    }

    [Fact]
    public void Figures_count_rests_and_the_most_frequent_figure_is_named()
    {
        MotifFact patterns = Fact(Analyze(FurElise(), s_threeEight, 0, 14 * Sixteenth, s_aMinor), "rhythm.patterns");

        Assert.Equal("The figure sixteenth–sixteenth fills 5 beats.", patterns.Text);
    }

    [Fact]
    public void Plain_quarters_have_no_repeated_figure()
    {
        MotifFact patterns = Fact(Analyze(Rhythm((0, Quarter), (Quarter, Quarter), (2 * Quarter, Quarter), (3 * Quarter, Quarter)), MeterMap.Default, 0, 4 * Quarter), "rhythm.patterns");

        Assert.Equal("No bar rhythm or beat figure repeats within the motif.", patterns.Text);
    }

    [Fact]
    public void Accents_are_listed_only_when_the_source_can_mark_them()
    {
        Note[] notes = FurElise();

        MotifAnalysis midi = Analyze(notes, s_threeEight, 0, 14 * Sixteenth, s_aMinor);
        MotifAnalysis none = Analyze(notes, s_threeEight, 0, 14 * Sixteenth, s_aMinor, accents: []);
        MotifAnalysis marked = Analyze(
            notes,
            s_threeEight,
            0,
            14 * Sixteenth,
            s_aMinor,
            accents: [new AccentMark("n6", AccentKind.Accent), new AccentMark("n1", AccentKind.Accent), new AccentMark("n1", AccentKind.StrongAccent), new AccentMark("elsewhere", AccentKind.Accent)]);

        Assert.DoesNotContain(midi.Facts, fact => fact.Id == "rhythm.accents");
        Assert.Equal("No accents are marked on the motif's notes.", Fact(none, "rhythm.accents").Text);
        MotifFact accents = Fact(marked, "rhythm.accents");
        Assert.Equal("Accents marked on 2 of 12 notes: E5 at bar 0 beat 3 (strong accent); B4 at bar 1 after beat 2.", accents.Text);
        Assert.Equal(["E5 at bar 0 beat 3 (strong accent): n1", "B4 at bar 1 after beat 2: n6"], Places(accents));
        Assert.Equal(FactCertainty.Detected, accents.Certainty);
    }

    [Fact]
    public void A_performance_off_the_grid_makes_the_new_rhythm_facts_uncertain()
    {
        Note[] notes = [Note("a", 60, 0, 470), Note("b", 62, 487, 455), Note("c", 64, 960, 480)];

        MotifAnalysis analysis = Analyze(notes, MeterMap.Default, 0, 4 * Quarter);

        Assert.All(
            new[] { "rhythm.grouping", "rhythm.syncopation", "rhythm.patterns" },
            id => Assert.Equal(FactCertainty.Uncertain, Fact(analysis, id).Certainty));
        Assert.Contains("played performance", Fact(analysis, "rhythm.grouping").Basis);
    }

    [Fact]
    public void Rhythm_facts_are_deterministic_and_ids_stay_unique()
    {
        Note[] notes = FurElise();
        AccentMark[] accents = [new("n1", AccentKind.Accent)];

        MotifAnalysis first = Analyze(notes, s_threeEight, 0, 14 * Sixteenth, s_aMinor, accents);
        MotifAnalysis second = Analyze([.. notes.Reverse()], s_threeEight, 0, 14 * Sixteenth, s_aMinor, accents);

        Assert.Equal(first.Facts, second.Facts);
        Assert.Equal(first.Facts.Count, first.Facts.Select(fact => fact.Id).Distinct().Count());
    }
}