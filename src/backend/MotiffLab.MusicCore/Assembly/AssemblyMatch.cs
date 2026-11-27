using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assembly;

/// <summary>How a stretch of a source compares with the player's notes, in words (decision record 0024, section 6).</summary>
internal enum MatchKind
{
    /// <summary>«совпадает»: the same notes, maybe with a few corrections.</summary>
    Same,

    /// <summary>«частично»: much in common, much not.</summary>
    Partly,

    /// <summary>«отличается»: found in place, but other notes.</summary>
    Differs,

    /// <summary>«не найдено»: nothing in the player's notes lines up with it.</summary>
    NotFound,
}

/// <summary>One bar of a source against the player's notes.</summary>
/// <param name="Index">The bar's index in the source's bars.</param>
/// <param name="Span">The bar in the source.</param>
/// <param name="Target">Where it lands in the player's notes; null when not found.</param>
/// <param name="Identical">The same notes at the same places and lengths: nothing to take.</param>
/// <param name="Clean">The bar lands on a whole bar of the notes, of the same length, so it can be taken bar for bar.</param>
internal sealed record MatchBar(int Index, TickSpan Span, MatchKind Kind, int Matched, int Total, TickSpan? Target, bool Identical, bool Clean);

/// <summary>A run of bars of the same kind: «такты 1–29 совпадают».</summary>
/// <param name="First">The first source bar's number.</param>
/// <param name="Last">The last source bar's number.</param>
internal sealed record MatchSegment(MatchKind Kind, int First, int Last, TickSpan Span);

/// <summary>
/// A source compared with the player's notes as they are now, bar by bar of the source (decision record 0024,
/// section 6): the alignment of 0022 between the two, then for each bar how many of its notes are found at their place.
/// Stamped with the journal it was made for, so nothing is taken on a stale comparison.
/// </summary>
internal sealed record AssemblyMatch(string SourceId, string Stamp, IReadOnlyList<MatchBar> Bars, IReadOnlyList<MatchSegment> Segments)
{
    /// <summary>At least this share of a bar's notes found in place, or all but one of at least half: «совпадает» (with a correction).</summary>
    public const double SameShare = 0.85;

    /// <summary>At least this share: «частично».</summary>
    public const double PartlyShare = 0.5;

    /// <summary>How far off its place a note may be found, in ticks: a 16th.</summary>
    public const long Tolerance = CanonicalTime.TicksPerQuarter / 4;

    /// <summary>The alignment the comparison was made through (<see cref="Aligned"/>).</summary>
    internal PerformanceAlignment Alignment { get; private init; } = null!;

    /// <summary>
    /// <paramref name="source"/> lined up with <paramref name="notes"/> as a version is with the score (0022, section 2):
    /// its ticks taken as microseconds, so <see cref="PerformanceAlignment.TickAt"/> of a source tick × 1000 is the tick of
    /// <paramref name="notes"/> it falls on; through <paramref name="confirmed"/>, ticks of <paramref name="notes"/> and of
    /// the source the player confirmed sound together, when there are any.
    /// </summary>
    internal static PerformanceAlignment Aligned(SourceMusic source, IReadOnlyList<Note> notes, IReadOnlyList<(Tick Notes, Tick Source)>? confirmed = null) =>
        PerformanceAlignment.Align(notes, Played(source), [.. (confirmed ?? []).Select(static anchor => new Anchor(anchor.Notes, anchor.Source.Value * 1000))]);

    /// <summary>The source's notes as a performance's, its ticks taken as microseconds.</summary>
    internal static IReadOnlyList<PlayedNote> Played(SourceMusic source) =>
        [.. source.Notes.Select(note => new PlayedNote(note.Id, note.Pitch, note.Span.Onset.Value * 1000, note.Velocity, note.Span.Duration * 1000))];

