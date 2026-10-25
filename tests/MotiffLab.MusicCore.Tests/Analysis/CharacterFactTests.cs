using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>Tempo, density, register, velocity, articulation and syncopation share (S1-A5).</summary>
public class CharacterFactTests
{
    private const long Quarter = 480;
    private const long Eighth = 240;

    private static readonly KeySignature s_cMajor = new(Tick.Zero, 0, KeyMode.Major);

    private static readonly CharacterContext s_stated = new([new PlaybackTempo(Tick.Zero, 625_000)], TempoAssumed: false, FromNotation: false);

    private static Note Note(string id, int pitch, long onset, long duration, int velocity = 80) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = velocity,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    /// <summary>C4 D4 E4 F4 | G4 (half) in 4/4, quarters held for their full length.</summary>
    private static Note[] Scale() =>
    [
        Note("n1", 60, 0, Quarter, 64),
        Note("n2", 62, Quarter, Quarter, 72),
        Note("n3", 64, 2 * Quarter, Quarter, 80),
        Note("n4", 65, 3 * Quarter, Quarter, 88),
        Note("n5", 67, 4 * Quarter, 2 * Quarter, 96),
    ];

    private static IReadOnlyList<MotifFact> Analyze(Note[] notes, long from, long to, CharacterContext? context = null, MeterMap? meter = null) =>
        MotifAnalyzer.AnalyzeCharacter(
            notes,
            meter ?? MeterMap.Default,
            MotifSelector.Select(notes, meter ?? MeterMap.Default, TickSpan.FromBounds(new Tick(from), new Tick(to)), SelectionFilter.All),
            s_cMajor,
            context ?? s_stated);

    private static MotifFact Fact(IReadOnlyList<MotifFact> facts, string id) => Assert.Single(facts, fact => fact.Id == id);

    /// <summary>Each place of a fact as "label: note ids", or the label alone for a stretch of time.</summary>
    private static string[] Places(MotifFact fact) =>
        [.. fact.Places.Select(place => place.NoteIds.Count == 0 ? place.Label : $"{place.Label}: {string.Join(" ", place.NoteIds)}")];

    [Fact]
    public void Facts_are_in_the_character_lens_with_stable_ids_in_order()
    {
        IReadOnlyList<MotifFact> facts = Analyze(Scale(), 0, 8 * Quarter);

        Assert.Equal(
            ["character.tempo", "character.density", "character.register", "character.velocity", "character.articulation", "character.syncopation"],
            facts.Select(fact => fact.Id));
        Assert.All(facts, fact => Assert.Equal(FactLens.Character, fact.Lens));
    }

    [Fact]
    public void A_selection_without_notes_has_no_character_facts()
    {
        Assert.Empty(Analyze(Scale(), 8 * Quarter, 12 * Quarter));
    }

    [Fact]
    public void A_stated_tempo_is_detected_in_quarter_notes_per_minute()
    {
        MotifFact tempo = Fact(Analyze(Scale(), 0, 8 * Quarter), "character.tempo");

        Assert.Equal("Tempo: 96 BPM throughout the motif.", tempo.Text);
        Assert.Equal(FactCertainty.Detected, tempo.Certainty);
    }

