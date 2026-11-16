using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

public class MotifAnalyzerTests
{
    private const long Quarter = 480;
    private const long Eighth = 240;
    private const long Sixteenth = 120;

    // Für Elise in 3/8 with a two-sixteenth pickup: bar 0 is [0, 240), bar 1 starts at 240.
    private static readonly MeterMap s_threeEight = new([new MeterChange(Tick.Zero, new TimeSignature(3, 8))], pickupDuration: Eighth);

    private static Note Note(string id, int pitch, long onset, long duration, Hand? hand = null, bool handInferred = false) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Hand = hand is { } value ? new Attributed<Hand>(value, handInferred ? Attribution.Inferred(0.8) : Attribution.Imported) : null,
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

    private static MotifSelection Select(IEnumerable<Note> notes, MeterMap meter, long from, long to, SelectionFilter? filter = null) =>
        MotifSelector.Select(notes, meter, TickSpan.FromBounds(new Tick(from), new Tick(to)), filter ?? SelectionFilter.All);

    private static MotifFact Fact(MotifAnalysis analysis, string id) => Assert.Single(analysis.Facts, fact => fact.Id == id);

    [Fact]
    public void A_single_line_gives_detected_intervals_and_contour()
    {
        Note[] notes = FurElise();
        MotifSelection selection = Select(notes, s_threeEight, 0, 9 * Sixteenth);

        MotifAnalysis analysis = MotifAnalyzer.Analyze(notes, s_threeEight, selection, new KeySignature(Tick.Zero, 0, KeyMode.Minor));

        Assert.False(analysis.LineIsTopNotes);
        Assert.Equal([-1, 1, -1, 1, -5, 3, -2, -3], analysis.Intervals);
        Assert.Equal(ContourStep.Down, analysis.Contour[0]);
        Assert.Equal("9 notes in the melody: E5 D♯5 E5 D♯5 E5 B4 D5 C5 A4.", Fact(analysis, "melody.line").Text);
        Assert.Equal(
            "Intervals in semitones: −1 +1 −1 +1 −5 +3 −2 −3. 5 steps, 3 leaps. Largest leap: a perfect 4th down.",
            Fact(analysis, "melody.intervals").Text);
        Assert.Equal("Zigzags, turning 6 times; ends a perfect 5th lower than it starts.", Fact(analysis, "melody.contour").Text);
        Assert.Equal("Range: A4 to E5, a perfect 5th (7 semitones).", Fact(analysis, "melody.range").Text);
        Assert.All(analysis.Facts.Where(fact => fact.Lens == FactLens.Melody), fact => Assert.Equal(FactCertainty.Detected, fact.Certainty));
    }

    [Fact]
    public void A_stated_key_is_detected_and_gives_scale_degrees()
    {
        Note[] notes = FurElise();
        MotifSelection selection = Select(notes, s_threeEight, 0, 9 * Sixteenth);

        MotifAnalysis analysis = MotifAnalyzer.Analyze(notes, s_threeEight, selection, new KeySignature(Tick.Zero, 0, KeyMode.Minor));

        Assert.Equal(new KeyReading(new MusicalKey(0, KeyMode.Minor), FactCertainty.Detected, null), analysis.Key);
        MotifFact key = Fact(analysis, "key");
        Assert.Equal("Key: A minor.", key.Text);
        Assert.Equal(FactCertainty.Detected, key.Certainty);
        Assert.Equal("Scale degrees in A minor: 5 ♯4 5 ♯4 5 2 4 3 1.", Fact(analysis, "melody.degrees").Text);
        Assert.Equal(FactCertainty.Detected, Fact(analysis, "melody.degrees").Certainty);
    }

