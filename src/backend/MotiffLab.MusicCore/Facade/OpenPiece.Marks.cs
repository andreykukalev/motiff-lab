using System.Text.Json;

using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The JSON adapter of the marks session (<see cref="MarksSession"/>, audit W1.7): the player's marks on the piece (ink,
/// margin notes, highlights, fingering), the bookmarks and their categories (0018, section 5; contract 2). Each call's
/// request read into typed values in the order it always was, the time read here («now», else the clock), the session
/// asked, the project it makes kept, and the marks or the bookmarks as they now are written as the answer; project.save
/// writes all of it. This file holds no state.
/// </summary>
internal sealed partial class OpenPiece
{
    internal static partial class Calls
    {
        /// <summary>The marks, the bookmarks and their categories.</summary>
        public static readonly FacadeCall<OpenPiece>[] Marks =
        [
            new("marks", static (piece, _) => piece.MarksAnswer(), CallFlags.Showing | CallFlags.OpenFirst),
            new("ink.add", static (piece, request) => piece.KeepMarks(MarksSession.WithInk(piece.State, [.. request.Elements("strokes").Select(ReadStroke)]))),
            new("ink.remove", static (piece, request) => piece.KeepMarks(MarksSession.WithoutInk(piece.State, request.Texts("ids")))),
            new("ink.clear", static (piece, _) => piece.KeepMarks(MarksSession.WithInkCleared(piece.State))),
            new("note.add", static (piece, request) => piece.AddMarginNote(request)),
            new("note.change", static (piece, request) => piece.ChangeMarginNote(request)),
            new("note.remove", static (piece, request) => piece.KeepMarks(MarksSession.WithoutNote(piece.State, request.Required("id")))),
            new("highlight.add", static (piece, request) => piece.AddHighlight(request)),
            new("highlight.remove", static (piece, request) => piece.KeepMarks(MarksSession.WithoutHighlight(piece.State, request.Required("id")))),
            new("fingering.set", static (piece, request) => piece.SetFingering(request)),
            new("fingering.remove", static (piece, request) => piece.KeepMarks(MarksSession.WithoutFingering(piece.State, request.Required("noteId")))),
            new("bookmarks", static (piece, _) => piece.BookmarksAnswer(), CallFlags.OpenFirst),
            new("bookmark.add", static (piece, request) => piece.AddBookmark(request)),
            new("bookmark.change", static (piece, request) => piece.ChangeBookmark(request)),
            new("bookmark.move", static (piece, request) => piece.MoveBookmark(request)),
            new("bookmark.remove", static (piece, request) => piece.KeepBookmarks(MarksSession.WithoutBookmark(piece.State, request.Required("id")))),
            new("bookmark.structure", static (piece, request) => piece.KeepBookmarks(MarksSession.WithStructureBookmarks(piece.State, request.Now()))),
            new("bookmark.form", static (piece, request) => piece.KeepBookmarks(MarksSession.WithFormBookmarks(piece.State, request.Now()))),
            new("category.add", static (piece, request) => piece.AddCategory(request)),
            new("category.rename", static (piece, request) => piece.RenameCategory(request)),
            new("category.remove", static (piece, request) => piece.KeepBookmarks(MarksSession.WithoutCategory(piece.State, request.Required("id")))),
        ];
    }

    /// <summary>The <paramref name="project"/> a change of the marks makes kept, and the marks answered.</summary>
    private string KeepMarks(Project project)
    {
        Project = project;
        return MarksAnswer();
    }

    private string MarksAnswer() => Answer.Write(json => Answer.Marks(json, Project.Marks, Piece.Meter));

    /// <summary>The <paramref name="project"/> a change of the bookmarks or their categories makes kept, and the bookmarks answered.</summary>
    private string KeepBookmarks(Project project)
    {
        Project = project;
        return BookmarksAnswer();
    }

    private string BookmarksAnswer() => Answer.Write(json => Answer.Bookmarks(json, Project, Piece.Meter, Words));