    /// <summary>
    /// Compares <paramref name="source"/> with <paramref name="notes"/>; <paramref name="stamp"/> names the journal the notes
    /// were built from, <paramref name="confirmed"/> the ticks the player confirmed sound together (<see cref="Aligned"/>).
    /// </summary>
    public static AssemblyMatch Of(string sourceId, SourceMusic source, SourceMusic notes, string stamp, IReadOnlyList<(Tick Notes, Tick Source)>? confirmed = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(notes);
        PerformanceAlignment alignment = Aligned(source, notes.Notes, confirmed);
        Anchor[] anchors = [.. alignment.Anchors];
        HashSet<string> used = new(StringComparer.Ordinal);
        List<MatchBar> bars = [];
        for (int i = 0; i < source.Bars.Count; i++)
        {
            TickSpan bar = source.Bars[i].Span;
            bool found = anchors.Any(anchor => anchor.Microseconds >= bar.Onset.Value * 1000 && anchor.Microseconds < bar.End.Value * 1000);
            Tick start = alignment.TickAt(bar.Onset.Value * 1000);
            Tick end = alignment.TickAt(bar.End.Value * 1000);
            TickSpan? target = found && end > start ? TickSpan.FromBounds(start, end) : null;
            IReadOnlyList<Note> mine = source.NotesIn(bar.Onset, bar.End);
            IReadOnlyList<Note> theirs = target is { } span ? notes.NotesIn(span.Onset, span.End) : [];
            int matched = 0;
            bool identical = found && mine.Count == theirs.Count;
            Note?[] matches = Exact(mine, theirs, bar, target, used);
            for (int n = 0; n < mine.Count; n++)
            {
                Note note = mine[n];
                Tick at = alignment.TickAt(note.Span.Onset.Value * 1000);
                Note? match = matches[n] ?? theirs.Where(other => other.Pitch == note.Pitch && !used.Contains(other.Id) && Math.Abs(other.Span.Onset.Value - at.Value) <= Tolerance)
                    .OrderBy(other => Math.Abs(other.Span.Onset.Value - at.Value)).FirstOrDefault();
                if (match is null)
                {
                    identical = false;
                    continue;
                }

                used.Add(match.Id);
                matched++;
                identical &= match.Span.Onset.Value - target!.Value.Onset.Value == note.Span.Onset.Value - bar.Onset.Value
                    && Math.Abs(match.Span.Duration - note.Span.Duration) < AssemblyBars.Grid;
            }

            int total = Math.Max(mine.Count, theirs.Count);
            MatchKind kind = !found && total > 0 ? MatchKind.NotFound
                : total == 0 ? MatchKind.Same
                : IsSame(matched, total) ? MatchKind.Same
                : matched >= total * PartlyShare ? MatchKind.Partly
                : MatchKind.Differs;
            bool clean = target is { } landed && CleanBar(notes, landed, bar.Duration) is not null;
            bars.Add(new MatchBar(i, bar, kind, matched, total, target, identical || total == 0, clean));
        }

        return new AssemblyMatch(sourceId, stamp, bars, RunsOf(source, bars)) { Alignment = alignment };
    }

    /// <summary>
    /// The notes of a source bar found exactly where they stand in it, as the bar lands in the notes (<paramref name="target"/>:
    /// its own position scaled to the target's length, in integers), each taken into <paramref name="used"/>; null for the
    /// rest, which are looked for near their aligned tick. First, so a run of one pitch is never paired one note late, the
    /// alignment's tick inside a bar being near but not exact even for the same music (v1.4 F6: the same passage taken
    /// twice showed 92 %).
    /// </summary>
    private static Note?[] Exact(IReadOnlyList<Note> mine, IReadOnlyList<Note> theirs, TickSpan bar, TickSpan? target, HashSet<string> used)
    {
        var matches = new Note?[mine.Count];
        if (target is not { } landed || bar.Duration == 0)
        {
            return matches;
        }

        for (int n = 0; n < mine.Count; n++)
        {
            Note note = mine[n];
            long expected = landed.Onset.Value + ((note.Span.Onset.Value - bar.Onset.Value) * landed.Duration / bar.Duration);
            matches[n] = theirs.FirstOrDefault(other => other.Pitch == note.Pitch && other.Span.Onset.Value == expected && !used.Contains(other.Id));
            if (matches[n] is { } match)
            {
                used.Add(match.Id);
            }
        }

        return matches;
    }