    [Fact]
    public void A_signature_without_a_mode_picks_major_or_minor_from_the_notes()
    {
        Note[] notes = FurElise();
        MotifSelection selection = Select(notes, s_threeEight, 0, 14 * Sixteenth);

        MotifAnalysis analysis = MotifAnalyzer.Analyze(notes, s_threeEight, selection, new KeySignature(Tick.Zero, 0, Mode: null));

        Assert.Equal(new MusicalKey(0, KeyMode.Minor), analysis.Key!.Key);
        Assert.Equal(new MusicalKey(0, KeyMode.Major), analysis.Key.RunnerUp);
        Assert.Equal(FactCertainty.Inferred, analysis.Key.Certainty);
        Assert.Contains("better than C major", Fact(analysis, "key").Basis);
        Assert.Equal(FactCertainty.Inferred, Fact(analysis, "melody.degrees").Certainty);
    }

    [Fact]
    public void Without_a_signature_the_key_is_inferred_from_the_notes()
    {
        // G major scale up and down, with the tonic held at both ends.
        int[] pitches = [67, 69, 71, 72, 74, 76, 78, 79, 74, 71, 67];
        Note[] notes = [.. pitches.Select((pitch, i) => Note($"n{i}", pitch, i * Quarter, i is 0 or 7 or 10 ? Quarter * 2 : Quarter))];
        MeterMap meter = MeterMap.Default;
        MotifSelection selection = Select(notes, meter, 0, 4 * 4 * Quarter);

        MotifAnalysis analysis = MotifAnalyzer.Analyze(notes, meter, selection, signature: null);

        Assert.Equal(new MusicalKey(1, KeyMode.Major), analysis.Key!.Key);
        Assert.Equal(FactCertainty.Inferred, analysis.Key.Certainty);
        Assert.Equal("Key: G major.", Fact(analysis, "key").Text);
        Assert.StartsWith("The file has no key signature; the notes fit G major best", Fact(analysis, "key").Basis);
        Assert.Contains("F♯5", Fact(analysis, "melody.line").Text);
    }

    [Fact]
    public void A_key_from_a_few_notes_is_uncertain()
    {
        // Ode to Joy, first bar: E E F G.
        Note[] notes = [Note("a", 64, 0, Quarter), Note("b", 64, Quarter, Quarter), Note("c", 65, 2 * Quarter, Quarter), Note("d", 67, 3 * Quarter, Quarter)];
        MotifSelection selection = Select(notes, MeterMap.Default, 0, 4 * Quarter);

        MotifAnalysis analysis = MotifAnalyzer.Analyze(notes, MeterMap.Default, selection, signature: null);

        MotifFact key = Fact(analysis, "key");
        Assert.Equal(FactCertainty.Uncertain, key.Certainty);
        Assert.Contains("4 notes and 3 pitch classes, that is a weak guess", key.Basis);
        Assert.Equal(FactCertainty.Uncertain, Fact(analysis, "melody.degrees").Certainty);
        Assert.Equal("Intervals in semitones: 0 +1 +2. 2 steps, 1 repeated note.", Fact(analysis, "melody.intervals").Text);
        Assert.Equal("Rises; ends a minor 3rd higher than it starts.", Fact(analysis, "melody.contour").Text);
    }

    [Fact]
    public void Notes_sounding_together_make_the_top_line_an_inference()
    {
        Note[] notes =
        [
            Note("r1", 72, 0, Quarter), Note("l1", 48, 0, 2 * Quarter),
            Note("r2", 76, Quarter, Quarter),
            Note("r3", 79, 2 * Quarter, Quarter), Note("l2", 55, 2 * Quarter, Quarter),
        ];
        MotifSelection selection = Select(notes, MeterMap.Default, 0, 4 * Quarter);

        MotifAnalysis analysis = MotifAnalyzer.Analyze(notes, MeterMap.Default, selection, new KeySignature(Tick.Zero, 0, KeyMode.Major));

        Assert.True(analysis.LineIsTopNotes);
        Assert.Equal(["r1", "r2", "r3"], analysis.Line.Select(note => note.Id));
        Assert.Equal([4, 3], analysis.Intervals);
        MotifFact line = Fact(analysis, "melody.line");
        Assert.Equal(FactCertainty.Inferred, line.Certainty);
        Assert.StartsWith("3 notes in the top line", line.Text);
        Assert.Equal(FactCertainty.Inferred, Fact(analysis, "melody.contour").Certainty);
        Assert.Equal("Range: C3 to G5, 2 octaves and a perfect 5th (31 semitones).", Fact(analysis, "melody.range").Text);
        Assert.Equal(FactCertainty.Detected, Fact(analysis, "melody.range").Certainty);
    }

