using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>Interval names, repeated notes and phrase boundaries (S1-A2).</summary>
public class MelodyFactTests
{
    private const long Quarter = 480;
    private const long Eighth = 240;
    private const long Sixteenth = 120;

    private static readonly MeterMap s_threeEight = new([new MeterChange(Tick.Zero, new TimeSignature(3, 8))], pickupDuration: Eighth);

    private static readonly KeySignature s_aMinor = new(Tick.Zero, 0, KeyMode.Minor);

    private static readonly KeySignature s_cMajor = new(Tick.Zero, 0, KeyMode.Major);

    private static Note Note(string id, int pitch, long onset, long duration) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    /// <summary>E5 D♯5 | E5 D♯5 E5 B4 D5 C5 | A4.</summary>
    private static Note[] FurElise()
    {
        int[] pitches = [76, 75, 76, 75, 76, 71, 74, 72];
        var notes = pitches.Select((pitch, i) => Note($"n{i + 1}", pitch, i * Sixteenth, Sixteenth)).ToList();
        notes.Add(Note("n9", 69, 8 * Sixteenth, Eighth));
        return [.. notes];
    }

    /// <summary>Ode to Joy, bars 1–2 in quarters: E E F G | G F E D.</summary>
    private static Note[] OdeToJoy() =>
        [.. new[] { 64, 64, 65, 67, 67, 65, 64, 62 }.Select((pitch, i) => Note($"o{i + 1}", pitch, i * Quarter, Quarter))];

    private static MotifAnalysis Analyze(Note[] notes, MeterMap meter, long from, long to, KeySignature? signature, IEnumerable<PhraseBoundary>? phrases = null) =>
        MotifAnalyzer.Analyze(notes, meter, MotifSelector.Select(notes, meter, TickSpan.FromBounds(new Tick(from), new Tick(to)), SelectionFilter.All), signature, phrases: phrases);

    private static MotifFact Fact(MotifAnalysis analysis, string id) => Assert.Single(analysis.Facts, fact => fact.Id == id);

    /// <summary>Each place of a fact as "label: note ids", or the label alone for a stretch of time.</summary>
    private static string[] Places(MotifFact fact) =>
        [.. fact.Places.Select(place => place.NoteIds.Count == 0 ? place.Label : $"{place.Label}: {string.Join(" ", place.NoteIds)}")];

    [Fact]
    public void Intervals_are_named_with_their_direction_and_the_most_common_size()
    {
        MotifAnalysis analysis = Analyze(FurElise(), s_threeEight, 0, 9 * Sixteenth, s_aMinor);

        MotifFact names = Fact(analysis, "melody.interval-names");
        Assert.Equal(
            "Intervals by name: minor 2nd down, minor 2nd up, minor 2nd down, minor 2nd up, perfect 4th down, minor 3rd up, major 2nd down, minor 3rd down. Most common: a minor 2nd (4 of 8).",
            names.Text);
        Assert.Equal(FactCertainty.Detected, names.Certainty);
    }

    [Fact]
    public void Repeated_notes_are_listed_where_they_start()
    {
        MotifAnalysis ode = Analyze(OdeToJoy(), MeterMap.Default, 0, 8 * Quarter, s_cMajor);
        MotifAnalysis elise = Analyze(FurElise(), s_threeEight, 0, 9 * Sixteenth, s_aMinor);

        Assert.Equal("Repeated straight away: E4 twice from bar 1 beat 1; G4 twice from bar 1 beat 4. E4 sounds most often, 3 of 8 notes.", Fact(ode, "melody.repeated-notes").Text);
        Assert.Equal(
            "Intervals by name: unison, minor 2nd up, major 2nd up, unison, major 2nd down, minor 2nd down, major 2nd down. Most common: a major 2nd (3 of 7).",
            Fact(ode, "melody.interval-names").Text);
        Assert.Equal("No note is repeated straight away. E5 sounds most often, 3 of 9 notes.", Fact(elise, "melody.repeated-notes").Text);
    }

    [Fact]
    public void Melody_facts_name_the_notes_they_refer_to()
    {
        MotifAnalysis analysis = Analyze(FurElise(), s_threeEight, 0, 9 * Sixteenth, s_aMinor);

        Assert.Equal(["the melody: n1 n2 n3 n4 n5 n6 n7 n8 n9"], Places(Fact(analysis, "melody.line")));
        Assert.Equal(["largest leap, E5 to B4: n5 n6"], Places(Fact(analysis, "melody.intervals")));
        Assert.Equal(
            ["start, E5: n1", "low point, D♯5: n2", "peak, E5: n3", "low point, D♯5: n4", "peak, E5: n5", "low point, B4: n6", "peak, D5: n7", "end, A4: n9"],
            Places(Fact(analysis, "melody.contour")));
        Assert.Equal(["lowest, A4: n9", "highest, E5: n1 n3 n5"], Places(Fact(analysis, "melody.range")));
        Assert.Equal(["D♯5, degree ♯4: n2", "D♯5, degree ♯4: n4"], Places(Fact(analysis, "melody.degrees")));
        Assert.Equal(["most common, minor 2nd: n1 n2 n3 n4 n5"], Places(Fact(analysis, "melody.interval-names")));
        Assert.Equal(["most often, E5: n1 n3 n5"], Places(Fact(analysis, "melody.repeated-notes")));
        Assert.Empty(Fact(analysis, "key").Places);

        // A place spans its notes: the highest E5s from the first onset to the end of the last.
        Assert.Equal(TickSpan.FromBounds(Tick.Zero, new Tick(5 * Sixteenth)), Fact(analysis, "melody.range").Places[1].Span);
    }

