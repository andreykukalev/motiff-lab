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
        MotifAnalyzer.Analyze(notes, meter, MotifSelector.Select(notes, meter, TickSpan.FromBounds(new Tick(from), new Tick(to)), SelectionFilter.All), signature, phrases);

    private static MotifFact Fact(MotifAnalysis analysis, string id) => Assert.Single(analysis.Facts, fact => fact.Id == id);

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

        MotifFact fact = Fact(MotifAnalyzer.Analyze(import.Notes, import.Meter, selection, null, phrases), "melody.phrases");

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
}