    [Fact]
    public void Rhythm_facts_name_note_values_the_grid_and_silences()
    {
        Note[] notes = FurElise();
        MotifSelection selection = Select(notes, s_threeEight, 0, 14 * Sixteenth);

        MotifAnalysis analysis = MotifAnalyzer.Analyze(notes, s_threeEight, selection, new KeySignature(Tick.Zero, 0, KeyMode.Minor));

        Assert.Equal("Lengths: 11 sixteenths, 1 eighth.", Fact(analysis, "rhythm.durations").Text);
        MotifFact grid = Fact(analysis, "rhythm.grid");
        Assert.Equal(FactCertainty.Detected, grid.Certainty);
        Assert.Equal("Onsets fit the sixteenth-note grid. 6 of 12 onsets fall on a beat. It starts on beat 3 of bar 0.", grid.Text);
        Assert.Equal("1 silence between notes: a sixteenth.", Fact(analysis, "rhythm.silences").Text);
    }

    [Fact]
    public void A_leading_silence_and_triplets_are_reported()
    {
        Note[] notes = [Note("a", 60, Quarter, 160), Note("b", 62, Quarter + 160, 160), Note("c", 64, Quarter + 320, 160)];
        MotifSelection selection = Select(notes, MeterMap.Default, 0, 4 * Quarter);

        MotifAnalysis analysis = MotifAnalyzer.Analyze(notes, MeterMap.Default, selection, null);

        Assert.Equal("Lengths: 3 eighth-note triplets.", Fact(analysis, "rhythm.durations").Text);
        Assert.StartsWith("Onsets fit the eighth-note triplet grid. 1 of 3 onsets falls on a beat. It starts on beat 2 of bar 1.", Fact(analysis, "rhythm.grid").Text);
        Assert.Equal("No silence between notes; the selection opens with a quarter of silence.", Fact(analysis, "rhythm.silences").Text);
    }

    [Fact]
    public void Onsets_off_every_grid_make_the_rhythm_uncertain()
    {
        Note[] notes = [Note("a", 60, 0, 470), Note("b", 62, 487, 455), Note("c", 64, 960, 480)];
        MotifSelection selection = Select(notes, MeterMap.Default, 0, 4 * Quarter);

        MotifAnalysis analysis = MotifAnalyzer.Analyze(notes, MeterMap.Default, selection, null);

        MotifFact grid = Fact(analysis, "rhythm.grid");
        Assert.Equal(FactCertainty.Uncertain, grid.Certainty);
        Assert.StartsWith("1 of 3 onsets falls between thirty-second and triplet grid lines.", grid.Text);
        MotifFact durations = Fact(analysis, "rhythm.durations");
        Assert.Equal(FactCertainty.Uncertain, durations.Certainty);
        Assert.Equal("Lengths: 1 quarter, 2 lengths that are no single note value.", durations.Text);
        Assert.Equal([("between grid lines", "b"), ("start, bar 1 beat 1", "a")], grid.Places.Select(place => (place.Label, string.Join(" ", place.NoteIds))));
        Assert.Equal([("quarter", "c"), ("no single note value", "a b")], durations.Places.Select(place => (place.Label, string.Join(" ", place.NoteIds))));
    }