    [Fact]
    public void A_missing_tempo_says_the_default_was_assumed()
    {
        var context = new CharacterContext([], TempoAssumed: true, FromNotation: true);

        MotifFact tempo = Fact(Analyze(Scale(), 0, 8 * Quarter, context), "character.tempo");

        Assert.Equal("Tempo: 120 BPM throughout the motif.", tempo.Text);
        Assert.Equal(FactCertainty.Inferred, tempo.Certainty);
        Assert.StartsWith("The file states no tempo at its start, so the default of 120 BPM is assumed.", tempo.Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void Tempo_changes_inside_the_motif_are_listed_with_their_place_and_range()
    {
        // 96 BPM, then 92.3 BPM (650 000 µs) at bar 1 beat 3, the same again at bar 2 (dropped), 100 BPM at bar 2 beat 3, then 80 after the selection.
        var context = new CharacterContext(
            [
                new PlaybackTempo(Tick.Zero, 625_000),
                new PlaybackTempo(new Tick(2 * Quarter), 650_000),
                new PlaybackTempo(new Tick(4 * Quarter), 650_000),
                new PlaybackTempo(new Tick(6 * Quarter), 600_000),
                new PlaybackTempo(new Tick(8 * Quarter), 750_000),
            ],
            TempoAssumed: true,
            FromNotation: false);

        MotifFact tempo = Fact(Analyze(Scale(), 0, 8 * Quarter, context), "character.tempo");

        Assert.Equal("Tempo: starts at 96 BPM and changes twice: 92.3 BPM at bar 1 beat 3; 100 BPM at bar 2 beat 3. It ranges from 92.3 to 100 BPM.", tempo.Text);
        Assert.Equal(FactCertainty.Inferred, tempo.Certainty);

        // Each change lasts until the next one, the last until the end of the selection.
        Assert.Equal(
            [("92.3 BPM at bar 1 beat 3", TickSpan.FromBounds(new Tick(2 * Quarter), new Tick(6 * Quarter))), ("100 BPM at bar 2 beat 3", TickSpan.FromBounds(new Tick(6 * Quarter), new Tick(8 * Quarter)))],
            tempo.Places.Select(place => (place.Label, place.Span)));
        Assert.All(tempo.Places, place => Assert.Empty(place.NoteIds));
    }

    [Fact]
    public void A_motif_after_the_first_stated_tempo_change_is_not_marked_assumed()
    {
        var context = new CharacterContext([new PlaybackTempo(Tick.Zero, 500_000), new PlaybackTempo(new Tick(4 * Quarter), 750_000)], TempoAssumed: true, FromNotation: false);

        MotifFact tempo = Fact(Analyze(Scale(), 4 * Quarter, 8 * Quarter, context), "character.tempo");

        Assert.Equal("Tempo: 80 BPM throughout the motif.", tempo.Text);
        Assert.Equal(FactCertainty.Detected, tempo.Certainty);
        Assert.Empty(tempo.Places);
    }

    [Fact]
    public void Density_counts_onsets_per_beat_and_notes_of_chords_separately()
    {
        // Bar 1: C–E–G chord on beat 1, then four eighths on beats 3 and 4.
        Note[] notes =
        [
            Note("c1", 60, 0, Quarter),
            Note("c2", 64, 0, Quarter),
            Note("c3", 67, 0, Quarter),
            Note("e1", 72, 2 * Quarter, Eighth),
            Note("e2", 71, (2 * Quarter) + Eighth, Eighth),
            Note("e3", 69, 3 * Quarter, Eighth),
            Note("e4", 67, (3 * Quarter) + Eighth, Eighth),
        ];

        MotifFact density = Fact(Analyze(notes, 0, 4 * Quarter), "character.density");

        Assert.Equal("Density: 1.3 onsets per beat (5 onsets over 4 beats); the busiest beat holds 2. Counting every note of a chord, 1.8 notes per beat.", density.Text);
        Assert.Equal(["busiest beat, bar 1 beat 3: e1 e2", "busiest beat, bar 1 beat 4: e3 e4"], Places(density));
        Assert.Equal(FactCertainty.Detected, density.Certainty);
    }

    [Fact]
    public void Register_gives_the_range_middle_c_and_the_median_pitch()
    {
        Note[] notes = [Note("a", 55, 0, Quarter), Note("b", 59, Quarter, Quarter), Note("c", 62, 2 * Quarter, Quarter), Note("d", 76, 3 * Quarter, Quarter)];

        MotifFact register = Fact(Analyze(notes, 0, 4 * Quarter), "character.register");
        MotifFact high = Fact(Analyze(Scale(), 0, 8 * Quarter), "character.register");

        Assert.Equal("Register: G3 to E5, a range of 21 semitones; 2 of 4 notes are at or above middle C, and the middle pitch is B3.", register.Text);
        Assert.Equal("Register: C4 to G4, a range of 7 semitones; every note is at or above middle C, and the middle pitch is E4.", high.Text);
        Assert.Equal(["lowest, C4: n1", "highest, G4: n5", "middle pitch, E4: n3"], Places(high));
    }

    [Fact]
    public void Velocity_gives_the_range_and_median_or_the_one_value()
    {
        Note[] even = [Note("a", 60, 0, Quarter), Note("b", 62, Quarter, Quarter)];

        Assert.Equal("Velocity: 64 to 96, median 80, on the MIDI scale of 1 to 127.", Fact(Analyze(Scale(), 0, 8 * Quarter), "character.velocity").Text);
        Assert.Equal(["lowest velocity, 64: n1", "highest velocity, 96: n5", "median velocity, 80: n3"], Places(Fact(Analyze(Scale(), 0, 8 * Quarter), "character.velocity")));
        MotifFact flat = Fact(Analyze(even, 0, 4 * Quarter, new CharacterContext([], TempoAssumed: true, FromNotation: true)), "character.velocity");
        Assert.Equal("Velocity: every note has 80, on the MIDI scale of 1 to 127.", flat.Text);
        Assert.Empty(flat.Places);
        Assert.Contains("A notation file holds no played loudness", flat.Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void Articulation_measures_sounding_length_against_the_gap_to_the_next_onset()
    {
        // Onsets on every beat: held, half held, a thirty-second, held past the next onset; the last onset has nothing to measure against.
        Note[] notes =
        [
            Note("a", 60, 0, Quarter),
            Note("b", 62, Quarter, Eighth),
            Note("c", 64, 2 * Quarter, 60),
            Note("d", 65, 3 * Quarter, 2 * Quarter),
            Note("e", 67, 4 * Quarter, Quarter),
        ];

        MotifFact articulation = Fact(Analyze(notes, 0, 8 * Quarter), "character.articulation");

        // (100 + 50 + 12.5 + 100) / 4 = 65.6.
        Assert.Equal("Articulation: notes sound for 66% of the time to the next onset on average; 2 of 4 notes sound until the next onset, and 1 sounds for less than half of it.", articulation.Text);
        Assert.Equal(FactCertainty.Detected, articulation.Certainty);
        Assert.DoesNotContain("written", articulation.Basis, StringComparison.Ordinal);
        Assert.Equal(["until the next onset: a d", "less than half: c"], Places(articulation));
    }

    [Fact]
    public void Articulation_from_notation_says_it_reflects_written_lengths()
    {
        MotifFact articulation = Fact(Analyze(Scale(), 0, 8 * Quarter, new CharacterContext([], TempoAssumed: true, FromNotation: true)), "character.articulation");

        Assert.Equal("Articulation: notes sound for 100% of the time to the next onset on average; 4 of 4 notes sound until the next onset, and 0 sound for less than half of it.", articulation.Text);
        Assert.Contains("staccato or tenuto marks are not read", articulation.Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void Articulation_of_a_single_chord_is_not_measured()
    {
        Note[] chord = [Note("a", 60, 0, Quarter), Note("b", 64, 0, Quarter)];

        Assert.Equal("Articulation: not measured, because every note starts together.", Fact(Analyze(chord, 0, 4 * Quarter), "character.articulation").Text);
    }

    [Fact]
    public void Syncopation_share_uses_the_same_rule_as_the_syncopation_fact()
    {
        // Eighth, quarter from the off-beat, eighth, quarter, then beat 4 held into the next bar, then a whole-bar note.
        Note[] notes =
        [
            Note("r1", 60, 0, Eighth),
            Note("r2", 60, Eighth, Quarter),
            Note("r3", 60, 3 * Eighth, Eighth),
            Note("r4", 60, 2 * Quarter, Quarter),
            Note("r5", 60, 3 * Quarter, 2 * Quarter),
            Note("r6", 60, 5 * Quarter, 3 * Quarter),
        ];
        MotifSelection selection = MotifSelector.Select(notes, MeterMap.Default, TickSpan.FromBounds(Tick.Zero, new Tick(8 * Quarter)), SelectionFilter.All);

        MotifFact share = Fact(Analyze(notes, 0, 8 * Quarter), "character.syncopation");
        MotifFact rhythm = Assert.Single(MotifAnalyzer.Analyze(notes, MeterMap.Default, selection, s_cMajor).Facts, fact => fact.Id == "rhythm.syncopation");

        Assert.StartsWith("2 syncopations", rhythm.Text, StringComparison.Ordinal);
        Assert.Equal("Syncopation share: 2 of 6 onsets (33%).", share.Text);
        Assert.Equal(["r2", "r5"], share.Places.SelectMany(place => place.NoteIds));
        Assert.Equal(rhythm.Places, share.Places);
        Assert.Equal(FactCertainty.Detected, share.Certainty);
    }

    [Fact]
    public void Off_grid_onsets_make_the_syncopation_share_uncertain()
    {
        Note[] notes = [Note("a", 60, 7, Quarter), Note("b", 62, Quarter + 3, Quarter)];

        Assert.Equal(FactCertainty.Uncertain, Fact(Analyze(notes, 0, 4 * Quarter), "character.syncopation").Certainty);
    }

    [Fact]
    public void Same_input_gives_the_same_facts()
    {
        Assert.Equal(Analyze(Scale(), 0, 8 * Quarter), Analyze(Scale(), 0, 8 * Quarter));
    }

    [Fact]
    public void Facts_use_no_mood_words()
    {
        string[] moods = ["calm", "urgent", "happy", "sad", "gentle", "energetic", "lively", "bright", "dark", "aggressive", "peaceful", "tense"];
        string text = string.Join(" ", Analyze(Scale(), 0, 8 * Quarter).Select(fact => $"{fact.Text} {fact.Basis}")).ToLowerInvariant();

        Assert.All(moods, mood => Assert.DoesNotContain(mood, text, StringComparison.Ordinal));
    }
}