    /// <summary>
    /// «Совпадает»: <paramref name="matched"/> of <paramref name="total"/> notes found in place is at least
    /// <see cref="SameShare"/> of them, or all but one of at least <see cref="PartlyShare"/> (a correction).
    /// </summary>
    public static bool IsSame(int matched, int total) =>
        matched >= total * SameShare || (total - matched <= 1 && matched >= total * PartlyShare);

    /// <summary>Where a stretch of the source lands in the player's notes and how alike it is there: «Похоже на такты 15–16 · частично».</summary>
    public (TickSpan Span, MatchKind Kind)? Similar(TickSpan inSource)
    {
        MatchBar[] bars = [.. Bars.Where(bar => bar.Target is not null && Overlaps(bar, inSource))];
        Tick start = Alignment.TickAt(inSource.Onset.Value * 1000);
        Tick end = Alignment.TickAt(inSource.End.Value * 1000);
        if (bars.Length == 0 || end <= start)
        {
            return null;
        }

        return (TickSpan.FromBounds(start, end), Worst(bars.Select(bar => bar.Kind)));
    }

    /// <summary>Where a stretch of the player's notes lies in the source: «Найти похожее» the other way.</summary>
    public TickSpan? InSource(TickSpan inNotes)
    {
        if (Alignment.Anchors.Count == 0)
        {
            return null;
        }

        long start = Alignment.MicrosecondsAt(inNotes.Onset) / 1000;
        long end = Alignment.MicrosecondsAt(inNotes.End) / 1000;
        return end > start && start >= 0 ? TickSpan.FromBounds(new Tick(start), new Tick(end)) : null;
    }

    /// <summary>
    /// The places «Принять совпавшее» takes: runs of bars that match but are not identical and land bar for bar on the
    /// player's notes, as source spans with where they go. Bars that only partly match or differ are left to the player.
    /// </summary>
    public IReadOnlyList<(TickSpan From, TickSpan To)> Corrections(SourceMusic source, SourceMusic notes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(notes);
        List<(TickSpan From, TickSpan To)> places = [];
        foreach (MatchBar bar in Bars)
        {
            if (bar.Kind != MatchKind.Same || bar.Identical || bar.Target is not { } target)
            {
                continue;
            }

            TickSpan from = source.Bars[bar.Index].Span;
            if (CleanBar(notes, target, from.Duration) is not { } to)
            {
                continue;
            }

            if (places.Count > 0 && places[^1].From.End == from.Onset && places[^1].To.End == to.Onset)
            {
                places[^1] = (TickSpan.FromBounds(places[^1].From.Onset, from.End), TickSpan.FromBounds(places[^1].To.Onset, to.End));
            }
            else
            {
                places.Add((from, to));
            }
        }

        return places;
    }

    /// <summary>How many places «Принять совпавшее» would take.</summary>
    public int CorrectionCount(SourceMusic source, SourceMusic notes) => Corrections(source, notes).Count;

    /// <summary>
    /// The source's bars in words against the notes: «Совпадает в тактах 1–29 и 42–64. В тактах 30–41 другой материал.
    /// На 8 тактов длиннее.»
    /// </summary>
    public string Summary(SourceMusic source, SourceMusic notes, Words words)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(notes);
        List<string> sentences = [];
        if (Segments.All(segment => segment.Kind == MatchKind.Same))
        {
            int corrections = CorrectionCount(source, notes);
            sentences.Add(corrections == 0
                ? words.T("Matches everywhere.", "Совпадает везде.")
                : words.T(
                    $"Matches everywhere. {words.Count(corrections, "place", "places", "место", "места", "мест")} with corrections: {BarList(CorrectedBars(source), words)}.",
                    $"Совпадает везде. {words.Count(corrections, "place", "places", "место", "места", "мест")} с поправками: {BarList(CorrectedBars(source), words)}."));
        }
        else
        {
            AddSentence(sentences, MatchKind.Same, words.T("Matches in", "Совпадает в"), words);
            AddSentence(sentences, MatchKind.Partly, words.T("Partly matches in", "Частично совпадает в"), words);
            AddSentence(sentences, MatchKind.Differs, words.T("Other material in", "Другой материал в"), words);
            AddSentence(sentences, MatchKind.NotFound, words.T("Not found in the notes:", "Не найдено в нотах:"), words);
        }

