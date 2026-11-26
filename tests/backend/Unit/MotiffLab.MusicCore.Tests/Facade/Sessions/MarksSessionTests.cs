using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Facade.Sessions;

/// <summary>
/// The marks session typed (audit W1.7): marks, bookmarks and categories each answer the project they make, made at the
/// time handed in, with no request, no JSON and no clock (the time is read by the adapter, <see cref="RequestTests"/>). What
/// the iPad reads of the same calls is pinned by tests/contracts/facade-answers (FacadeAnswersContractTests).
/// </summary>
public class MarksSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 30, 0, TimeSpan.Zero);

    private static SessionPiece Example(Words words) => OpenPiece.Import(
        "p_session",
        "FurElise.musicxml",
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml")),
        words,
        new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero)).State;

    private static string Refused(Func<Project> change) => Assert.Throws<FacadeException>(() => change()).Code;

    [Fact]
    public void Marks_answer_the_project_they_make_with_the_time_handed_in()
    {
        SessionPiece piece = Example(Words.English);
        TickSpan bars = MarksSession.Bars(piece, 1, 2);

        Project noted = MarksSession.WithNote(piece, bars, " Left hand ", Now);
        Assert.Empty(piece.Project.Marks.Notes);
        MarginNote note = Assert.Single(noted.Marks.Notes);
        Assert.Equal(("n_0001", "Left hand", Now, bars), (note.Id, note.Text, note.CreatedAt, note.Span));

        SessionPiece marked = piece with { Project = MarksSession.WithHighlight(piece with { Project = noted }, bars, ["a"], null) };
        Assert.Equal(InkColour.Amber, Assert.Single(marked.Project.Marks.Highlights).Colour);
        Assert.Equal("badRequest", Refused(() => MarksSession.WithNote(piece, bars, "  ", Now)));
        Assert.Equal("badRequest", Refused(() => MarksSession.WithNoteChanged(piece, "n_0009", "x")));
        Assert.Equal("badRequest", Refused(() => MarksSession.WithFingering(piece, "n1", 6)));
        Assert.Equal("badRequest", Refused(() => MarksSession.WithoutHighlight(piece, "h_0009")));
        Assert.Equal("badRequest", Assert.Throws<FacadeException>(() => MarksSession.Bars(piece, 900, 900)).Code);
        Assert.Equal("badRequest", Assert.Throws<FacadeException>(() => MarksSession.Phrase(piece, 999)).Code);
    }

    [Fact]
    public void A_bookmark_starts_titled_by_its_bars_in_practise_and_from_a_phrase_when_one_spans_it()
    {
        SessionPiece piece = Example(Words.Russian);
        TickSpan phrase = MarksSession.Phrase(piece, 1);
        TickSpan bars = MarksSession.Bars(piece, 3, 4);

        SessionPiece with = piece with { Project = MarksSession.WithBookmark(piece, phrase, phraseNamed: false, null, null, Now) };
        with = with with { Project = MarksSession.WithBookmark(with, bars, phraseNamed: false, "Трудное место", BookmarkCategory.PerformanceId, Now) };
        Assert.Empty(piece.Project.Bookmarks);
        Bookmark first = with.Project.Bookmarks[0];
        Assert.Equal((MarksSession.BarsTitle(piece, phrase), BookmarkCategory.PractiseId, true, Now), (first.Title, first.CategoryId, first.FromPhrase, first.CreatedAt));
        Assert.StartsWith("такт", first.Title, StringComparison.Ordinal);
        Assert.False(with.Project.Bookmarks[1].FromPhrase);
        Assert.Equal("такты 3–4", MarksSession.BarsTitle(piece, bars));

        Project moved = MarksSession.WithBookmarkChanged(with, "b_0002", bars, phraseNamed: true, () => (null, null));
        Bookmark changed = moved.FindBookmark("b_0002")!;
        Assert.Equal(("Трудное место", BookmarkCategory.PerformanceId, true, Now), (changed.Title, changed.CategoryId, changed.FromPhrase, changed.CreatedAt));
        Assert.Equal("b_0002", MarksSession.WithBookmarkMoved(with, "b_0002", 0).Bookmarks[0].Id);
        Assert.Equal("badRequest", Refused(() => MarksSession.WithBookmarkChanged(with, "b_0009", null, phraseNamed: false, () => throw new InvalidOperationException("read only once the bookmark is found"))));
        Assert.Equal("badRequest", Refused(() => MarksSession.WithBookmark(piece, bars, phraseNamed: false, null, "c_0009", Now)));
        Assert.Equal("badRequest", Refused(() => MarksSession.WithBookmarkMoved(with, "b_0001", 5)));
    }

    [Fact]
    public void Structure_bookmarks_are_written_once_and_a_category_removed_sends_its_bookmarks_to_practise()
    {
        SessionPiece piece = Example(Words.English);
        SessionPiece structured = piece with { Project = MarksSession.WithStructureBookmarks(piece, Now) };
        Assert.NotEmpty(structured.Project.Bookmarks);
        Assert.All(structured.Project.Bookmarks, bookmark => Assert.Equal((BookmarkCategory.StructureId, Now), (bookmark.CategoryId, bookmark.CreatedAt)));
        Assert.Equal(structured.Project.Bookmarks, MarksSession.WithStructureBookmarks(structured, Now.AddHours(1)).Bookmarks);

        SessionPiece filed = piece with { Project = MarksSession.WithCategory(piece, "Memory", null) };
        Assert.Equal(InkColour.Graphite, filed.Project.FindCategory("c_0001")!.Ink);
        filed = filed with { Project = MarksSession.WithBookmark(filed, MarksSession.Bars(filed, 5, 5), phraseNamed: false, null, "c_0001", Now) };
        Project removed = MarksSession.WithoutCategory(filed, "c_0001");
        Assert.Null(removed.FindCategory("c_0001"));
        Assert.Equal(BookmarkCategory.PractiseId, Assert.Single(removed.Bookmarks).CategoryId);
        Assert.Equal("badRequest", Refused(() => MarksSession.WithoutCategory(piece, BookmarkCategory.StructureId)));
        Assert.Equal("badRequest", Refused(() => MarksSession.WithCategoryRenamed(piece, "c_0009", "x")));
    }
}