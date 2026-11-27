using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The player's work kept in ticks with the piece, following the music when other Notes become the working ones (v1.4 plan,
/// answer 5 of 2026-11-25: «Фразы и закладки при смене рабочих Нот переносятся по сопоставлению тактов сами»): the
/// bookmarks (and a title still by their bars), the phrase boundaries set or taken away at every level, the resume points, the
/// ideas kept with a note alone, and the versions compared with the notes (their anchors, the bars dismissed and their Check
/// flags; lead, 2026-11-26: nothing stays in stale ticks), each placed by bars in the new working notes as the two are
/// matched (<see cref="ReadingMatch"/>, the match of the corrections carried over). A place in a bar with no match is kept
/// where the music before it went and told, never dropped: the earlier notes keep none of this work, so dropping it would
/// lose it. What waits with its own notes (the marks, motifs, variations and ideas with a variation) is not here; a page put
/// into the same notes moves that too (<see cref="Inserted"/>). Pure: the same projects give the same work.
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
        if (!Placed(made) || !was.HasNotes || !made.HasNotes || Seen(was) is not { } before || Seen(made) is not { } after)
        {
            return null;
        }

        var match = new ReadingMatch(before, after);
        return Followed(made, new Following((at, end) => match.Place(at, end), span => match.Place(span), before.Meter, after.Meter, null));
    }

    /// <summary>
    /// The work of <paramref name="project"/> in its notes' ticks (<paramref name="was"/>'s meter) placed in the same notes with
    /// a page of <paramref name="length"/> ticks put in at <paramref name="at"/> (<paramref name="meter"/> theirs; v1.5 i-7,
    /// the 1.4 plan's answer 9: «закладки и точки продолжения сдвигаются на длину вставленной страницы, а не
    /// сбрасываются»; the lead, 2026-11-27: phrases, ideas, motifs and marks by the same rule): a place at or after it moves
    /// on by the page, a span's end only when it is after it, so a span across it takes the page in; the rest stays. What
    /// names a note by its id (motifs, ideas' variations, highlights, fingering) names it by <paramref name="ids"/>, its id
    /// among the notes with the page (<see cref="NoteEditLog.IdsMovedOn"/>). Every place is matched, so nothing is kept near.
    /// Null, and nothing moved, when a motif or an idea's variation of these notes stands across the page's place (it would
    /// take the page's notes in) or names a note that is not the same once the page is in: nothing is dropped.
    /// </summary>
    public static FollowedWork? Inserted(Project project, MeterMap was, MeterMap meter, Tick at, long length, IReadOnlyDictionary<string, string?> ids)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ids);
        // E-scan-7 (docs/ui-edge-cases.md): a passage of notes is its notes, so one across the place keeps the page out.
        bool Across(TickSpan span) => span.Onset < at && span.End > at;
        if (project.Motifs.Any(motif => motif.Notes is null && Across(motif.Span))
            || project.Observations.Any(observation => observation.Variation is { } kept && observation.Notes is null && Across(kept.Passage.Span)))
        {
            return null;
        }

        Tick Moved(Tick tick, bool end) => (end ? tick > at : tick >= at) ? new Tick(tick.Value + length) : tick;
        // A span that starts at the place moves on whole, its end too, even when it is empty.
        TickSpan Span(TickSpan span) => span.Onset >= at ? new TickSpan(Moved(span.Onset, false), span.Duration) : TickSpan.FromBounds(span.Onset, Moved(span.End, true));
        // A note not among the notes read (one the player added) keeps its id, as its correction does.
        var follow = new Following((tick, end) => (Moved(tick, end), true), span => (Span(span), true), was, meter, id => ids.TryGetValue(id, out string? to) ? to : id);
        FollowedWork followed = Followed(project, follow);
        return follow.Lost ? null : followed;
    }

    /// <summary><paramref name="made"/> with its work placed one by one by <paramref name="follow"/>, and what that moved.</summary>
    private static FollowedWork Followed(Project made, Following follow)
    {
        Project followed = made with
        {
            Phrases = follow.Edits(made.Phrases, Told.Phrase),
            ShortPhrases = follow.Edits(made.ShortPhrases, Told.None),
            Sentences = follow.Edits(made.Sentences, Told.None),
            Bookmarks = [.. made.Bookmarks.Select(follow.Placed)],
            ResumePoints = [.. made.ResumePoints.Select(point => point with
            {
                At = follow.At(point.At, Told.None),
                Passage = point.Passage is { } passage ? passage with { Span = follow.Span(passage.Span, Told.None) } : null,
            })],
            Observations = [.. made.Observations.Select(follow.Idea)],
            Motifs = [.. made.Motifs.Select(follow.Motif)],
            Marks = follow.Marked(made.Marks),
            Performances = [.. made.Performances.Select(follow.Compared)],
            Check = made.Check is { } check ? follow.Checks(check) : null,
        };
        return new FollowedWork(followed, follow.Moved(Told.Bookmark), follow.Moved(Told.Phrase), follow.Moved(Told.Idea), follow.Near, [.. follow.NearBars]);
    }

    /// <summary>Whether the project keeps work in its working notes' ticks or bars that would follow them.</summary>
    private static bool Placed(Project project) =>
        !project.Phrases.IsEmpty || !project.ShortPhrases.IsEmpty || !project.Sentences.IsEmpty || project.Bookmarks.Count > 0
        || project.ResumePoints.Any(static point => point.At != Tick.Zero || point.Passage is not null)
        || project.Observations.Any(static observation => observation.Variation is null) || project.Performances.Count > 0;

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

    /// <summary>
    /// The places of the work placed one by one, counted as they are told: <paramref name="place"/> places a tick (a span's
    /// end with <c>end</c>) and <paramref name="span"/> a span, each saying whether it was matched.
    /// </summary>
    /// <remarks>
    /// <paramref name="ids"/> gives a note's id where the work is placed, null for a note not there the same; with no
    /// <paramref name="ids"/> (other Notes made working) what is kept with its own notes waits with them as it is: the marks,
    /// the motifs and the ideas with a variation.
    /// </remarks>
    private sealed class Following(
        Func<Tick, bool, (Tick At, bool Matched)> place, Func<TickSpan, (TickSpan Span, bool Matched)> span, MeterMap was, MeterMap meter, Func<string, string?>? ids)
    {
        private readonly int[] _moved = new int[4];

        public int Near { get; private set; }

        public SortedSet<int> NearBars { get; } = [];

        /// <summary>Whether a note named by the work is not there the same, so the work cannot follow it.</summary>
        public bool Lost { get; private set; }

        public int Moved(Told told) => _moved[(int)told];

        /// <summary>The idea: a note alone at its place; one with a variation of the working notes, its passage and notes, when notes are followed.</summary>
        public Observation Idea(Observation observation)
        {
            if (observation.Variation is not { } kept)
            {
                return observation with { Phrase = observation.Phrase with { Span = Span(observation.Phrase.Span, Told.Idea) } };
            }

            return ids is null || observation.Notes is not null ? observation : observation with
            {
                Phrase = observation.Phrase with { Span = Span(observation.Phrase.Span, Told.Idea) },
                Variation = kept with { Passage = kept.Passage with { Span = Span(kept.Passage.Span, Told.None) }, NoteIds = Notes(kept.NoteIds) },
            };
        }

        /// <summary>A motif of the working notes at its place and on its notes, when notes are followed.</summary>
        public SavedMotif Motif(SavedMotif motif) =>
            ids is null || motif.Notes is not null ? motif : motif with { Span = Span(motif.Span, Told.None), NoteIds = Notes(motif.NoteIds) };

        /// <summary>The marks, each at its place and on its notes, when notes are followed: a stroke by its bar, a margin note and a highlight by their bars.</summary>
        public Marks Marked(Marks marks) => ids is null ? marks : marks with
        {
            Ink = [.. marks.Ink.Select(stroke => stroke with { Bar = At(stroke.Bar, Told.None) })],
            Notes = [.. marks.Notes.Select(note => note with { Span = Span(note.Span, Told.None) })],
            Highlights = [.. marks.Highlights.Select(highlight => new Highlight(highlight.Id, Span(highlight.Span, Told.None), Notes(highlight.NoteIds), highlight.Colour))],
            Fingering = [.. marks.Fingering.Select(fingering => new Fingering(Note(fingering.NoteId), fingering.Finger))],
        };

        /// <summary>The edits with each boundary placed, set and taken away as before (a tick both set and taken away is set).</summary>
        public PhraseEdits Edits(PhraseEdits edits, Told told) =>
            PhraseEdits.None.Removing([.. edits.Removed.Select(tick => At(tick, told))]).Adding([.. edits.Added.Select(tick => At(tick, told))]);

        /// <summary>
        /// The bookmark at its place, and with a title that is still, or ends with ", " and, the bars title of its place, in
        /// either language (<see cref="Bookmark.BarsTitle"/>: «bars 1–8», the form's «Section A, bars 1–8», a confirmed
        /// section's «Part 2, bars 41–80»), that ending titled by its bars now in that language; a title the player gave stays.
        /// </summary>
        public Bookmark Placed(Bookmark bookmark)
        {
            TickSpan span = Span(bookmark.Span, Told.Bookmark);
            foreach (Words words in (Words[])[Words.English, Words.Russian])
            {
                string bars = Bookmark.BarsTitle(was, bookmark.Span, words);
                if (bookmark.Title == bars || bookmark.Title.EndsWith(", " + bars, StringComparison.Ordinal))
                {
                    return bookmark with { Span = span, Title = string.Concat(bookmark.Title.AsSpan(0, bookmark.Title.Length - bars.Length), Bookmark.BarsTitle(meter, span, words)) };
                }
            }

            return bookmark with { Span = span };
        }

        /// <summary>
        /// A version compared with the notes, lined up with the new ones: its anchors placed, those that would no longer
        /// increase left out (an alignment needs them strictly increasing), and the bars the player said are not a
        /// difference at their bars now. Its merge record is written back as it was (it names no bar the app reads).
        /// </summary>
        public SavedPerformance Compared(SavedPerformance performance)
        {
            List<Anchor> anchors = [];
            foreach (Anchor anchor in performance.Anchors)
            {
                Anchor placed = anchor with { Score = At(anchor.Score, Told.None) };
                if (anchors.Count == 0 || (placed.Score > anchors[^1].Score && placed.Microseconds > anchors[^1].Microseconds))
                {
                    anchors.Add(placed);
                }
            }

            return performance with { Anchors = anchors, Dismissed = [.. performance.Dismissed.Select(Bar).Distinct().Order()] };
        }

        /// <summary>The Check flags, those of the versions compared at their bars now (one of a bar and reason kept), in bar order.</summary>
        public IReadOnlyList<CheckFlag> Checks(IReadOnlyList<CheckFlag> flags) =>
            [.. flags.Select(flag => flag.Source == CheckSource.Performance ? new CheckFlag(Bar(flag.Bar), flag.Source, flag.Reason, flag.Resolved) : flag)
                .DistinctBy(static flag => (flag.Bar, flag.Source, flag.Reason)).OrderBy(static flag => flag.Bar).ThenBy(static flag => flag.Reason)];

        /// <summary>The bar of the new working notes where an earlier bar's start is placed.</summary>
        public int Bar(int number) => number < was.FirstBarNumber ? number : meter.BarAt(At(was.GetBar(number).Span.Onset, Told.None)).Number;

        public Tick At(Tick at, Told told)
        {
            (Tick placed, bool matched) = place(at, false);
            Count(told, placed, placed != at, matched);
            return placed;
        }

        public TickSpan Span(TickSpan given, Told told)
        {
            (TickSpan placed, bool matched) = span(given);
            Count(told, placed.Onset, placed != given, matched);
            return placed;
        }

        /// <summary>The notes' ids where the work is placed (<see cref="Note"/>).</summary>
        private string[] Notes(IEnumerable<string> noteIds) => [.. noteIds.Select(Note)];

        /// <summary>A note's id where the work is placed; its own, and the work <see cref="Lost"/>, when it is not there the same.</summary>
        private string Note(string id)
        {
            if (ids!(id) is { } placed)
            {
                return placed;
            }

            Lost = true;
            return id;
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