    [Fact]
    public void Repeated_notes_and_a_rise_and_fall_name_their_places()
    {
        MotifAnalysis ode = Analyze(OdeToJoy(), MeterMap.Default, 0, 8 * Quarter, s_cMajor);

        Assert.Equal(["E4 twice from bar 1 beat 1: o1 o2", "G4 twice from bar 1 beat 4: o4 o5", "most often, E4: o1 o2 o7"], Places(Fact(ode, "melody.repeated-notes")));
        Assert.Equal(["start, E4: o1", "peak, G4: o5", "end, D4: o8"], Places(Fact(ode, "melody.contour")));
        Assert.Empty(Fact(ode, "melody.intervals").Places);
    }

    [Fact]
    public void Long_interval_lists_are_cut()
    {
        Note[] notes = [.. Enumerable.Range(0, 16).Select(i => Note($"s{i}", i % 2 == 0 ? 60 : 67, i * Eighth, Eighth))];

        string text = Fact(Analyze(notes, MeterMap.Default, 0, 16 * Eighth, s_cMajor), "melody.interval-names").Text;

        Assert.Contains("perfect 5th down and 3 more.", text);
        Assert.EndsWith("Most common: a perfect 5th (15 of 15).", text);
    }

    [Fact]
    public void Phrase_boundaries_inside_the_motif_are_listed_with_their_evidence()
    {
        Note[] notes = [.. Enumerable.Range(0, 8).Select(i => Note($"p{i}", 60 + i, i * Quarter, Quarter))];
        PhraseBoundary[] found =
        [
            new(new Tick(2 * Quarter), [PhraseEvidence.Slur], Attribution.Imported),
            new(new Tick(4 * Quarter), [PhraseEvidence.Rest], Attribution.Inferred(PhraseBoundaryFinder.RestConfidence)),
        ];

        MotifFact firstBar = Fact(Analyze(notes, MeterMap.Default, 0, 4 * Quarter, s_cMajor, found), "melody.phrases");
        MotifFact twoBars = Fact(Analyze(notes, MeterMap.Default, 0, 8 * Quarter, s_cMajor, found), "melody.phrases");
        MotifFact fromTheSlur = Fact(Analyze(notes, MeterMap.Default, 2 * Quarter, 4 * Quarter, s_cMajor, found), "melody.phrases");

        Assert.Equal("1 phrase boundary inside the motif: at bar 1 beat 3, from a slur.", firstBar.Text);
        Assert.Equal(FactCertainty.Detected, firstBar.Certainty);
        Assert.Equal("2 phrase boundaries inside the motif: at bar 1 beat 3, from a slur; at bar 2 beat 1, from a rest of a beat or more.", twoBars.Text);
        Assert.Equal(FactCertainty.Inferred, twoBars.Certainty);
        Assert.Equal("No phrase boundary falls inside the motif. It starts where a phrase starts (from a slur).", fromTheSlur.Text);
        Assert.Equal(["phrase boundary, bar 1 beat 3: p2", "phrase boundary, bar 2 beat 1: p4"], Places(twoBars));
        Assert.Equal(["phrase start: p2"], Places(fromTheSlur));
    }

    [Fact]
    public void Boundaries_the_player_set_are_certain_and_say_so()
    {
        Note[] notes = [.. Enumerable.Range(0, 8).Select(i => Note($"p{i}", 60 + i, i * Quarter, Quarter))];
        PhraseBoundary[] found = [new(new Tick(4 * Quarter), [PhraseEvidence.Rest], Attribution.Inferred(PhraseBoundaryFinder.RestConfidence))];

        IReadOnlyList<PhraseBoundary> phrases = PhraseBoundaryFinder.WithPlayerBoundaries(found, [new Tick(4 * Quarter), new Tick(6 * Quarter)]);
        MotifFact fact = Fact(Analyze(notes, MeterMap.Default, 0, 8 * Quarter, s_cMajor, phrases), "melody.phrases");

        Assert.Equal(
            "2 phrase boundaries inside the motif: at bar 2 beat 1, from a rest of a beat or more, confirmed by the player; at bar 2 beat 3, set by the player.",
            fact.Text);
        Assert.Equal(FactCertainty.Detected, fact.Certainty);
    }

