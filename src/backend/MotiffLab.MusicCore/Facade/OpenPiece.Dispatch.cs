using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>The calls on an open piece with notes that the piece answers itself (<see cref="CallRegistry.OnPiece"/>).</summary>
internal sealed partial class OpenPiece
{
    /// <summary>The piece's calls by name, each with its handler here and its flags, a list to a feature.</summary>
    internal static partial class Calls
    {
        /// <summary>The piece: its phrases, notation, playback, roll, tempo and saving.</summary>
        public static readonly FacadeCall<OpenPiece>[] Piece =
        [
            new("piece", static (piece, _) => Answer.Write(json => Answer.Piece(json, piece.Piece, piece.Notes, withNotes: true, piece.Words)), CallFlags.Showing | CallFlags.OpenFirst),
            new("language", static (piece, request) => piece.Language(request), CallFlags.Showing),
            new("phrases", static (piece, request) => Answer.Write(json => Answer.Phrases(json, piece.Levels, piece.Structure.Sections, LevelOf(request) ?? piece.Level, piece.Words, piece.Piece.Meter)), CallFlags.Showing | CallFlags.OpenFirst),
            new("phrases.edit", static (piece, request) => piece.EditPhrases(request)),
            new("notation", static (piece, request) => piece.Notation(request), CallFlags.Showing | CallFlags.OpenFirst),
            new("playback", static (piece, request) => piece.Playback(request), CallFlags.Showing | CallFlags.OpenFirst),
            new("tempo.set", static (piece, request) => piece.SetTempo(request)),
            new("roll", static (piece, request) => piece.Roll(request), CallFlags.Showing | CallFlags.OpenFirst),
            new("project.save", static (piece, request) => piece.Save(request)),
            new("reference.key", static (piece, request) => ReferenceRequest.Key(request, PieceDna.OpeningKey(piece.Notes, piece.Piece.Meter, piece.Piece.Keys, piece.Piece.End))),
        ];