    /// <summary>A stroke in the pencil layer's shape; refused when it cannot be read.</summary>
    private static InkStroke ReadStroke(JsonElement stroke)
    {
        try
        {
            return ProjectFile.ReadStroke(stroke);
        }
        catch (ProjectFileException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    /// <summary>The span a mark is about: "span" {onset, end}, "bars" {first, last} or "phrase" (a number).</summary>
    private TickSpan Span(Request request)
    {
        if (request.Span("span") is { } span)
        {
            return span;
        }

        if (request.Int("phrase") is int number)
        {
            return MarksSession.Phrase(State, number);
        }

        Request bars = request.Object("bars");
        int first = bars.Int("first") ?? throw FacadeException.BadRequest("Send \"span\" {onset, end}, \"bars\" {first, last} or \"phrase\" (a number).");
        return MarksSession.Bars(State, first, bars.Int("last") ?? first);
    }

    /// <summary>The place (<see cref="Span"/>), "text" and "now": a margin note.</summary>
    private string AddMarginNote(Request request)
    {
        TickSpan span = Span(request);
        string text = request.Required("text");
        return KeepMarks(MarksSession.WithNote(State, span, text, request.Now()));
    }

    /// <summary>"id" and "text".</summary>
    private string ChangeMarginNote(Request request)
    {
        string id = request.Required("id");
        return KeepMarks(MarksSession.WithNoteChanged(State, id, request.Required("text")));
    }

    /// <summary>The place (<see cref="Span"/>), "notes" (ids) and "colour" (amber unless sent).</summary>
    private string AddHighlight(Request request)
    {
        TickSpan span = Span(request);
        IReadOnlyList<string> notes = request.Texts("notes");
        return KeepMarks(MarksSession.WithHighlight(State, span, notes, request.Enum<InkColour>("colour")));
    }

    /// <summary>"noteId" and "finger", 1 to 5.</summary>
    private string SetFingering(Request request)
    {
        string noteId = request.Required("noteId");
        int finger = request.Int("finger") ?? throw FacadeException.BadRequest("The fingering needs \"finger\", 1 to 5.");
        return KeepMarks(MarksSession.WithFingering(State, noteId, finger));
    }

    /// <summary>The place (<see cref="Span"/>), "title", "category" and "now" (<see cref="MarksSession.WithBookmark"/>).</summary>
    private string AddBookmark(Request request)
    {
        TickSpan span = Span(request);
        bool phraseNamed = request.Has("phrase");
        string? title = request.Text("title");
        string? category = request.Text("category");
        return KeepBookmarks(MarksSession.WithBookmark(State, span, phraseNamed, title, category, request.Now()));
    }

    /// <summary>"id", and any of a new place (<see cref="Span"/>), "title" and "category" (<see cref="MarksSession.WithBookmarkChanged"/>).</summary>
    private string ChangeBookmark(Request request)
    {
        string id = request.Required("id");
        TickSpan? span = request.Has("span") || request.Has("bars") || request.Has("phrase") ? Span(request) : null;
        Bookmark bookmark = MarksSession.Bookmark(State, id);
        string? title = request.Text("title");
        string? category = request.Text("category");
        return KeepBookmarks(MarksSession.WithBookmarkChanged(State, bookmark, title, category, span, request.Has("phrase")));
    }

    /// <summary>"id" and "index", the place in the player's order.</summary>
    private string MoveBookmark(Request request)
    {
        string id = request.Required("id");
        int index = request.Int("index") ?? throw FacadeException.BadRequest("Send \"index\", the place in your order.");
        return KeepBookmarks(MarksSession.WithBookmarkMoved(State, id, index));
    }

    /// <summary>"name" and "ink" (graphite unless sent): a category of the player's own.</summary>
    private string AddCategory(Request request)
    {
        string name = request.Required("name");
        return KeepBookmarks(MarksSession.WithCategory(State, name, request.Enum<InkColour>("ink")));
    }

    /// <summary>"id" and "name" (blank gives a built-in category its own name back).</summary>
    private string RenameCategory(Request request)
    {
        string id = request.Required("id");
        return KeepBookmarks(MarksSession.WithCategoryRenamed(State, id, request.Text("name")));
    }
}