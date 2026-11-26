using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Tests.Performance.ReadingCarryTests;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The player's work in ticks following the music when other Notes become the working ones (v1.5 c-11b; the v1.4 plan's
/// answer 5): bookmarks, phrase boundaries, resume points and ideas placed by bars, a place in a bar with no match kept where
/// the music before it went and told, and the line of the toast in both languages.
/// </summary>
public class FollowedWorkTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private const int Bar = 4 * 480;

    private static TickSpan Bars(int first, int last) => TickSpan.FromBounds(new Tick((first - 1) * Bar), new Tick(last * Bar));

    /// <summary>A piece read as <paramref name="working"/>, with other Notes "n_2" read as <paramref name="other"/>.</summary>
    private static Project Piece(string working, string other) =>
        Project.Create("p_1", "a.musicxml", ReadingXml(working), s_now)
            .AddNotes(new OtherNotes("n_2", "b.musicxml", ReadingXml(other), new NotesProvenance(NotesProvenance.Import)));

    private static Project Bookmarked(Project project, params TickSpan[] spans) => spans.Aggregate(project, static (marked, span) =>
        marked.AddBookmark(new Bookmark(marked.NextBookmarkId(), "Mine", BookmarkCategory.PractiseId, span, FromPhrase: false, s_now)));

    [Fact]
    public void Bookmarks_phrase_boundaries_resume_points_and_ideas_move_with_their_bars_and_back()
    {
        Project project = Bookmarked(Piece("E4 F4 G4 A4 | B4 C5 D5 E5", "C4 C4 C4 C4 | E4 F4 G4 A4 | B4 C5 D5 E5"), Bars(2, 2)) with
        {
            Phrases = PhraseEdits.None.Adding([new Tick(Bar + 480)]).Removing([new Tick(Bar)]),
            ShortPhrases = PhraseEdits.None.Adding([new Tick(960)]),
            ResumePoints = [ResumePoint.Start("r_0001", s_now).Touch(new SavedPassage(Bars(2, 2), SelectionFilter.All), s_now)],
            Observations = [Observation.Create("o_0001", new SavedPassage(Bars(1, 1), SelectionFilter.All), null, "Idea", "Lighter here", s_now)],
        };

        Project swapped = project.MakeWorking("n_2", out FollowedWork? followed);

        Assert.Equal(Bars(3, 3), Assert.Single(swapped.Bookmarks).Span);
        Assert.Equal(PhraseEdits.None.Adding([new Tick(2 * Bar + 480)]).Removing([new Tick(2 * Bar)]), swapped.Phrases);
        Assert.Equal([new Tick(Bar + 960)], swapped.ShortPhrases.Added);
        ResumePoint point = Assert.Single(swapped.ResumePoints);
        Assert.Equal((new Tick(2 * Bar), Bars(3, 3)), (point.At, point.Passage!.Span));
        Assert.Equal(Bars(2, 2), Assert.Single(swapped.Observations).Phrase.Span);
        Assert.Equal((1, 2, 1, 0), (followed!.Bookmarks, followed.Phrases, followed.Ideas, followed.Near));
        Assert.Equal("moved with their bars: 1 bookmark, 2 phrase boundaries, 1 idea", followed.Text(Words.English));
        Assert.Equal("перенесено по тактам: 1 закладка, 2 границы фраз, 1 идея", followed.Text(Words.Russian));

        Project back = swapped.MakeWorking("n_1", out _);
        Assert.Equal(project.Bookmarks, back.Bookmarks);
        Assert.Equal((project.Phrases, project.ShortPhrases), (back.Phrases, back.ShortPhrases));
        Assert.Equal(project.ResumePoints, back.ResumePoints);
    }

    [Fact]
    public void A_bookmark_in_a_bar_the_new_notes_have_not_is_kept_by_the_music_before_it_and_told()
    {
        Project project = Bookmarked(Piece("E4 F4 G4 A4 | B4 C5 D5 E5 | F5 G5 A5 B5", "E4 F4 G4 A4 | F5 G5 A5 B5"), Bars(2, 2), Bars(3, 3));

        Project swapped = project.MakeWorking("n_2", out FollowedWork? followed);

        Assert.Equal([Bars(2, 2), Bars(2, 2)], swapped.Bookmarks.Select(static bookmark => bookmark.Span));
        Assert.Equal((1, 1), (followed!.Bookmarks, followed.Near));
        Assert.Equal([2], followed.NearBars);
        Assert.Equal("moved with their bars: 1 bookmark · in bars with no match, kept by the music before them: 1 (bar 2)", followed.Text(Words.English));
        Assert.Equal("перенесено по тактам: 1 закладка · в тактах без пары оставлено рядом: 1 (такт 2)", followed.Text(Words.Russian));
    }

    [Fact]
    public void Bars_that_stay_where_they_were_tell_nothing_and_no_work_follows_nothing()
    {
        Project same = Bookmarked(Piece("E4 F4 G4 A4 | B4 C5 D5 E5", "E4 F4 G4 A4 | B4 C5 D5 D5"), Bars(2, 2));
        Project none = Piece("E4 F4 G4 A4", "C4 E4 F4 G4 A4") with { ResumePoints = [ResumePoint.Start("r_0001", s_now)] };

        Project kept = same.MakeWorking("n_2", out FollowedWork? followed);
        Project untouched = none.MakeWorking("n_2", out FollowedWork? nothing);

        Assert.Equal(Bars(2, 2), Assert.Single(kept.Bookmarks).Span);
        Assert.Null(followed!.Text(Words.English));
        Assert.Null(nothing);
        Assert.Equal(none.ResumePoints, untouched.ResumePoints);
    }

    [Fact]
    public void Work_on_notes_that_do_not_open_stays_in_its_ticks()
    {
        Project project = Bookmarked(Project.Create("p_1", "a.musicxml", ReadingXml("E4 F4 G4 A4"), s_now)
            .AddNotes(new OtherNotes("n_2", "b.musicxml", "<not music/>"u8.ToArray(), new NotesProvenance(NotesProvenance.Import))), Bars(1, 1));

        Project swapped = project.MakeWorking("n_2", out FollowedWork? followed);

        Assert.Null(followed);
        Assert.Equal(Bars(1, 1), Assert.Single(swapped.Bookmarks).Span);
    }
}