    [Fact]
    public void An_inferred_hand_makes_detected_facts_inferred()
    {
        Note[] notes = [Note("a", 72, 0, Quarter, Hand.Right, handInferred: true), Note("b", 74, Quarter, Quarter, Hand.Right, handInferred: true)];
        MotifSelection selection = Select(notes, MeterMap.Default, 0, 4 * Quarter, SelectionFilter.ForHand(Hand.Right));

        MotifAnalysis analysis = MotifAnalyzer.Analyze(notes, MeterMap.Default, selection, new KeySignature(Tick.Zero, 0, KeyMode.Major));

        Assert.True(selection.HandInferred);
        Assert.DoesNotContain(analysis.Facts, fact => fact.Certainty == FactCertainty.Detected);
        Assert.StartsWith("The hand is inferred", Fact(analysis, "melody.intervals").Basis);
        Assert.Equal(["lowest, C5", "highest, D5"], Fact(analysis, "melody.range").Places.Select(place => place.Label));
    }

    [Fact]
    public void An_empty_selection_has_no_facts_and_one_note_has_no_intervals()
    {
        Note[] notes = [Note("a", 60, 0, Quarter)];

        MotifAnalysis empty = MotifAnalyzer.Analyze(notes, MeterMap.Default, Select(notes, MeterMap.Default, 4 * Quarter, 8 * Quarter), null);
        MotifAnalysis single = MotifAnalyzer.Analyze(notes, MeterMap.Default, Select(notes, MeterMap.Default, 0, 4 * Quarter), null);

        Assert.Empty(empty.Facts);
        Assert.Null(empty.Key);
        Assert.DoesNotContain(single.Facts, fact => fact.Id is "melody.intervals" or "melody.contour");
        Assert.Equal("Range: one pitch, C4.", Fact(single, "melody.range").Text);
    }

    [Fact]
    public void Analysis_is_deterministic_and_fact_ids_are_unique()
    {
        Note[] notes = FurElise();
        MotifSelection selection = Select(notes, s_threeEight, 0, 14 * Sixteenth);

        MotifAnalysis first = MotifAnalyzer.Analyze(notes, s_threeEight, selection, null);
        MotifAnalysis second = MotifAnalyzer.Analyze(notes.AsEnumerable().Reverse(), s_threeEight, selection, null);

        Assert.Equal(first.Facts, second.Facts);
        Assert.Equal(first.Key, second.Key);
        Assert.Equal(first.Facts.Count, first.Facts.Select(fact => fact.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(0, "unison")]
    [InlineData(-3, "minor 3rd")]
    [InlineData(6, "tritone")]
    [InlineData(12, "octave")]
    [InlineData(14, "octave and a major 2nd")]
    [InlineData(24, "2 octaves")]
    public void Intervals_are_named_by_size(int semitones, string name) =>
        Assert.Equal(name, MotifAnalyzer.IntervalName(semitones));

    [Theory]
    [InlineData(68, 0, KeyMode.Minor, "♯7")]
    [InlineData(70, 0, KeyMode.Major, "♭7")]
    [InlineData(63, -3, KeyMode.Major, "1")]
    [InlineData(67, -3, KeyMode.Major, "3")]
    public void Scale_degrees_count_from_the_tonic(int pitch, int fifths, KeyMode mode, string degree) =>
        Assert.Equal(degree, MotifAnalyzer.Degree(new Pitch(pitch), new MusicalKey(fifths, mode)));

    [Theory]
    [InlineData(0, KeyMode.Major, "C major")]
    [InlineData(-3, KeyMode.Major, "E♭ major")]
    [InlineData(3, KeyMode.Minor, "F♯ minor")]
    [InlineData(-7, KeyMode.Major, "C♭ major")]
    public void Keys_are_named_from_their_signature(int fifths, KeyMode mode, string name) =>
        Assert.Equal(name, new MusicalKey(fifths, mode).Name);

    [Fact]
    public void The_key_signature_in_force_is_the_last_one_at_or_before_a_position()
    {
        KeySignature[] keys = [new(Tick.Zero, 0, KeyMode.Major), new(new Tick(1920), 1, null)];

        Assert.Equal(keys[0], KeySignature.InForce(keys, new Tick(1919)));
        Assert.Equal(keys[1], KeySignature.InForce(keys, new Tick(1920)));
        Assert.Null(KeySignature.InForce([], Tick.Zero));
    }
}