using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Facade.Sessions;

/// <summary>
/// The corrections session typed (audit W1.7): the note editor's draft, the layer and a bar's corrections, with no request
/// and no JSON. What the iPad reads of the same calls is pinned by tests/contracts/facade-answers (FacadeAnswersContractTests).
/// </summary>
public class CorrectionsSessionTests
{
    private static OpenPiece Example() => OpenPiece.Import(
        "p_session",
        "FurElise.musicxml",
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml")),
        Words.English,
        new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero), IdSource.Numbered);

    /// <summary>The piece as it reads with <paramref name="edits"/> kept, as the open piece keeps a log the session made.</summary>
    private static SessionPiece With(SessionPiece piece, NoteEditLog edits) => piece with { Project = piece.Project with { Edits = edits } };

    [Fact]
    public void The_draft_steps_back_and_is_put_back_while_the_file_keeps_the_corrections_it_began_from()
    {
        OpenPiece open = Example();
        CorrectionsSession session = open.Corrections;
        SessionPiece piece = open.State;
        session.Begin(piece);

        Note first = session.Correctable(piece, piece.Notes[0].Id);
        NoteEditLog removed = Assert.IsType<NoteEditLog>(session.Remove(piece, first));
        SessionPiece after = With(piece, removed);
        Assert.Same(piece.Project.Edits, session.Kept(after));
        Assert.True(session.Bar(after, piece.Piece.Meter.GetBar(1)).CanUndo);

        Assert.Same(piece.Project.Edits, session.Undo(after));
        Assert.False(session.Bar(piece, piece.Piece.Meter.GetBar(1)).CanUndo);
        Assert.Null(session.End(after, keep: true));
        Assert.False(session.Editing);
        Assert.Same(removed, session.Kept(after));
        Assert.Throws<FacadeException>(() => session.DraftBegun());
    }

    [Fact]
    public void Off_the_layer_shows_the_notes_as_imported_and_refuses_a_correction()
    {
        OpenPiece open = Example();
        CorrectionsSession session = open.Corrections;
        SessionPiece piece = open.State;
        NoteEditLog removed = Assert.IsType<NoteEditLog>(session.Remove(piece, piece.Notes[0]));
        SessionPiece after = With(piece, removed);
        Assert.Null(session.Laid(after));
        Assert.DoesNotContain(session.Corrected(after), note => note.Id == piece.Notes[0].Id);

        Assert.True(session.Switch(on: false));
        Assert.False(session.Switch(on: false));
        Assert.Same(piece.Piece.Notes, session.Corrected(after));
        Assert.True(session.Laid(after)?.Off);
        Assert.Equal("unavailable", Assert.Throws<FacadeException>(() => session.RefuseWhileOff(piece.Words)).Code);
    }

    [Fact]
    public void An_accepted_draft_is_undone_whole_only_while_nothing_came_after_it()
    {
        OpenPiece open = Example();
        CorrectionsSession session = open.Corrections;
        SessionPiece piece = open.State;
        NoteEditLog variant = Assert.IsType<NoteEditLog>(session.TakeVariant(piece, piece.Piece.Meter.GetBar(1), [new VariantStaff(0, 1, [])]));
        SessionPiece after = With(piece, variant);
        Assert.Same(piece.Project.Edits, session.AcceptedBefore(after));

        NoteEditLog more = Assert.IsType<NoteEditLog>(session.Remove(after, session.Corrected(after)[0]));
        Assert.Throws<FacadeException>(() => session.AcceptedBefore(With(after, more)));
        Assert.Same(piece.Project.Edits, session.UndoDraft(after));
        Assert.Throws<FacadeException>(() => session.AcceptedBefore(piece));
    }

    [Fact]
    public void A_note_on_the_first_tick_cannot_start_earlier()
    {
        OpenPiece open = Example();
        SessionPiece piece = open.State;
        Note opening = piece.Notes.First(note => note.Span.Onset.Value == 0);

        CorrectionNote note = open.Corrections.Bar(piece, piece.Piece.Meter.BarAt(opening.Span.Onset)).Notes.Single(each => each.Note.Id == opening.Id);

        Assert.False(note.CanEarlier);
        Assert.False(note.Edited);
    }
}