using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade.Sessions;

/// <summary>
/// The player's marks on the piece and the bookmarks (audit W1.7, the fifth typed session; 0018, section 5; contract 2): ink,
/// margin notes, highlights and fingering; the bookmarks in the player's order, the Structure bookmarks written from the
/// sentences or the form, and the bookmarks' categories. The marks and the bookmarks are the project's: each change here
/// answers the project it makes, and the open piece keeps it. The session keeps no state of its own; it reads the piece
/// through <see cref="SessionPiece"/>, takes typed values (the time as <c>now</c>), never a request, and returns typed
/// results; it writes no JSON and reads no clock. OpenPiece.Marks.cs is its JSON adapter.
/// </summary>
internal static class MarksSession
{
    /// <summary>The span of the phrase <paramref name="number"/>; refused when the piece has none so.</summary>
    public static TickSpan Phrase(SessionPiece piece, int number) =>
        (piece.Structure.Levels.Phrases.Phrases.FirstOrDefault(phrase => phrase.Number == number) ?? throw FacadeException.BadRequest($"There is no phrase {number}.")).Span;

    /// <summary>The span of the bars <paramref name="first"/> to <paramref name="last"/>; refused when the piece has no such bars.</summary>
    public static TickSpan Bars(SessionPiece piece, int first, int last)
    {
        try
        {
            return MotifSelector.Bars(piece.Piece.Meter, first, last, piece.Piece.End);
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }

    /// <summary>"bars 1–8" / "такты 1–8": the title a bookmark starts with, and the name of an idea kept with a note alone.</summary>
    public static string BarsTitle(SessionPiece piece, TickSpan span)
    {
        int first = piece.Piece.Meter.BarAt(span.Onset).Number;
        int last = piece.Piece.Meter.BarAt(new Tick(span.End.Value - 1)).Number;
        return first == last ? piece.Words.T($"bar {first}", $"такт {first}") : piece.Words.T($"bars {first}–{last}", $"такты {first}–{last}");
    }

    /// <summary>The project with the <paramref name="strokes"/> added after the ink there, thinned as the ink is kept; refused when two strokes share an id.</summary>
    public static Project WithInk(SessionPiece piece, IReadOnlyList<InkStroke> strokes) => Marked(piece, marks => marks.AddInk(strokes));

    /// <summary>The project without the strokes <paramref name="ids"/>; ids not there are ignored.</summary>
    public static Project WithoutInk(SessionPiece piece, IReadOnlyList<string> ids) => Marked(piece, marks => marks.RemoveInk(ids));

    /// <summary>The project with no ink at all.</summary>
    public static Project WithInkCleared(SessionPiece piece) => Marked(piece, marks => marks.WithInk([]));

    /// <summary>The project with a margin note of <paramref name="text"/> over <paramref name="span"/>, written <paramref name="now"/>; refused when the text is blank.</summary>
    public static Project WithNote(SessionPiece piece, TickSpan span, string text, DateTimeOffset now) =>
        Marked(piece, marks => marks.AddNote(new MarginNote(marks.NextNoteId(), span, text, now)));

    /// <summary>The project with the margin note <paramref name="id"/> saying <paramref name="text"/>; refused when there is no such note or the text is blank.</summary>
    public static Project WithNoteChanged(SessionPiece piece, string id, string text) => Marked(piece, marks => marks.ChangeNote(id, text));

    /// <summary>The project without the margin note <paramref name="id"/>; refused when there is none.</summary>
    public static Project WithoutNote(SessionPiece piece, string id) => Marked(piece, marks => marks.RemoveNote(id));

    /// <summary>The project with the <paramref name="notes"/> over <paramref name="span"/> highlighted in <paramref name="colour"/>, amber (what the player marked) unless given.</summary>
    public static Project WithHighlight(SessionPiece piece, TickSpan span, IReadOnlyList<string> notes, InkColour? colour) =>
        Marked(piece, marks => marks.AddHighlight(new Highlight(marks.NextHighlightId(), span, notes, colour ?? InkColour.Amber)));

    /// <summary>The project without the highlight <paramref name="id"/>; refused when there is none.</summary>
    public static Project WithoutHighlight(SessionPiece piece, string id) => Marked(piece, marks => marks.RemoveHighlight(id));

    /// <summary>The project with <paramref name="finger"/> (1 to 5) written on the note <paramref name="noteId"/>, in place of any finger there.</summary>
    public static Project WithFingering(SessionPiece piece, string noteId, int finger) => Marked(piece, marks => marks.SetFingering(new Fingering(noteId, finger)));

    /// <summary>The project without a finger on the note <paramref name="noteId"/>; nothing changes when there was none.</summary>
    public static Project WithoutFingering(SessionPiece piece, string noteId) => Marked(piece, marks => marks.RemoveFingering(noteId));

    /// <summary>
    /// The project with a bookmark over <paramref name="span"/> at the end of the player's order, made <paramref name="now"/>:
    /// titled <paramref name="title"/>, else by its bars (<see cref="BarsTitle"/>); in <paramref name="category"/>, else
    /// Practise; from a phrase when the player named one (<paramref name="phraseNamed"/>) or a phrase spans the same bars.
    /// </summary>
    public static Project WithBookmark(SessionPiece piece, TickSpan span, bool phraseNamed, string? title, string? category, DateTimeOffset now) =>
        Changed(() => piece.Project.AddBookmark(new Bookmark(piece.Project.NextBookmarkId(), title ?? BarsTitle(piece, span), category ?? BookmarkCategory.PractiseId, span, FromPhrase(piece, span, phraseNamed), now)));

    /// <summary>The bookmark <paramref name="id"/>; refused when there is none.</summary>
    public static Bookmark Bookmark(SessionPiece piece, string id) =>
        piece.Project.FindBookmark(id) ?? throw FacadeException.BadRequest($"There is no bookmark {id}.");

    /// <summary>
    /// The project with the <paramref name="bookmark"/> retitled, put in another category or moved to another
    /// <paramref name="span"/> (from a phrase as <see cref="WithBookmark"/> says), what is not given kept; its id, its place
    /// in the order and when it was made stay.
    /// </summary>
    public static Project WithBookmarkChanged(SessionPiece piece, Bookmark bookmark, string? title, string? category, TickSpan? span, bool phraseNamed) =>
        Changed(() => piece.Project.ChangeBookmark(bookmark.Id, kept => new Bookmark(
            kept.Id,
            title ?? kept.Title,
            category ?? kept.CategoryId,
            span ?? kept.Span,
            span is { } moved ? FromPhrase(piece, moved, phraseNamed) : kept.FromPhrase,
            kept.CreatedAt)));

    /// <summary>The project with the bookmark <paramref name="id"/> at <paramref name="index"/> in the player's order.</summary>
    public static Project WithBookmarkMoved(SessionPiece piece, string id, int index) => Changed(() => piece.Project.MoveBookmark(id, index));

    /// <summary>The project without the bookmark <paramref name="id"/>.</summary>
    public static Project WithoutBookmark(SessionPiece piece, string id) => Changed(() => piece.Project.RemoveBookmark(id));

    /// <summary>The project with a Structure bookmark for every sentence not bookmarked there yet, made <paramref name="now"/>.</summary>
    public static Project WithStructureBookmarks(SessionPiece piece, DateTimeOffset now) =>
        Changed(() => piece.Project.AddStructureBookmarks(piece.Structure.Levels.Sentences.Sentences, piece.Words, now));

    /// <summary>The project with a Structure bookmark for every section of the form not bookmarked there yet, made <paramref name="now"/>.</summary>
    public static Project WithFormBookmarks(SessionPiece piece, DateTimeOffset now) =>
        Changed(() => piece.Project.AddFormBookmarks(piece.Structure.Sections, piece.Words, now));

    /// <summary>The project with a category of the player's own called <paramref name="name"/>, in <paramref name="ink"/> (graphite unless given).</summary>
    public static Project WithCategory(SessionPiece piece, string name, InkColour? ink) =>
        Changed(() => piece.Project.AddCategory(new BookmarkCategory(piece.Project.NextCategoryId(), name, ink ?? InkColour.Graphite)));

    /// <summary>The project with the category <paramref name="id"/> renamed; blank gives a built-in one its own name back.</summary>
    public static Project WithCategoryRenamed(SessionPiece piece, string id, string? name) => Changed(() => piece.Project.RenameCategory(id, name));

    /// <summary>The project without the player's category <paramref name="id"/>, its bookmarks moved to Practise; the built-in ones stay.</summary>
    public static Project WithoutCategory(SessionPiece piece, string id) => Changed(() => piece.Project.RemoveCategory(id));

    /// <summary>Whether a bookmark over <paramref name="span"/> is from a phrase: the player named one, or a phrase spans the same bars.</summary>
    private static bool FromPhrase(SessionPiece piece, TickSpan span, bool phraseNamed) =>
        phraseNamed || piece.Structure.Levels.Phrases.Phrases.Any(phrase => phrase.Span == span);

    private static Project Marked(SessionPiece piece, Func<Marks, Marks> change) => Changed(() => piece.Project.ChangeMarks(change));

    /// <summary>The project <paramref name="change"/> makes; a change the project refuses is a bad request.</summary>
    private static Project Changed(Func<Project> change)
    {
        try
        {
            return change();
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }
}