        /// <summary>What the project keeps with the piece: marks, bookmarks, resume points, ideas, settings, Check, pieces, tempo.</summary>
        public static readonly FacadeCall<OpenPiece>[] Project =
        [
            new("marks", static (piece, _) => piece.MarksAnswer(), CallFlags.Showing | CallFlags.OpenFirst),
            new("ink.add", static (piece, request) => piece.Marks(marks => marks.AddInk(request.Elements("strokes").Select(ReadStroke)))),
            new("ink.remove", static (piece, request) => piece.Marks(marks => marks.RemoveInk(request.Texts("ids")))),
            new("ink.clear", static (piece, _) => piece.Marks(marks => marks.WithInk([]))),
            new("note.add", static (piece, request) => piece.Marks(marks => marks.AddNote(new MarginNote(marks.NextNoteId(), piece.Span(request), request.Required("text"), Now(request))))),
            new("note.change", static (piece, request) => piece.Marks(marks => marks.ChangeNote(request.Required("id"), request.Required("text")))),
            new("note.remove", static (piece, request) => piece.Marks(marks => marks.RemoveNote(request.Required("id")))),
            new("highlight.add", static (piece, request) => piece.Marks(marks => marks.AddHighlight(new Highlight(marks.NextHighlightId(), piece.Span(request), request.Texts("notes"), request.Enum<InkColour>("colour") ?? InkColour.Amber)))),
            new("highlight.remove", static (piece, request) => piece.Marks(marks => marks.RemoveHighlight(request.Required("id")))),
            new("fingering.set", static (piece, request) => piece.Marks(marks => marks.SetFingering(new Fingering(request.Required("noteId"), request.Int("finger") ?? throw FacadeException.BadRequest("The fingering needs \"finger\", 1 to 5."))))),
            new("fingering.remove", static (piece, request) => piece.Marks(marks => marks.RemoveFingering(request.Required("noteId")))),
            new("bookmarks", static (piece, _) => piece.BookmarksAnswer(), CallFlags.OpenFirst),
            new("bookmark.add", static (piece, request) => piece.AddBookmark(request)),
            new("bookmark.change", static (piece, request) => piece.ChangeBookmark(request)),
            new("bookmark.move", static (piece, request) => piece.Bookmarks(project => project.MoveBookmark(request.Required("id"), request.Int("index") ?? throw FacadeException.BadRequest("Send \"index\", the place in your order.")))),
            new("bookmark.remove", static (piece, request) => piece.Bookmarks(project => project.RemoveBookmark(request.Required("id")))),
            new("bookmark.structure", static (piece, request) => piece.Bookmarks(project => project.AddStructureBookmarks(piece.Levels.Sentences.Sentences, piece.Words, Now(request)))),
            new("bookmark.form", static (piece, request) => piece.Bookmarks(project => project.AddFormBookmarks(piece.Structure.Sections, piece.Words, Now(request)))),
            new("category.add", static (piece, request) => piece.Bookmarks(project => project.AddCategory(new BookmarkCategory(project.NextCategoryId(), request.Required("name"), request.Enum<InkColour>("ink") ?? InkColour.Graphite)))),
            new("category.rename", static (piece, request) => piece.Bookmarks(project => project.RenameCategory(request.Required("id"), request.Text("name")))),
            new("category.remove", static (piece, request) => piece.Bookmarks(project => project.RemoveCategory(request.Required("id")))),
            new("resume", static (piece, _) => Answer.Write(json => Answer.ResumePoints(json, piece.Project, piece.Piece.Meter)), CallFlags.OpenFirst),
            new("resume.save", static (piece, request) => piece.SaveResumePoint(request)),
            new("resume.rename", static (piece, request) => piece.Resume(project => project.ChangeResumePoint(request.Required("id"), point => point.Rename(request.Text("name"))))),
            new("resume.remove", static (piece, request) => piece.Resume(project => project.RemoveResumePoint(request.Required("id")))),
            new("ideas", static (piece, _) => piece.IdeasAnswer(), CallFlags.OpenFirst),
            new("idea.keep", static (piece, request) => piece.KeepIdea(request)),
            new("idea.rename", static (piece, request) => piece.Ideas(project => project.RenameObservation(request.Required("id"), request.Required("name")))),
            new("idea.note", static (piece, request) => piece.Ideas(project => project.ChangeObservationNote(request.Required("id"), request.Text("note")))),
            new("idea.remove", static (piece, request) => piece.Ideas(project => project.DeleteObservation(request.Required("id")))),
            new("idea.open", static (piece, request) => piece.OpenIdea(request)),
            new("settings", static (piece, _) => Answer.Write(json =>
            {
                Answer.SettingsCatalogue(json);
                Answer.Overrides(json, piece.Project);
            })),
            new("settings.override", static (piece, request) => piece.Change(project => project.OverrideSetting(request.Required("id"), request.Text("value")), json => Answer.Overrides(json, piece.Project))),
            new("project.metadata", static (piece, request) => piece.Change(project => MetadataRequest.Apply(project, request), json => Answer.Project(json, "project", piece.Project))),
            new("check", static (piece, _) => Answer.Write(piece.CheckAnswer), CallFlags.Showing | CallFlags.OpenFirst),
            new("check.resolve", static (piece, request) => piece.Change(project => PiecesRequest.Resolve(project, request), piece.CheckAnswer)),
            new("pieces.find", static (piece, request) => Answer.Write(json => PiecesRequest.Find(json, piece.Project, piece.Piece, piece.Notes, piece.Words, request))),
            new("pieces.save", static (piece, request) => piece.Change(project => PiecesRequest.Save(project, piece.Piece, piece.Words, Now(request), request), json => PiecesRequest.Layer(json, piece.Project, piece.Piece))),
            new("tempo", static (piece, _) => Answer.Write(json => TempoRequest.Write(json, piece.Project, piece.Piece, piece.Words))),
        ];

        /// <summary>The performances.</summary>
        public static readonly FacadeCall<OpenPiece>[] Performances =
        [
            new("performances", static (piece, _) => Answer.Write(json => Answer.Performances(json, piece.Project)), CallFlags.OpenFirst),
            new("performance", static (piece, request) => piece.PerformanceAnswer(piece.Performance(request)), CallFlags.OpenFirst),
            new("performance.add", static (piece, request) => piece.AddPerformance(request)),
            new("performance.rename", static (piece, request) => piece.ChangePerformance(request, performance => performance with { Name = request.Required("name") })),
            new("performance.dismiss", static (piece, request) => piece.ChangePerformance(request, performance => performance.Dismiss(BarOf(request), request.Bool("dismissed") ?? true))),
            new("performance.take", static (piece, request) => piece.TakeBars(request)),
            new("performance.takeAll", static (piece, request) => piece.TakeAll(request)),
            new("performance.restore", static (piece, request) => piece.ChangePerformance(request, performance => performance.Restore(BarsOf(request)), notesChange: true)),
        ];

        /// <summary>The sources that need the notes (those that need none: <see cref="SourceRequest.Calls"/>).</summary>
        public static readonly FacadeCall<OpenPiece>[] Sources =
        [
            new("sources.document", static (piece, request) => piece.KeepDocument(request)),
            new("sources.documentNotes", static (piece, request) => piece.AddDocumentNotes(request)),
            new("sources.pdf", static (piece, request) => request.Text("id") is { } id && id != PieceOriginal.DocumentId ? piece.OriginalBytes(id) : piece.DocumentBytes()),
            new("sources.relink", static (piece, request) => piece.RelinkDocument(request)),
            new("sources.held", static (piece, request) => piece.MarkHeld(request)),
            new("sources.notes.recognise", static (piece, request) => piece.RecogniseAgain(request)),
        ];
    }
}