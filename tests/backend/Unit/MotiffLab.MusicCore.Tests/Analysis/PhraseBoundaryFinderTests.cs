using System.Text;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

public class PhraseBoundaryFinderTests
{
    private const long Quarter = 480;

    private static readonly MeterMap s_fourFour = MeterMap.Default;

    private static Note Note(string id, int pitch, long onset, long duration, int track = 0) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = track,
        Attribution = Attribution.Imported,
    };

    private static MusicXmlImport ImportXml(string name) =>
        MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", name)));

    private static MidiImport ImportMidi(string name) =>
        MidiImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Midi", "Fixtures", name)));

    [Fact]
    public void Phrase_slurs_rests_and_repeats_mark_boundaries_with_their_provenance()
    {
        MusicXmlImport result = ImportXml("phrases-repeats.musicxml");

        IReadOnlyList<PhraseBoundary> boundaries = PhraseBoundaryFinder.Find(result.Notes, result.Meter, result.PhraseMarks);

        // Bar 3 starts with a beat of rest and a new slur; bar 5 (the second ending) follows a backward repeat and starts a slur.
        Assert.Equal(
            [
                (4320L, new[] { PhraseEvidence.Slur, PhraseEvidence.Rest }),
                (7680L, new[] { PhraseEvidence.Slur, PhraseEvidence.RepeatSign }),
            ],
            boundaries.Select(boundary => (boundary.At.Value, boundary.Evidence.ToArray())));
        Assert.All(boundaries, boundary => Assert.Equal(Attribution.Imported, boundary.Attribution));
    }

    [Fact]
    public void A_slur_inside_a_phrase_slur_is_not_a_boundary()
    {
        MusicXmlImport result = ImportXml("phrases-repeats.musicxml");

        // The nested E5–F5 slur in bar 1 is imported but marks nothing.
        Assert.Contains(result.Slurs, slur => slur.Number == 2 && slur.Start.Value == 960 && slur.Stop.Value == 1440);

        IReadOnlyList<PhraseBoundary> boundaries = PhraseBoundaryFinder.Find(result.Notes, result.Meter, result.PhraseMarks);
        Assert.DoesNotContain(boundaries, boundary => boundary.At.Value is 960 or 1920);
    }

    [Fact]
    public void Without_marks_only_a_rest_gives_a_boundary_and_it_is_inferred()
    {
        MusicXmlImport result = ImportXml("phrases-repeats.musicxml");

        PhraseBoundary boundary = Assert.Single(PhraseBoundaryFinder.Find(result.Notes, result.Meter, PhraseMarks.None));

        Assert.Equal(new Tick(4320), boundary.At);
        Assert.Equal([PhraseEvidence.Rest], boundary.Evidence);
        Assert.Equal(Attribution.Inferred(PhraseBoundaryFinder.RestConfidence), boundary.Attribution);
    }

    [Fact]
    public void No_boundary_is_invented_without_evidence()
    {
        // A scale with no rests and no phrase marks.
        MidiImport scale = ImportMidi("scale-type0.mid");
        Assert.Empty(PhraseBoundaryFinder.Find(scale.Notes, scale.Meter, PhraseMarks.None));

        // A score without slurs or repeats, and with no beat of rest.
        MusicXmlImport ties = ImportXml("ties-triplets-pickup.musicxml");
        Assert.Empty(ties.Slurs);
        Assert.Empty(ties.Repeats);
        Assert.Empty(PhraseBoundaryFinder.Find(ties.Notes, ties.Meter, ties.PhraseMarks));

        Assert.Empty(PhraseBoundaryFinder.Find([], s_fourFour, PhraseMarks.None));
    }

    [Fact]
    public void A_rest_shorter_than_a_beat_is_not_a_boundary()
    {
        Note[] notes =
        [
            Note("a", 60, 0, Quarter),
            Note("b", 62, Quarter + 240, Quarter), // an eighth of silence
            Note("c", 64, (3 * Quarter) + 240, Quarter), // a beat of silence after b
        ];

        PhraseBoundary boundary = Assert.Single(PhraseBoundaryFinder.Find(notes, s_fourFour, PhraseMarks.None));

        Assert.Equal(new Tick((3 * Quarter) + 240), boundary.At);
    }

    [Fact]
    public void A_held_note_in_another_voice_fills_the_silence()
    {
        Note[] notes =
        [
            Note("melody1", 72, 0, Quarter),
            Note("bass", 48, 0, 4 * Quarter),
            Note("melody2", 74, 2 * Quarter, Quarter),
        ];

        Assert.Empty(PhraseBoundaryFinder.Find(notes, s_fourFour, PhraseMarks.None));
        Assert.Single(PhraseBoundaryFinder.Find(notes.Where(note => note.Id != "bass"), s_fourFour, PhraseMarks.None));
    }

    [Fact]
    public void Slurs_on_other_tracks_are_ignored()
    {
        Note[] notes = [Note("a", 60, 0, Quarter), Note("b", 62, Quarter, Quarter), Note("c", 64, 2 * Quarter, Quarter)];
        var marks = new PhraseMarks([new SlurMark(Track: 1, new Tick(Quarter), new Tick(2 * Quarter))], []);

        Assert.Empty(PhraseBoundaryFinder.Find(notes, s_fourFour, marks));
    }

    [Fact]
    public void A_repeat_sign_marks_the_first_note_at_or_after_its_bar_line()
    {
        Note[] notes = [Note("a", 60, 0, 3 * Quarter), Note("b", 62, 3 * Quarter, 2 * Quarter), Note("c", 64, 5 * Quarter, Quarter)];
        var marks = new PhraseMarks([], [new Tick(4 * Quarter), Tick.Zero, new Tick(8 * Quarter)]);

        PhraseBoundary boundary = Assert.Single(PhraseBoundaryFinder.Find(notes, s_fourFour, marks));

        Assert.Equal((new Tick(5 * Quarter), PhraseEvidence.RepeatSign), (boundary.At, Assert.Single(boundary.Evidence)));
    }

    [Fact]
    public void Finding_is_deterministic_whatever_the_note_order()
    {
        MusicXmlImport result = ImportXml("phrases-repeats.musicxml");

        var forward = PhraseBoundaryFinder.Find(result.Notes, result.Meter, result.PhraseMarks);
        var reversed = PhraseBoundaryFinder.Find(result.Notes.Reverse(), result.Meter, result.PhraseMarks);

        Assert.Equal(forward.Select(b => (b.At, string.Join(",", b.Evidence), b.Attribution)), reversed.Select(b => (b.At, string.Join(",", b.Evidence), b.Attribution)));
    }

    [Fact]
    public void Repeat_signs_are_imported_once_per_bar_line()
    {
        MusicXmlImport result = ImportXml("phrases-repeats.musicxml");

        Assert.Equal(
            [new MusicXmlRepeat(Tick.Zero, 0, MusicXmlRepeatDirection.Forward), new MusicXmlRepeat(new Tick(7680), 3, MusicXmlRepeatDirection.Backward)],
            result.Repeats);
        Assert.Equal([0L, 4320, 7680], result.Slurs.Where(slur => slur.Number == 1).Select(slur => slur.Start.Value));
    }

    [Fact]
    public void Unmatched_slur_marks_are_reported_and_left_out()
    {
        const string attributes = "<attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time></attributes>";
        static string N(string step, string slur = "") =>
            $"<note><pitch><step>{step}</step><octave>4</octave></pitch><duration>1</duration>{slur}</note>";
        string xml = "<score-partwise><part-list><score-part id=\"P1\"/></part-list><part id=\"P1\"><measure number=\"1\">"
            + attributes
            + N("C", "<notations><slur number=\"1\" type=\"stop\"/></notations>")
            + N("D", "<notations><slur number=\"2\" type=\"start\"/></notations>")
            + N("E", "<notations><slur number=\"3\" type=\"start\"/></notations>")
            + N("F", "<notations><slur number=\"3\" type=\"stop\"/></notations>")
            + "</measure></part></score-partwise>";

        MusicXmlImport result = MusicXmlImporter.Import(Encoding.UTF8.GetBytes(xml));

        MusicXmlSlur slur = Assert.Single(result.Slurs);
        Assert.Equal((3, 960L, 1440L), (slur.Number, slur.Start.Value, slur.Stop.Value));
        Assert.Equal(2, result.Report.CountOf(MusicXmlIssueCodes.SlurUnmatched));
    }

    /// <summary>Eighth notes, one after another from <paramref name="start"/>, with these pitches.</summary>
    private static IEnumerable<Note> Line(string prefix, long start, params int[] pitches) =>
        pitches.Select((pitch, i) => Note($"{prefix}{i}", pitch, start + (i * 240), 240));

    [Fact]
    public void A_passage_that_comes_back_note_for_note_starts_a_phrase_and_is_inferred()
    {
        int[] opening = [76, 75, 76, 75, 76, 71, 74, 72];
        Note[] notes = [.. Line("a", 0, opening), .. Line("b", 8 * 240, 69, 60, 64, 69), .. Line("c", 12 * 240, opening), .. Line("d", 20 * 240, 69)];

        PhraseBoundary boundary = Assert.Single(PhraseBoundaryFinder.Find(notes, s_fourFour, PhraseMarks.None));

        Assert.Equal(new Tick(12 * 240), boundary.At);
        Assert.Equal([PhraseEvidence.Repetition], boundary.Evidence);
        Assert.Equal(Attribution.Inferred(PhraseBoundaryFinder.RepetitionConfidence), boundary.Attribution);
    }

    [Fact]
    public void A_return_with_other_rhythm_or_fewer_notes_marks_nothing()
    {
        int[] opening = [76, 75, 76, 75, 76, 71, 74, 72];
        Note[] slower = [.. Line("a", 0, opening), .. opening.Select((pitch, i) => Note($"s{i}", pitch, (8 * 240) + (i * 480), 480))];
        Note[] shorter = [.. Line("a", 0, opening), .. Line("b", 8 * 240, 60, 62), .. Line("c", 10 * 240, opening[..7]), .. Line("d", 17 * 240, 60)];

        Assert.Empty(PhraseBoundaryFinder.Find(slower, s_fourFour, PhraseMarks.None));
        Assert.Empty(PhraseBoundaryFinder.Find(shorter, s_fourFour, PhraseMarks.None));
    }

    [Fact]
    public void A_passage_after_a_boundary_that_comes_back_marks_its_return_too()
    {
        int[] tune = [60, 62, 64, 65, 67, 65, 64, 62];
        // An opening phrase, a beat of rest, the tune, other notes, then the tune again a little later.
        Note[] notes = [.. Line("a", 0, 72, 71, 69), .. Line("t", 6 * 240, tune), .. Line("b", 14 * 240, 50, 52, 53), .. Line("u", 17 * 240, tune), .. Line("e", 25 * 240, 48)];

        IReadOnlyList<PhraseBoundary> boundaries = PhraseBoundaryFinder.Find(notes, s_fourFour, PhraseMarks.None);

        Assert.Equal([(6 * 240L, PhraseEvidence.Rest), (17 * 240L, PhraseEvidence.Repetition)], boundaries.Select(b => (b.At.Value, Assert.Single(b.Evidence))));
    }
}