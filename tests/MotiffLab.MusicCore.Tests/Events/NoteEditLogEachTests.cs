using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Events;

/// <summary>The corrections laid over the notes one by one (version 0.5, «Мои правки: вкл / выкл»).</summary>
public class NoteEditLogEachTests
{
    private static Note Note(string id, int pitch, long onset, long duration = 480, int velocity = 80) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = velocity,
        Track = 0,
        Attribution = Attribution.Imported,
        Source = new MidiSourceLocation(1, 0, 0),
    };

    private static Note[] Imported() => [Note("n1", 60, 0), Note("n2", 62, 480), Note("n3", 64, 960)];

    private static NoteValues Values(int pitch, long onset, long duration = 480, int velocity = 80) => new(new Pitch(pitch), new TickSpan(new Tick(onset), duration), velocity);

    [Fact]
    public void A_log_that_fits_is_applied_as_the_strict_apply_applies_it()
    {
        Note[] imported = Imported();
        NoteEditLog log = NoteEditLog.Empty
            .Change(imported, "n2", Values(63, 600, 240, 100))
            .Remove(imported, "n3")
            .Add(imported, Values(67, 240), 0, Hand.Left, out _);

        EditsApplied each = log.ApplyEach(imported);

        Assert.True(each.AllFit);
        Assert.Equal([0, 1, 2], each.Applied);
        Assert.Equal(log.Apply(imported), each.Notes);
        Assert.Equal(["a1", "n2", "n3"], each.EditedIds(log).Order(StringComparer.Ordinal));
        Assert.Same(imported, NoteEditLog.Empty.ApplyEach(imported).Notes);
    }

    [Fact]
    public void A_correction_whose_note_moved_a_little_is_laid_on_the_nearest_note_of_its_pitch_and_moved_as_far()
    {
        Note[] before = Imported();
        NoteEditLog log = NoteEditLog.Empty.Change(before, "n2", Values(62, 520, 240, 100));
        // The notes came again with the second note 30 ticks later and another id, as a new transcription gives them.
        Note[] now = [Note("n1", 60, 0), Note("n7", 62, 510), Note("n3", 64, 960)];

        EditsApplied each = log.ApplyEach(now);

        MovedEdit moved = Assert.Single(each.Moved);
        Assert.Equal(("n7", 30L, EditMisfit.Missing), (moved.NoteId, moved.Shift, moved.Reason));
        Note laid = each.Notes.Single(note => note.Id == "n7");
        Assert.Equal(Values(62, 550, 240, 100), NoteValues.Of(laid));
        Assert.Equal(Provenance.UserEdited, laid.Attribution.Provenance);
        Assert.Empty(each.Unfit);
        Assert.Equal(["n2", "n7"], each.NamedFor("n7").Order(StringComparer.Ordinal));
        Assert.Throws<InvalidOperationException>(() => log.Apply(now));
    }

    [Fact]
    public void On_a_tie_the_lower_id_is_taken_and_a_later_correction_follows_the_first()
    {
        Note[] before = [Note("n5", 62, 480)];
        NoteEditLog log = NoteEditLog.Empty.Change(before, "n5", Values(62, 480, 480, 50));
        log = log.Change(before, "n5", Values(62, 480, 480, 30));
        Note[] now = [Note("n9", 62, 500), Note("n8", 62, 460)];

        EditsApplied each = log.ApplyEach(now);

        Assert.Equal(["n8", "n8"], each.Moved.Select(moved => moved.NoteId));
        Assert.Equal(30, each.Notes.Single(note => note.Id == "n8").Velocity);
        Assert.Equal(80, each.Notes.Single(note => note.Id == "n9").Velocity);
    }

    [Fact]
    public void A_correction_with_no_note_near_does_not_fit_and_says_why_and_the_others_still_apply()
    {
        Note[] before = Imported();
        NoteEditLog log = NoteEditLog.Empty
            .Change(before, "n1", Values(61, 0))
            .Change(before, "n2", Values(62, 480, 480, 20))
            .Remove(before, "n3");
        // The first note is now an E, the second is a thirty-second and a tick away, the third is gone.
        Note[] now = [Note("n1", 64, 0), Note("n2", 62, 480 + NoteEditLog.NearOnset + 1)];

        EditsApplied each = log.ApplyEach(now);

        Assert.Equal([(0, EditMisfit.Changed), (1, EditMisfit.Changed), (2, EditMisfit.Missing)], each.Unfit.Select(unfit => (unfit.Index, unfit.Reason)));
        Assert.Equal(now, each.Notes);
        Assert.Equal(3, log.Edits.Count);
    }

    [Fact]
    public void A_note_touched_by_one_correction_is_not_taken_by_another()
    {
        Note[] before = [Note("n1", 62, 0), Note("n2", 62, 40)];
        NoteEditLog log = NoteEditLog.Empty.Remove(before, "n1").Change(before, "n2", Values(62, 40, 480, 10));
        Note[] now = [Note("m1", 62, 20)];

        EditsApplied each = log.ApplyEach(now);

        Assert.Equal("m1", Assert.Single(each.Moved).NoteId);
        Assert.Equal(1, Assert.Single(each.Unfit).Index);
        Assert.Empty(each.Notes);
    }

    [Fact]
    public void An_added_note_whose_id_is_taken_does_not_fit()
    {
        var log = new NoteEditLog([new NoteEdit("n2", null, Values(60, 0, 240))]);

        EditsApplied each = log.ApplyEach(Imported());

        Assert.Equal(EditMisfit.IdTaken, Assert.Single(each.Unfit).Reason);
        Assert.Equal(Imported(), each.Notes);
    }

    [Fact]
    public void The_same_notes_and_log_give_the_same_result()
    {
        Note[] before = Imported();
        NoteEditLog log = NoteEditLog.Empty.Change(before, "n2", Values(62, 520)).Remove(before, "n1");
        Note[] now = [Note("x1", 60, 10), Note("x2", 62, 470)];

        EditsApplied first = log.ApplyEach(now);
        EditsApplied second = log.ApplyEach(now);

        Assert.Equal(first.Notes, second.Notes);
        Assert.Equal(first.Moved, second.Moved);
        Assert.Equal(first.Unfit, second.Unfit);
    }

    [Fact]
    public void Dropping_takes_out_only_the_corrections_that_do_not_fit()
    {
        Note[] before = Imported();
        NoteEditLog log = NoteEditLog.Empty.Change(before, "n1", Values(61, 0)).Change(before, "n2", Values(63, 480));
        Note[] now = [Note("n1", 60, 0)];

        NoteEditLog dropped = log.DropUnfit(now, ["n1", "n2"]);

        Assert.Equal("n1", Assert.Single(dropped.Edits).NoteId);
        Assert.Same(log, log.DropUnfit(now, ["n1"]));
        Assert.True(dropped.ApplyEach(now).AllFit);
    }

    [Fact]
    public void A_correction_can_be_made_while_another_no_longer_fits()
    {
        Note[] before = Imported();
        NoteEditLog log = NoteEditLog.Empty.Change(before, "n1", Values(61, 0));
        Note[] now = [Note("n1", 65, 0), Note("n2", 62, 480)];

        NoteEditLog more = log.Change(now, "n2", Values(62, 480, 480, 90));

        EditsApplied each = more.ApplyEach(now);
        Assert.Equal([1], each.Applied);
        Assert.Equal(90, each.Notes.Single(note => note.Id == "n2").Velocity);
    }
}