    [Fact]
    public void Without_phrase_information_there_is_no_phrase_fact_and_an_empty_list_says_none()
    {
        Note[] notes = OdeToJoy();

        Assert.DoesNotContain(Analyze(notes, MeterMap.Default, 0, 8 * Quarter, s_cMajor).Facts, fact => fact.Id == "melody.phrases");
        Assert.Equal("No phrase boundary falls inside the motif.", Fact(Analyze(notes, MeterMap.Default, 0, 8 * Quarter, s_cMajor, []), "melody.phrases").Text);
    }

    [Fact]
    public void Phrase_marks_from_a_musicxml_file_reach_the_motif()
    {
        MusicXmlImport import = MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "phrases-repeats.musicxml")));
        IReadOnlyList<PhraseBoundary> phrases = PhraseBoundaryFinder.Find(import.Notes, import.Meter, import.PhraseMarks);
        MotifSelection selection = MotifSelector.Select(import.Notes, import.Meter, TickSpan.FromBounds(new Tick(1920), new Tick(5760)), SelectionFilter.All);

        MotifFact fact = Fact(MotifAnalyzer.Analyze(import.Notes, import.Meter, selection, null, phrases: phrases), "melody.phrases");

        Assert.Equal("1 phrase boundary inside the motif: at bar 3 beat 2, from a slur and a rest of a beat or more.", fact.Text);
        Assert.Equal(FactCertainty.Detected, fact.Certainty);
    }

    [Fact]
    public void Melody_facts_are_deterministic_and_ids_stay_unique()
    {
        Note[] notes = OdeToJoy();
        PhraseBoundary[] phrases = [new(new Tick(4 * Quarter), [PhraseEvidence.Slur], Attribution.Imported)];

        MotifAnalysis first = Analyze(notes, MeterMap.Default, 0, 8 * Quarter, s_cMajor, phrases);
        MotifAnalysis second = Analyze([.. notes.Reverse()], MeterMap.Default, 0, 8 * Quarter, s_cMajor, phrases);

        Assert.Equal(first.Facts, second.Facts);
        Assert.Equal(first.Facts.Count, first.Facts.Select(fact => fact.Id).Distinct().Count());
    }

    [Fact]
    public void A_single_line_has_no_fact_about_melody_over_chords()
    {
        MotifAnalysis analysis = Analyze(OdeToJoy(), MeterMap.Default, 0, 8 * Quarter, s_cMajor);

        Assert.DoesNotContain(analysis.Facts, fact => fact.Id == "melody.over-chords");
    }

    [Fact]
    public void Melody_notes_over_chords_are_counted_and_shared_pitch_classes_named()
    {
        // E5 struck with C3 E4 G4 (E is in the chord), then D5 over the held chord, then F5 struck with B3 D4 (F is not doubled).
        Note[] notes =
        [
            Note("c3", 48, 0, 2 * Quarter), Note("e4", 64, 0, 2 * Quarter), Note("g4", 67, 0, 2 * Quarter),
            Note("e5", 76, 0, Quarter), Note("d5", 74, Quarter, Quarter),
            Note("b3", 59, 2 * Quarter, Quarter), Note("d4", 62, 2 * Quarter, Quarter), Note("f5", 77, 2 * Quarter, Quarter),
        ];

        MotifAnalysis analysis = Analyze(notes, MeterMap.Default, 0, 3 * Quarter, s_cMajor);

        MotifFact fact = Fact(analysis, "melody.over-chords");
        Assert.Equal(
            "3 of 3 melody notes sound over lower notes: 2 struck together with them, 1 over notes still held. 1 shares its pitch class with a lower note, so it is also part of the chord below: E5 (with E4).",
            fact.Text);
        Assert.Equal(FactCertainty.Inferred, fact.Certainty);
        Assert.Equal(["e5", "d5", "f5"], analysis.Line.Select(note => note.Id));
        Assert.Equal(["E5 (with E4): e4 e5"], Places(fact));
        Assert.Equal(["the top line: e5 d5 f5"], Places(Fact(analysis, "melody.line")));
    }

    [Fact]
    public void Accompaniment_under_a_held_melody_note_stays_out_of_the_melody_facts()
    {
        // The melody holds A4 while the accompaniment moves below it; the old top-note-per-onset reading counted E3 and A3 as melody.
        Note[] notes =
        [
            Note("a4", 69, 0, 3 * Eighth), Note("a2", 45, 0, Eighth), Note("e3", 52, Eighth, Eighth), Note("a3", 57, 2 * Eighth, Eighth),
            Note("b4", 71, 3 * Eighth, Eighth),
        ];

        MotifAnalysis analysis = Analyze(notes, MeterMap.Default, 0, 4 * Eighth, s_aMinor);

        Assert.Equal(["a4", "b4"], analysis.Line.Select(note => note.Id));
        Assert.Equal([2], analysis.Intervals);
        Assert.StartsWith("1 of 2 melody notes sound over lower notes: 1 struck together with them.", Fact(analysis, "melody.over-chords").Text);
    }
}