using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade.Sessions;

/// <summary>
/// The open piece as a session reads it at one call (audit W1.7): its project, the imported piece, the notes with the
/// corrections and the bars taken applied, its structure and the language. The open piece owns all of it and hands a
/// session this view; a session never keeps it past the call, so it always reads the piece as it is now.
/// </summary>
internal readonly record struct SessionPiece(Project Project, ImportedPiece Piece, IReadOnlyList<Note> Notes, PieceStructure Structure, Words Words)
{
    /// <summary>The recognised notes still to check: in the bars with an open Check flag about the notes (#352).</summary>
    public NoteDoubt Doubt() => NoteDoubt.Of(Notes, Piece.Meter, CheckFlag.DoubtfulBars(Project.Check), Words);
}