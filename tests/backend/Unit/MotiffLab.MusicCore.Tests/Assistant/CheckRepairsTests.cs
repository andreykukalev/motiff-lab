using System.Text.Json;

using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Repairs;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Assistant.AssistantScores;

namespace MotiffLab.MusicCore.Tests.Assistant;

/// <summary>Repairs by Check reason, with no AI (0028 section 6).</summary>
public class CheckRepairsTests
{
    private static readonly string s_full = Note("G", 4, 6);

    private static RepairOptions Repairs(ImportedPiece piece, int bar, CheckReason reason, IReadOnlyList<Note>? notes = null) =>
        CheckRepairs.For(new AssistantPiece(piece, notes ?? piece.Notes, [new CheckFlag(bar, CheckSource.Scan, reason, false)]), new CheckFlag(bar, CheckSource.Scan, reason, false));

    private static Note Played(string id, int pitch, long onset, long duration, int velocity = 80) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = velocity,
        Track = 0,
        Hand = new Attributed<Hand>(Hand.Right, Attribution.Detected(0.7)),
        Attribution = Attribution.Detected(0.7),
    };

    /// <summary>The notes after the draft is accepted as ordinary corrections, through the corrections log.</summary>
    private static IReadOnlyList<Note> Accepted(IReadOnlyList<Note> notes, AssistantDraft draft)
    {
        NoteEditLog log = NoteEditLog.Empty;
        foreach (DraftEdit edit in draft.Edits)
        {
            log = edit.Kind switch
            {
                DraftEditKind.Remove => log.Remove(notes, edit.NoteId),
                DraftEditKind.Change => log.Change(notes, edit.NoteId, edit.After!.Value),
                _ => log.Add(notes, edit.After!.Value, edit.Track, edit.Hand, out _),
            };
        }

        return log.Apply(notes);
    }

    [Fact]
    public void A_bar_too_long_by_a_repeated_note_offers_to_remove_it_and_close_up()
    {
        // Bar 2 holds seven eighths in 3/4: D5 is read twice.
        ImportedPiece piece = Score(s_full, Note("E", 5, 1) + Note("D", 5, 1) + Note("D", 5, 1) + Note("C", 5, 1) + Note("B", 4, 1) + Note("A", 4, 1) + Note("G", 4, 1), s_full);

        RepairOptions options = Repairs(piece, 2, CheckReason.BarTooLong);

        RepairCandidate first = options.Candidates[0];
        Assert.Null(options.NoneReason);
        Assert.Equal("barTooLong.doubled.2.R.3", first.Id);
        Assert.Equal("Remove the repeated D5 (2.R.3)", first.Title);
        IReadOnlyList<Note> fixedNotes = Accepted(piece.Notes, first.Draft);
        Bar bar = piece.Meter.GetBar(2);
        Assert.All(fixedNotes.Where(note => note.Span.Onset >= bar.Span.Onset && note.Span.Onset < new Tick(bar.Span.End.Value + 240)), note => Assert.True(note.Span.End <= bar.Span.End));
        Assert.Equal(6, fixedNotes.Count(note => bar.Span.Contains(note.Span.Onset)));
        Assert.Equal(piece.Notes.Count - 1, fixedNotes.Count);
        Assert.Equal(TickSpan.FromBounds(bar.Span.Onset, piece.Meter.GetBar(3).Span.End), options.Measure);
    }

    [Fact]
    public void Three_equal_notes_that_save_the_excess_are_offered_as_a_triplet()
    {
        ImportedPiece piece = Score(s_full, Note("C", 5, 2) + Note("D", 5, 2) + Note("E", 5, 2) + Note("F", 5, 2), s_full);

        RepairOptions options = Repairs(piece, 2, CheckReason.BarTooLong);

        RepairCandidate triplet = options.Candidates[0];
        Assert.Equal("barTooLong.triplet.2.R.1", triplet.Id);
        Assert.Equal([320L, 320L, 320L], triplet.Draft.Edits.Where(edit => edit.Kind == DraftEditKind.Change).Take(3).Select(edit => edit.After!.Value.Span.Duration));
    }

    [Fact]
    public void A_note_read_a_step_too_long_before_the_overflow_is_shortened()
    {
        // Homr's reading of Für Elise: bar 27 runs a 32nd long, its last E5 starting on the bar line.
        ImportedPiece piece = PieceImporter.Import("homr.musicxml", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"))).Piece!;
        ScanCheck check = ScanCheck.Of(piece);
        IReadOnlyList<Note> notes = check.Mark(piece.Notes, []);
        var open = new AssistantPiece(piece, notes, CheckFlag.FromScan(check, []), Words.Russian);

        RepairOptions options = CheckRepairs.For(open, open.Check.Single(flag => flag.Bar == 27 && flag.Reason == CheckReason.BarTooLong));

        RepairCandidate candidate = Assert.Single(options.Candidates);
        Assert.Equal("barTooLong.before.27.R.2", candidate.Id);
        Assert.Equal("Укоротить F5 (27.R.2) на 1/32 и сдвинуть следующие ноты раньше", candidate.Title);
        Bar bar = piece.Meter.GetBar(27);
        Assert.All(Accepted(notes, candidate.Draft).Where(note => note.Hand?.Value == Hand.Right && note.Span.Onset >= bar.Span.Onset && note.Span.Onset < piece.Meter.GetBar(28).Span.End), note => Assert.True(note.Span.End <= bar.Span.End));
        Assert.Equal(options.Candidates.Select(c => c.Draft.Edits), CheckRepairs.For(open, options.Flag).Candidates.Select(c => c.Draft.Edits));
    }

    [Fact]
    public void A_hand_short_by_half_its_last_note_is_offered_the_dot()
    {
        ImportedPiece piece = Score(s_full, Note("C", 5, 6) + Backup(6) + Note("C", 3, 4, staff: 2), s_full);

        RepairOptions options = Repairs(piece, 2, CheckReason.StavesDiffer);

        RepairCandidate dot = Assert.Single(options.Candidates);
        Assert.Equal("stavesDiffer.dot.2.L.1", dot.Id);
        DraftEdit edit = Assert.Single(dot.Draft.Edits);
        Assert.Equal(1440, edit.After!.Value.Span.Duration);
    }

    [Fact]
    public void A_note_beyond_the_piano_moves_by_octaves_or_goes()
    {
        ImportedPiece piece = Score(Note("C", 5, 2) + Note("G", 0, 4), s_full);

        RepairOptions options = Repairs(piece, 1, CheckReason.OutOfPianoRange);

        Assert.Equal(["outOfPianoRange.octave", "outOfPianoRange.remove"], options.Candidates.Select(candidate => candidate.Id));
        Assert.Equal(31, options.Candidates[0].Draft.Edits.Single().After!.Value.Pitch.MidiNumber);
        Assert.Equal(DraftEditKind.Remove, options.Candidates[1].Draft.Edits.Single().Kind);
    }

    [Fact]
    public void A_note_heard_twice_at_a_seam_is_merged()
    {
        ImportedPiece piece = Score(s_full, s_full);
        Note[] heard = [Played("h1", 72, 0, 240), Played("h2", 72, 60, 300), Played("h3", 74, 480, 240)];

        RepairOptions options = Repairs(piece, 1, CheckReason.PartSeam, heard);

        RepairCandidate merge = Assert.Single(options.Candidates);
        Assert.Equal([DraftEditKind.Remove, DraftEditKind.Change], merge.Draft.Edits.Select(edit => edit.Kind));
        Assert.Equal(360, merge.Draft.Edits[1].After!.Value.Span.Duration);
        Assert.NotNull(Repairs(piece, 1, CheckReason.PartSeam, [Played("h1", 72, 0, 240), Played("h3", 74, 480, 240)]).NoneReason);
    }

    [Fact]
    public void A_bar_off_the_tempo_is_offered_snapped_to_16ths_and_triplets()
    {
        ImportedPiece piece = Score(s_full, s_full);
        Note[] heard = [Played("t1", 72, 10, 100), Played("t2", 74, 130, 100), Played("t3", 76, 250, 100), Played("t4", 77, 370, 100)];

        RepairOptions options = Repairs(piece, 1, CheckReason.TempoUncertain, heard);

        Assert.Equal(["tempoUncertain.sixteenths", "tempoUncertain.triplets"], options.Candidates.Select(candidate => candidate.Id));
        Assert.Equal([0L, 120, 240, 360], options.Candidates[0].Draft.Variation.Notes.Select(note => note.Span.Onset.Value));
    }

    [Fact]
    public void The_quietest_of_crowded_notes_is_offered_and_equal_ones_are_not_guessed()
    {
        ImportedPiece piece = Score(s_full, s_full);
        Note[] heard = [Played("c1", 60, 0, 240, 80), Played("c2", 62, 0, 240, 30), Played("c3", 64, 0, 240, 70)];

        RepairOptions options = Repairs(piece, 1, CheckReason.DenseCluster, heard);
        RepairOptions equal = Repairs(piece, 1, CheckReason.DenseCluster, [.. heard.Select(note => note with { Velocity = 80 })]);

        Assert.Equal(["denseCluster.quiet.1.R.2", "denseCluster.quiet.1.R.3"], options.Candidates.Select(candidate => candidate.Id));
        Assert.Empty(equal.Candidates);
        Assert.Contains("as loud", equal.NoneReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CheckReason.GraceNotes)]
    [InlineData(CheckReason.PageQuality)]
    [InlineData(CheckReason.HandsGuessed)]
    [InlineData(CheckReason.RecordingOctave)]
    [InlineData(CheckReason.BarTooShort)]
    public void A_reason_the_notes_cannot_settle_gets_no_candidate_and_says_why(CheckReason reason)
    {
        ImportedPiece piece = Score(s_full, Note("C", 5, 4), s_full);

        RepairOptions options = Repairs(piece, 2, reason);

        Assert.Empty(options.Candidates);
        Assert.False(string.IsNullOrWhiteSpace(options.NoneReason));
    }

    [Fact]
    public void The_repair_tool_answers_titled_drafts_or_why_there_are_none()
    {
        ImportedPiece piece = Score(s_full, Note("E", 5, 1) + Note("D", 5, 1) + Note("D", 5, 1) + Note("C", 5, 1) + Note("B", 4, 1) + Note("A", 4, 1) + Note("G", 4, 1), s_full);
        var open = new AssistantPiece(piece, piece.Notes, [new CheckFlag(2, CheckSource.Scan, CheckReason.BarTooLong, false), new CheckFlag(2, CheckSource.Scan, CheckReason.GraceNotes, false)]);

        JsonElement drafted = Answer(open, "repair.candidates", """{"bar": 2, "reason": "barTooLong"}""");
        JsonElement none = Answer(open, "repair.candidates", """{"bar": 2, "reason": "graceNotes"}""");
        JsonElement missing = Answer(open, "repair.candidates", """{"bar": 1, "reason": "barTooLong"}""");

        Assert.Equal("Remove the repeated D5 (2.R.3)", drafted.GetProperty("drafts")[0].GetProperty("title").GetString());
        Assert.Equal(0, none.GetProperty("drafts").GetArrayLength());
        Assert.StartsWith("Grace notes are kept", none.GetProperty("noDraft").GetString(), StringComparison.Ordinal);
        Assert.False(missing.GetProperty("ok").GetBoolean());
    }
}