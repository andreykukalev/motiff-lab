using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assembly;

/// <summary>How a stretch of a source compares with the player's notes, in words (decision record 0024, section 6).</summary>
public enum MatchKind
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
public sealed record MatchBar(int Index, TickSpan Span, MatchKind Kind, int Matched, int Total, TickSpan? Target, bool Identical, bool Clean);

/// <summary>A run of bars of the same kind: «такты 1–29 совпадают».</summary>
/// <param name="First">The first source bar's number.</param>
/// <param name="Last">The last source bar's number.</param>
public sealed record MatchSegment(MatchKind Kind, int First, int Last, TickSpan Span);

/// <summary>
/// A source compared with the player's notes as they are now, bar by bar of the source (decision record 0024,
/// section 6): the alignment of 0022 between the two, then for each bar how many of its notes are found at their place.
/// Stamped with the journal it was made for, so nothing is taken on a stale comparison.
/// </summary>
public sealed record AssemblyMatch(string SourceId, string Stamp, IReadOnlyList<MatchBar> Bars, IReadOnlyList<MatchSegment> Segments)
{
    /// <summary>At least this share of a bar's notes found in place, or all but one of at least half: «совпадает» (with a correction).</summary>
    public const double SameShare = 0.85;

    /// <summary>At least this share: «частично».</summary>
    public const double PartlyShare = 0.5;

    /// <summary>How far off its place a note may be found, in ticks: a 16th.</summary>
    public const long Tolerance = CanonicalTime.TicksPerQuarter / 4;

    private PerformanceAlignment Alignment { get; init; } = null!;

    /// <summary>Compares <paramref name="source"/> with <paramref name="notes"/>; <paramref name="stamp"/> names the journal the notes were built from.</summary>
    public static AssemblyMatch Of(string sourceId, SourceMusic source, SourceMusic notes, string stamp)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(notes);
        PlayedNote[] played = [.. source.Notes.Select(note => new PlayedNote(note.Id, note.Pitch, note.Span.Onset.Value * 1000, note.Velocity, note.Span.Duration * 1000))];
        PerformanceAlignment alignment = PerformanceAlignment.Align(notes.Notes, played);
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
            foreach (Note note in mine)
            {
                Tick at = alignment.TickAt(note.Span.Onset.Value * 1000);
                Note? match = theirs.Where(other => other.Pitch == note.Pitch && !used.Contains(other.Id) && Math.Abs(other.Span.Onset.Value - at.Value) <= Tolerance)
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
                : matched >= total * SameShare || (total - matched <= 1 && matched >= total * PartlyShare) ? MatchKind.Same
                : matched >= total * PartlyShare ? MatchKind.Partly
                : MatchKind.Differs;
            bool clean = target is { } landed && CleanBar(notes, landed, bar.Duration) is not null;
            bars.Add(new MatchBar(i, bar, kind, matched, total, target, identical || total == 0, clean));
        }

        return new AssemblyMatch(sourceId, stamp, bars, RunsOf(source, bars)) { Alignment = alignment };
    }

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

        string bars = words.Joined([.. runs.Select(run => run.First == run.Last ? $"{run.First}" : $"{run.First}–{run.Last}")]);
        bool one = runs.Length == 1 && runs[0].First == runs[0].Last;
        sentences.Add(kind == MatchKind.NotFound
            ? words.T($"{start} {(one ? "bar" : "bars")} {bars}.", $"{start} {(one ? "такт" : "такты")} {bars}.")
            : words.T($"{start} {(one ? "bar" : "bars")} {bars}.", $"{start} {(one ? "такте" : "тактах")} {bars}."));
    }

    private IReadOnlyList<int> CorrectedBars(SourceMusic source) =>
        [.. Bars.Where(bar => bar.Kind == MatchKind.Same && !bar.Identical && bar.Clean).Select(bar => source.NumberOf(bar.Index))];

    private static string BarList(IReadOnlyList<int> bars, Words words)
    {
        List<string> runs = [];
        for (int i = 0; i < bars.Count; i++)
        {
            int first = bars[i];
            while (i + 1 < bars.Count && bars[i + 1] == bars[i] + 1)
            {
                i++;
            }

            runs.Add(first == bars[i] ? $"{first}" : $"{first}–{bars[i]}");
        }

        string list = string.Join(", ", runs);
        return words.T(bars.Count == 1 ? $"bar {list}" : $"bars {list}", bars.Count == 1 ? $"такт {list}" : $"такты {list}");
    }

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