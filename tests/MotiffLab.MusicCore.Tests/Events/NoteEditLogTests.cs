using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Events;

/// <summary>Corrections of imported notes with an edit log (S1-I7, #19).</summary>
public class NoteEditLogTests
{
    private static Note Note(string id, int pitch, long onset, long duration = 480, int velocity = 80) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = velocity,
        Track = 0,
        Hand = new Attributed<Hand>(Hand.Right, Attribution.Inferred(0.8)),
        Attribution = Attribution.Imported,
        Source = new MidiSourceLocation(1, 0, 0),
    };

    private static Note[] Imported() => [Note("n1", 60, 0), Note("n2", 62, 480), Note("n3", 64, 960)];

    [Fact]
    public void A_change_marks_the_note_user_edited_and_leaves_the_imported_notes_as_they_were()
    {
        Note[] imported = Imported();
        NoteValues after = new(new Pitch(63), new TickSpan(new Tick(600), 240), 100);

        NoteEditLog log = NoteEditLog.Empty.Change(imported, "n2", after);
        IReadOnlyList<Note> notes = log.Apply(imported);

        Note changed = notes.Single(note => note.Id == "n2");
        Assert.Equal(after, NoteValues.Of(changed));
        Assert.Equal(Provenance.UserEdited, changed.Attribution.Provenance);
        Assert.Equal(imported[1].Hand, changed.Hand);
        Assert.Equal(imported[1].Source, changed.Source);
        Assert.Equal(new NoteEdit("n2", NoteValues.Of(imported[1]), after), Assert.Single(log.Edits));
        Assert.Equal(Imported(), imported);
    }

    [Fact]
    public void Undo_restores_the_imported_value_exactly()
    {
        Note[] imported = Imported();
        NoteEditLog log = NoteEditLog.Empty
            .Change(imported, "n1", NoteValues.Of(imported[0]) with { Pitch = new Pitch(61) })
            .Remove(imported, "n3");

        Assert.Equal(2, log.Apply(imported).Count);
        Assert.Equal(imported, log.Undo().Undo().Apply(imported));
        Assert.Equal(imported[0], log.Undo().Undo().Apply(imported)[0]);
        Assert.Throws<InvalidOperationException>(() => NoteEditLog.Empty.Undo());
    }

    [Fact]
    public void Revert_drops_every_edit_of_one_note_and_keeps_the_others()
    {
        Note[] imported = Imported();
        NoteEditLog log = NoteEditLog.Empty
            .Change(imported, "n1", NoteValues.Of(imported[0]) with { Velocity = 20 })
            .Change(imported, "n2", NoteValues.Of(imported[1]) with { Velocity = 30 });
        log = log.Change(imported, "n1", NoteValues.Of(log.Apply(imported)[0]) with { Velocity = 40 });

        NoteEditLog reverted = log.Revert("n1");

        Assert.Equal(imported[0], reverted.Apply(imported)[0]);
        Assert.Equal(30, reverted.Apply(imported)[1].Velocity);
        Assert.Equal(["n1", "n2"], log.EditedIds);
    }

    [Fact]
    public void Edits_build_on_each_other_and_notes_stay_in_onset_order()
    {
        Note[] imported = Imported();
        NoteEditLog log = NoteEditLog.Empty.Change(imported, "n1", NoteValues.Of(imported[0]) with { Span = new TickSpan(new Tick(1200), 240) });

        Assert.Equal(["n2", "n3", "n1"], log.Apply(imported).Select(note => note.Id));
    }

    [Fact]
    public void An_unchanged_value_adds_nothing_and_bad_values_are_refused()
    {
        Note[] imported = Imported();

        Assert.Same(NoteEditLog.Empty, NoteEditLog.Empty.Change(imported, "n1", NoteValues.Of(imported[0])));
        Assert.Throws<ArgumentException>(() => NoteEditLog.Empty.Change(imported, "n1", NoteValues.Of(imported[0]) with { Velocity = 0 }));
        Assert.Throws<ArgumentException>(() => NoteEditLog.Empty.Change(imported, "n9", NoteValues.Of(imported[0])));
        Assert.Throws<ArgumentException>(() => NoteEditLog.Empty.Remove(imported, "n1").Remove(imported, "n1"));
    }

    [Fact]
    public void A_log_that_no_longer_fits_the_notes_is_refused()
    {
        Note[] imported = Imported();
        var stale = new NoteEditLog([new NoteEdit("n1", NoteValues.Of(imported[0]) with { Velocity = 5 }, null)]);

        Assert.Throws<InvalidOperationException>(() => stale.Apply(imported));
        Assert.Throws<InvalidOperationException>(() => new NoteEditLog([new NoteEdit("n9", NoteValues.Of(imported[0]), null)]).Apply(imported));
    }

    [Fact]
    public void Logs_compare_by_value()
    {
        Note[] imported = Imported();

        Assert.Equal(NoteEditLog.Empty.Remove(imported, "n2"), NoteEditLog.Empty.Remove(imported, "n2"));
        Assert.NotEqual(NoteEditLog.Empty.Remove(imported, "n2"), NoteEditLog.Empty.Remove(imported, "n3"));
    }
}