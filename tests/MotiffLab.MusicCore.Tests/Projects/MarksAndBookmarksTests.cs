using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>The rules of marks, bookmarks, resume points and ideas kept with the piece (0018, sections 5 and 9).</summary>
public class MarksAndBookmarksTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 1, 14, 0, 0, TimeSpan.FromHours(1));

    private static readonly byte[] s_source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "importer-grand-staff.musicxml"));

    private static ImportedPiece Piece() => PieceImporter.Import("minuet.musicxml", s_source).Piece!;

    private static TickSpan Bars(ImportedPiece piece, int first, int last) => MotifSelector.Bars(piece.Meter, first, last, piece.End);

    private static Project Fresh() => Project.Create("p_test", "minuet.musicxml", s_source, s_now);

    [Fact]
    public void A_bookmark_made_from_a_phrase_follows_it_through_a_merge_and_a_split()
    {
        ImportedPiece piece = Piece();
        TickSpan phrase1 = Bars(piece, 0, 1);
        TickSpan phrase2 = Bars(piece, 2, 3);
        TickSpan merged = Bars(piece, 0, 3);
        Project project = Fresh()
            .AddBookmark(new Bookmark("b_0001", "From the phrase", BookmarkCategory.StructureId, phrase1, FromPhrase: true, s_now))
            .AddBookmark(new Bookmark("b_0002", "My own bars", BookmarkCategory.PractiseId, phrase1, FromPhrase: false, s_now));

        Project afterMerge = project.FollowPhrases([merged]);
        Assert.Equal(merged, afterMerge.FindBookmark("b_0001")!.Span);
        Assert.Equal(phrase1, afterMerge.FindBookmark("b_0002")!.Span);

        Project afterSplit = afterMerge.FollowPhrases([Bars(piece, 0, 0), Bars(piece, 1, 3)]);
        Assert.Equal(Bars(piece, 0, 0), afterSplit.FindBookmark("b_0001")!.Span);

        // A phrase list that does not reach the bookmark leaves it alone.
        Assert.Equal(afterSplit.Bookmarks, afterSplit.FollowPhrases([phrase2]).Bookmarks);
    }

    [Fact]
    public void Bookmarks_keep_the_players_order_and_can_be_moved_and_sorted_by_place()
    {
        ImportedPiece piece = Piece();
        Project project = Fresh()
            .AddBookmark(new Bookmark("b_0001", "Coda", BookmarkCategory.StructureId, Bars(piece, 3, 3), false, s_now))
            .AddBookmark(new Bookmark("b_0002", "Opening", BookmarkCategory.StructureId, Bars(piece, 0, 0), false, s_now))
            .AddBookmark(new Bookmark("b_0003", "Middle", BookmarkCategory.StructureId, Bars(piece, 1, 2), false, s_now));

        Assert.Equal(["b_0001", "b_0002", "b_0003"], project.Bookmarks.Select(bookmark => bookmark.Id));
        Assert.Equal(["b_0002", "b_0003", "b_0001"], project.BookmarksInScoreOrder.Select(bookmark => bookmark.Id));
        Assert.Equal(["b_0003", "b_0001", "b_0002"], project.MoveBookmark("b_0003", 0).MoveBookmark("b_0002", 2).Bookmarks.Select(bookmark => bookmark.Id));
        Assert.Equal("b_0004", project.NextBookmarkId());
        Assert.Throws<KeyNotFoundException>(() => project.MoveBookmark("b_0009", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => project.MoveBookmark("b_0001", 3));
        Assert.Throws<ArgumentException>(() => project.AddBookmark(new Bookmark("b_0009", "Nowhere", "c_0007", Bars(piece, 0, 0), false, s_now)));
        Assert.Throws<ArgumentException>(() => new Bookmark("b_0009", "  ", BookmarkCategory.PractiseId, Bars(piece, 0, 0), false, s_now));
    }

    [Fact]
    public void Categories_are_the_four_built_in_ones_renamable_plus_the_players_own()
    {
        ImportedPiece piece = Piece();
        Project project = Fresh();
        Assert.Equal(["practise", "performance", "structure", "teacher"], project.Categories.Select(category => category.Id));
        Assert.Equal("Practise", project.Categories[0].NameIn(Words.English));
        Assert.Equal("Учитель", project.Categories[3].NameIn(Words.Russian));
        Assert.Equal([InkColour.Carmine, InkColour.Forest, InkColour.Graphite, InkColour.Ochre], project.Categories.Select(category => category.Ink));

        project = project.RenameCategory(BookmarkCategory.TeacherId, " Lena ")
            .AddCategory(new BookmarkCategory(project.NextCategoryId(), "Exam", InkColour.Carmine))
            .AddBookmark(new Bookmark("b_0001", "Scales", "c_0001", Bars(piece, 0, 0), false, s_now));
        Assert.Equal("Lena", project.FindCategory(BookmarkCategory.TeacherId)!.NameIn(Words.Russian));
        Assert.Equal("Teacher", project.RenameCategory(BookmarkCategory.TeacherId, null).FindCategory(BookmarkCategory.TeacherId)!.NameIn(Words.English));
        Assert.Equal("c_0002", project.NextCategoryId());

        Project removed = project.RemoveCategory("c_0001");
        Assert.Equal(4, removed.Categories.Count);
        Assert.Equal(BookmarkCategory.PractiseId, removed.FindBookmark("b_0001")!.CategoryId);
        Assert.Throws<ArgumentException>(() => project.RemoveCategory(BookmarkCategory.PractiseId));
        Assert.Throws<ArgumentException>(() => new BookmarkCategory("c_0009", "  ", InkColour.Forest));
        Assert.Throws<ArgumentException>(() => new BookmarkCategory("c_0009", "Amber?", InkColour.Amber));
    }

    [Fact]
    public void Marks_are_edited_one_kind_at_a_time()
    {
        ImportedPiece piece = Piece();
        string note = piece.Notes[0].Id;
        var stroke = new InkStroke("k1", InkTool.Pen, InkColour.Carmine, 0.16, Tick.Zero, 0.1, 0.5, [], 0.5);
        Marks marks = Marks.Empty
            .AddInk([stroke])
            .AddNote(new MarginNote("n_0001", Bars(piece, 0, 0), "First", s_now))
            .AddHighlight(new Highlight("h_0001", Bars(piece, 0, 0), [], InkColour.Amber))
            .SetFingering(new Fingering(note, 1))
            .SetFingering(new Fingering(note, 2));

        Assert.Equal(4, marks.Count);
        Assert.Equal(2, marks.Fingering.Single().Finger);
        Assert.Equal("n_0002", marks.NextNoteId());
        Assert.Equal("h_0002", marks.NextHighlightId());
        Assert.Equal("Second", marks.ChangeNote("n_0001", " Second ").Notes[0].Text);
        Assert.Empty(marks.RemoveInk(["k1", "k9"]).Ink);
        Assert.Empty(marks.RemoveFingering(note).Fingering);
        Assert.Empty(marks.RemoveNote("n_0001").Notes);
        Assert.Empty(marks.RemoveHighlight("h_0001").Highlights);
        Assert.Throws<ArgumentException>(() => marks.AddInk([stroke]));
        Assert.Throws<ArgumentException>(() => marks.AddNote(new MarginNote("n_0001", Bars(piece, 0, 0), "Again", s_now)));
        Assert.Throws<KeyNotFoundException>(() => marks.ChangeNote("n_0009", "Gone"));
        Assert.Throws<ArgumentException>(() => new MarginNote("n_0002", Bars(piece, 0, 0), "   ", s_now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Fingering(note, 0));
        Assert.Throws<ArgumentException>(() => new InkStroke("k2", InkTool.Pen, InkColour.Graphite, 0.16, Tick.Zero, 0, 0, [1.0, 2.0], 0.5));
    }

    [Fact]
    public void A_resume_point_starts_at_the_beginning_and_is_brought_up_to_date_as_the_player_works()
    {
        ImportedPiece piece = Piece();
        Project project = Fresh();
        ResumePoint start = ResumePoint.Start(project.NextResumePointId(), s_now, "  ");
        Assert.Equal(("r_0001", null, Tick.Zero, 100, false), (start.Id, start.Name, start.At, start.TempoPercent, start.Loop));

        project = project.AddResumePoint(start);
        project = project.AddResumePoint(ResumePoint.Start(project.NextResumePointId(), s_now.AddHours(1), "Coda"))
            .ChangeResumePoint("r_0001", point => point.Touch(new SavedPassage(Bars(piece, 2, 3), SelectionFilter.ForHand(Hand.Right)), s_now.AddHours(2)));

        Assert.Equal(["r_0001", "r_0002"], project.RecentResumePoints.Select(point => point.Id));
        ResumePoint touched = project.FindResumePoint("r_0001")!;
        Assert.Equal(Bars(piece, 2, 3).Onset, touched.At);
        Assert.Equal(Hand.Right, touched.Passage!.Filter.Hand);
        Assert.Equal("Coda", project.FindResumePoint("r_0002")!.Name);
        Assert.Null(project.ChangeResumePoint("r_0002", point => point.Rename(" ")).FindResumePoint("r_0002")!.Name);
        Assert.Equal([Overlay.PianoRoll, Overlay.Insights], touched.Touch(touched.At, touched.Passage, false, [Overlay.Insights, Overlay.PianoRoll, Overlay.Insights], 80, null, false, s_now).Overlays);
        Assert.Single(project.RemoveResumePoint("r_0001").ResumePoints);
        Assert.Throws<KeyNotFoundException>(() => project.RemoveResumePoint("r_0009"));
        Assert.Throws<ArgumentException>(() => project.AddResumePoint(start));
        Assert.Throws<ArgumentOutOfRangeException>(() => start.Touch(Tick.Zero, null, false, [], 10, null, false, s_now));
    }

    [Fact]
    public void An_idea_belongs_to_the_phrase_its_passage_starts_in()
    {
        ImportedPiece piece = Piece();
        var idea = Observation.Create("o_0001", new SavedPassage(Bars(piece, 1, 2), SelectionFilter.All), null, "Idea", "A note", s_now);

        Assert.Equal(Bars(piece, 0, 1), idea.Home([Bars(piece, 0, 1), Bars(piece, 2, 3)]));
        Assert.Equal(Bars(piece, 0, 3), idea.Home([Bars(piece, 0, 3)]));
        Assert.Equal(Bars(piece, 2, 3), idea.Home([Bars(piece, 2, 3)]));
        Assert.Null(idea.Home([Bars(piece, 3, 3)]));
        Assert.True(idea.IsAbout(Bars(piece, 2, 3)));
    }

    [Fact]
    public void Check_reasons_are_said_in_both_languages()
    {
        foreach (CheckReason reason in Enum.GetValues<CheckReason>())
        {
            var flag = new CheckFlag(1, reason >= CheckReason.PartSeam ? CheckSource.Transcription : CheckSource.Scan, reason, false);
            Assert.NotEqual(flag.Describe(Words.English), flag.Describe(Words.Russian));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new CheckFlag(-1, CheckSource.Scan, CheckReason.BarTooLong, false));
    }
}