        int difference = source.Bars.Count - notes.Bars.Count;
        if (difference != 0)
        {
            int count = Math.Abs(difference);
            sentences.Add(words.T(
                $"{words.Count(count, "bar", "bars", "такт", "такта", "тактов")} {(difference > 0 ? "longer" : "shorter")}.",
                $"На {words.Count(count, "bar", "bars", "такт", "такта", "тактов")} {(difference > 0 ? "длиннее" : "короче")}."));
        }

        return string.Join(" ", sentences);
    }

    /// <summary>The kind in a word: «совпадает», «частично», «отличается», «не найдено».</summary>
    public static string Word(MatchKind kind, Words words) => kind switch
    {
        MatchKind.Same => words.T("matches", "совпадает"),
        MatchKind.Partly => words.T("partly", "частично"),
        MatchKind.Differs => words.T("differs", "отличается"),
        _ => words.T("not found", "не найдено"),
    };

    private void AddSentence(List<string> sentences, MatchKind kind, string start, Words words)
    {
        MatchSegment[] runs = [.. Segments.Where(segment => segment.Kind == kind)];
        if (runs.Length == 0)
        {
            return;
        }

        string bars = words.Joined([.. runs.Select(run => Words.Range(run.First, run.Last))]);
        bool one = runs.Length == 1 && runs[0].First == runs[0].Last;
        sentences.Add(kind == MatchKind.NotFound
            ? $"{start} {words.Bars(bars, one)}."
            : words.T($"{start} {(one ? "bar" : "bars")} {bars}.", $"{start} {(one ? "такте" : "тактах")} {bars}."));
    }

    private IReadOnlyList<int> CorrectedBars(SourceMusic source) =>
        [.. Bars.Where(bar => bar.Kind == MatchKind.Same && !bar.Identical && bar.Clean).Select(bar => source.NumberOf(bar.Index))];

    private static string BarList(IReadOnlyList<int> bars, Words words) =>
        words.Bars(string.Join(", ", StructureAnalyzer.Runs(bars).Select(run => Words.Range(run.First, run.Last))), bars.Count == 1);

    private static IReadOnlyList<MatchSegment> RunsOf(SourceMusic source, IReadOnlyList<MatchBar> bars)
    {
        List<MatchSegment> segments = [];
        foreach (MatchBar bar in bars)
        {
            int number = source.NumberOf(bar.Index);
            TickSpan span = source.Bars[bar.Index].Span;
            if (segments.Count > 0 && segments[^1].Kind == bar.Kind && segments[^1].Last == number - 1)
            {
                segments[^1] = segments[^1] with { Last = number, Span = TickSpan.FromBounds(segments[^1].Span.Onset, span.End) };
            }
            else
            {
                segments.Add(new MatchSegment(bar.Kind, number, number, span));
            }
        }

        return segments;
    }

    /// <summary>The bar of the notes that a target lands on, when it starts within a 16th of a bar line and is just as long.</summary>
    private static TickSpan? CleanBar(SourceMusic notes, TickSpan target, long length)
    {
        foreach (AssemblyBar bar in notes.Bars)
        {
            if (Math.Abs(bar.Span.Onset.Value - target.Onset.Value) <= Tolerance)
            {
                return bar.Span.Duration == length ? bar.Span : null;
            }
        }

        return null;
    }

    private static bool Overlaps(MatchBar bar, TickSpan inSource) => bar.Span.Overlaps(inSource);

    private static MatchKind Worst(IEnumerable<MatchKind> kinds) => kinds.DefaultIfEmpty(MatchKind.NotFound).Max();
}