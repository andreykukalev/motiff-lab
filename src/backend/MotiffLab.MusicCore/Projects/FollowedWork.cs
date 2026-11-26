using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The player's work kept in ticks with the piece, following the music when other Notes become the working ones (v1.4 plan,
/// answer 5 of 2026-11-25: «Фразы и закладки при смене рабочих Нот переносятся по сопоставлению тактов сами»): the
/// bookmarks, the phrase boundaries set or taken away at every level, the resume points and the ideas kept with a note alone,
/// each placed by bars in the new working notes as the two are matched (<see cref="ReadingMatch"/>, the match of the
/// corrections carried over). A place in a bar with no match is kept where the music before it went and told, never
/// dropped: the earlier notes keep none of this work, so dropping it would lose it. What waits with its own notes (the marks,
/// motifs, variations and ideas with a variation) and the versions compared are not here. Pure: the same projects give the
/// same work.
/// </summary>
/// <param name="Project">The project with the work followed.</param>
/// <param name="Bookmarks">The bookmarks whose place changed with their bars.</param>
/// <param name="Phrases">The player's phrase boundaries of the level the score shows (<see cref="Project.Phrases"/>) whose place changed with their bars.</param>
/// <param name="Ideas">The ideas kept with a note alone whose place changed with their bars.</param>
/// <param name="Near">The bookmarks, phrase boundaries and ideas in a bar with no match, kept where the music before them went.</param>
/// <param name="NearBars">The bars of the new working notes those are in now, in order, each once.</param>
internal sealed record FollowedWork(Project Project, int Bookmarks, int Phrases, int Ideas, int Near, IReadOnlyList<int> NearBars)
{
    /// <summary>
    /// What followed, in a line of the toast: «перенесено по тактам: 2 закладки, 3 границы фраз · в тактах без пары оставлено
    /// рядом: 1 (такт 9)», each part only when it counts something; null when nothing does (the bars stayed where they were).
    /// </summary>
    public string? Text(Words words)
    {
        List<string> moved = [];
        if (Bookmarks > 0)
        {
            moved.Add(words.Count(Bookmarks, "bookmark", "bookmarks", "закладка", "закладки", "закладок"));
        }

        if (Phrases > 0)
        {
            moved.Add(words.Count(Phrases, "phrase boundary", "phrase boundaries", "граница фразы", "границы фраз", "границ фраз"));
        }

        if (Ideas > 0)
        {
            moved.Add(words.Count(Ideas, "idea", "ideas", "идея", "идеи", "идей"));
        }

        List<string> parts = [];
        if (moved.Count > 0)
        {
            string list = string.Join(", ", moved);
            parts.Add(words.T($"moved with their bars: {list}", $"перенесено по тактам: {list}"));
        }

        if (Near > 0)
        {
            string where = string.Join(", ", NearBars);
            parts.Add(words.T(
                $"in bars with no match, kept by the music before them: {Near} ({(NearBars.Count == 1 ? "bar" : "bars")} {where})",
                $"в тактах без пары оставлено рядом: {Near} ({(NearBars.Count == 1 ? "такт" : "такты")} {where})"));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// <paramref name="made"/>, the project with other Notes made working out of <paramref name="was"/>, with the work kept in
    /// the ticks of <paramref name="was"/>'s working notes placed in theirs, both read as the player sees them (the corrections
    /// laid on leniently, or off when the player turned them off); null when there is no such work, or either notes do not
    /// open, and the work stays in its ticks.
    /// </summary>
    public static FollowedWork? Of(Project was, Project made)
    {
        ArgumentNullException.ThrowIfNull(was);
        ArgumentNullException.ThrowIfNull(made);
        bool placed = !made.Phrases.IsEmpty || !made.ShortPhrases.IsEmpty || !made.Sentences.IsEmpty || made.Bookmarks.Count > 0
            || made.ResumePoints.Any(static point => point.At != Tick.Zero || point.Passage is not null)
            || made.Observations.Any(static observation => observation.Variation is null);
        if (!placed || !was.HasNotes || !made.HasNotes || Seen(was) is not { } before || Seen(made) is not { } after)
        {
            return null;
        }

        var follow = new Following(new ReadingMatch(before, after), after.Meter);
        Project followed = made with
        {
            Phrases = follow.Edits(made.Phrases, Told.Phrase),
            ShortPhrases = follow.Edits(made.ShortPhrases, Told.None),
            Sentences = follow.Edits(made.Sentences, Told.None),
            Bookmarks = [.. made.Bookmarks.Select(bookmark => bookmark with { Span = follow.Span(bookmark.Span, Told.Bookmark) })],
            ResumePoints = [.. made.ResumePoints.Select(point => point with
            {
                At = follow.At(point.At, Told.None),
                Passage = point.Passage is { } passage ? passage with { Span = follow.Span(passage.Span, Told.None) } : null,
            })],
            Observations = [.. made.Observations.Select(observation => observation.Variation is not null ? observation
                : observation with { Phrase = observation.Phrase with { Span = follow.Span(observation.Phrase.Span, Told.Idea) } })],
        };
        return new FollowedWork(followed, follow.Moved(Told.Bookmark), follow.Moved(Told.Phrase), follow.Moved(Told.Idea), follow.Near, [.. follow.NearBars]);
    }

    /// <summary>The working notes of <paramref name="project"/> as the player sees them; null when they do not open.</summary>
    private static ImportedPiece? Seen(Project project)
    {
        Project bare = project with { Motifs = [], Variations = [], Observations = [] };
        try
        {
            OpenedProject opened = OpenedProject.Open(bare, project.EditsOff ? EditsLayer.Off : EditsLayer.Lenient);
            return opened.Piece with { Notes = opened.Notes };
        }
        catch (ProjectFileException)
        {
            return null;
        }
    }

    /// <summary>Which count of the toast a place is told in; None for what it does not name (the levels the score does not show, the resume points).</summary>
    private enum Told
    {
        None,
        Bookmark,
        Phrase,
        Idea,
    }

    /// <summary>The places of the work placed one by one, counted as they are told.</summary>
    private sealed class Following(ReadingMatch match, MeterMap meter)
    {
        private readonly int[] _moved = new int[4];

        public int Near { get; private set; }

        public SortedSet<int> NearBars { get; } = [];

        public int Moved(Told told) => _moved[(int)told];

        /// <summary>The edits with each boundary placed, set and taken away as before (a tick both set and taken away is set).</summary>
        public PhraseEdits Edits(PhraseEdits edits, Told told) =>
            PhraseEdits.None.Removing([.. edits.Removed.Select(tick => At(tick, told))]).Adding([.. edits.Added.Select(tick => At(tick, told))]);

        public Tick At(Tick at, Told told)
        {
            (Tick placed, bool matched) = match.Place(at, end: false);
            Count(told, placed, placed != at, matched);
            return placed;
        }

        public TickSpan Span(TickSpan span, Told told)
        {
            (TickSpan placed, bool matched) = match.Place(span);
            Count(told, placed.Onset, placed != span, matched);
            return placed;
        }

        private void Count(Told told, Tick at, bool changed, bool matched)
        {
            if (told == Told.None)
            {
                return;
            }

            if (!matched)
            {
                Near++;
                NearBars.Add(meter.BarAt(at).Number);
            }
            else if (changed)
            {
                _moved[(int)told]++;
            }
        }
    }
}