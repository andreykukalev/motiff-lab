using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// Rhythm facts beyond lengths, grid and silences (S1-A1): how beats are subdivided, syncopations,
/// accents where the file marks them, and rhythms that repeat inside the motif.
/// </summary>
/// <remarks>
/// An onset is where one or more notes of the motif start; notes that start together count once,
/// and the longest of them decides how long the onset is held.
/// </remarks>
public static partial class MotifAnalyzer
{
    /// <summary>A list in a fact names at most this many places, then says how many more there are.</summary>
    private const int MaxListed = 6;

    private const string PerformanceBasis = " Onsets fall between grid lines, as in a played performance, so this may not match the written rhythm.";

    /// <summary>One onset of the motif and how long its longest note sounds.</summary>
    private readonly record struct Hit(Tick At, Tick End);

    /// <summary>A beat of the selection: where it starts and ends, and the bar it is in.</summary>
    private readonly record struct BeatSlot(int Bar, Tick Start, Tick End);

    private static Hit[] Hits(IEnumerable<IGrouping<Tick, Note>> onsets) =>
        [.. onsets.Select(group => new Hit(group.Key, group.Max(note => note.Span.End))).OrderBy(hit => hit.At)];

    /// <summary>The beats a beat-aligned span covers, each cut at its bar line.</summary>
    private static List<BeatSlot> Beats(MeterMap meter, TickSpan span)
    {
        var beats = new List<BeatSlot>();
        Tick start = span.Onset;
        while (start < span.End)
        {
            Bar bar = meter.BarAt(start);
            MetricPosition position = meter.ToPosition(start);
            long end = Math.Min(start.Value + bar.Signature.BeatTicks - position.BeatOffset, Math.Min(bar.Span.End.Value, span.End.Value));
            beats.Add(new BeatSlot(bar.Number, start, new Tick(end)));
            start = new Tick(end);
        }

        return beats;
    }

    private static MotifFact GroupingFact(Hit[] hits, MeterMap meter, TickSpan span, bool offGrid)
    {
        List<BeatSlot> beats = Beats(meter, span);
        int[] counts = [.. beats.Select(beat => hits.Count(hit => hit.At >= beat.Start && hit.At < beat.End))];
        string perBar = string.Join(" | ", beats
            .Select((beat, i) => (beat.Bar, Count: counts[i]))
            .GroupBy(beat => beat.Bar)
            .Select(bar => string.Join(" ", bar.Select(beat => beat.Count))));

        string text = $"Onsets per beat, bar by bar: {perBar}.";
        IGrouping<int, int> common = counts.GroupBy(count => count).OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key).First();
        if (common.Count() == counts.Length && counts.Length > 1)
        {
            text += $" Every beat {BeatSplit(common.Key, plural: false)}.";
        }
        else if (common.Count() * 2 > counts.Length)
        {
            text += $" Most beats {BeatSplit(common.Key, plural: true)} ({common.Count()} of {counts.Length}).";
        }

        const string Basis = "Counted per beat of the time signature, from the bar lines; notes that start together count as one onset.";
        return offGrid
            ? new MotifFact("rhythm.grouping", FactLens.Rhythm, FactCertainty.Uncertain, text, Basis + PerformanceBasis)
            : new MotifFact("rhythm.grouping", FactLens.Rhythm, FactCertainty.Detected, text, Basis);
    }

    private static string BeatSplit(int onsets, bool plural) => (onsets switch
    {
        0 => "{0} no onset",
        1 => "{0} one onset",
        2 => "{1} in two",
        3 => "{1} in three",
        4 => "{1} in four",
        _ => $"{{0}} {onsets} onsets",
    }).Replace("{0}", plural ? "hold" : "holds", StringComparison.Ordinal).Replace("{1}", plural ? "split" : "splits", StringComparison.Ordinal);

    private static MotifFact SyncopationFact(Hit[] hits, MeterMap meter, bool offGrid)
    {
        var found = new List<string>();
        foreach (Hit hit in hits)
        {
            Bar bar = meter.BarAt(hit.At);
            MetricPosition position = meter.ToPosition(hit.At);
            if (position.BeatOffset != 0)
            {
                Tick nextBeat = hit.At.Offset(bar.Signature.BeatTicks - position.BeatOffset);
                if (hit.End > nextBeat)
                {
                    found.Add(nextBeat >= bar.Span.End
                        ? $"bar {position.Bar} after beat {position.Beat}, held across the bar line"
                        : $"bar {position.Bar} after beat {position.Beat}, held into beat {position.Beat + 1}");
                }
            }
            else if (position.Beat != 1 && hit.End > bar.Span.End)
            {
                found.Add($"bar {position.Bar} beat {position.Beat}, held across the bar line");
            }
        }

        string text = found.Count == 0
            ? "No syncopation: no onset off the beat is held into the next beat, and no note is held across the bar line from a weak beat."
            : $"{Count(found.Count, "syncopation", "syncopations")}: {Listed(found)}.";
        const string Basis = "An onset is syncopated when it starts off the beat and still sounds on the next beat, or starts on a beat other than the first and sounds across the bar line. Tied notes count as one note.";
        return offGrid
            ? new MotifFact("rhythm.syncopation", FactLens.Rhythm, FactCertainty.Uncertain, text, Basis + PerformanceBasis)
            : new MotifFact("rhythm.syncopation", FactLens.Rhythm, FactCertainty.Detected, text, Basis);
    }

    private static MotifFact AccentFact(Note[] notes, IEnumerable<AccentMark> accents, MeterMap meter, MusicalKey key)
    {
        var marks = new Dictionary<string, AccentKind>(StringComparer.Ordinal);
        foreach (AccentMark mark in accents)
        {
            // A strong accent wins over a plain one written on the same note.
            if (!marks.TryGetValue(mark.NoteId, out AccentKind kind) || mark.Kind > kind)
            {
                marks[mark.NoteId] = mark.Kind;
            }
        }

        var accented = notes.Where(note => marks.ContainsKey(note.Id)).ToList();
        string text = accented.Count == 0
            ? "No accents are marked on the motif's notes."
            : $"Accents marked on {accented.Count} of {Count(notes.Length, "note", "notes")}: " + Listed(accented
                .Select(note => $"{key.Spell(note.Pitch.MidiNumber)} at {Place(meter.ToPosition(note.Span.Onset))}{(marks[note.Id] == AccentKind.StrongAccent ? " (strong accent)" : "")}")
                .ToList()) + ".";
        return new MotifFact("rhythm.accents", FactLens.Rhythm, FactCertainty.Detected, text, "Read from the accent marks written in the file (> accent, ^ strong accent); loudness in the notes is not counted.");
    }

    private static MotifFact PatternFact(Hit[] hits, MeterMap meter, TickSpan span, bool offGrid)
    {
        var sentences = new List<string>();

        // Bars wholly inside the selection with the same onsets from the bar line.
        var bars = new List<(int Number, string Rhythm)>();
        for (Bar bar = meter.BarAt(span.Onset); bar.Span.Onset < span.End; bar = meter.GetBar(bar.Number + 1))
        {
            Hit[] inBar = [.. hits.Where(hit => hit.At >= bar.Span.Onset && hit.At < bar.Span.End)];
            if (bar.Span.Onset >= span.Onset && bar.Span.End <= span.End && inBar.Length > 0)
            {
                bars.Add((bar.Number, $"{bar.Span.Duration}:{string.Join(",", inBar.Select(hit => hit.At.Value - bar.Span.Onset.Value))}"));
            }
        }

        string[] barGroups = [.. bars
            .GroupBy(bar => bar.Rhythm)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.First().Number)
            .Select(group => Joined(group.Select(bar => bar.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList()))];
        if (barGroups.Length > 0)
        {
            sentences.Add($"Bars {barGroups[0]} have the same rhythm" + string.Concat(barGroups.Skip(1).Select(group => $"; so do bars {group}")) + ".");
        }

        // Figures inside a beat: onsets and how long each lasts until the next one or the end of the beat.
        // A beat holding one note on the beat is no figure.
        var figures = new List<string>();
        foreach (BeatSlot beat in Beats(meter, span))
        {
            Hit[] inBeat = [.. hits.Where(hit => hit.At >= beat.Start && hit.At < beat.End)];
            if (inBeat.Length == 0 || (inBeat.Length == 1 && inBeat[0].At == beat.Start))
            {
                continue;
            }

            var parts = new List<string>();
            if (inBeat[0].At > beat.Start)
            {
                parts.Add($"{ValueName(inBeat[0].At.Value - beat.Start.Value)} rest");
            }

            for (int i = 0; i < inBeat.Length; i++)
            {
                Tick until = i + 1 < inBeat.Length ? inBeat[i + 1].At : beat.End;
                parts.Add(ValueName(until.Value - inBeat[i].At.Value));
            }

            figures.Add(string.Join("–", parts));
        }

        IGrouping<string, string>? figure = figures
            .GroupBy(name => name)
            .Where(group => group.Count() > 1)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => figures.IndexOf(group.Key))
            .FirstOrDefault();
        if (figure is not null)
        {
            sentences.Add($"The figure {figure.Key} fills {figure.Count()} beats.");
        }

        string text = sentences.Count == 0 ? "No bar rhythm or beat figure repeats within the motif." : string.Join(" ", sentences);
        const string Basis = "Bars are compared by where their onsets fall from the bar line, counting only bars wholly inside the selection; figures by the onsets inside one beat. A beat holding one note on the beat is not a figure.";
        return offGrid
            ? new MotifFact("rhythm.patterns", FactLens.Rhythm, FactCertainty.Uncertain, text, Basis + PerformanceBasis)
            : new MotifFact("rhythm.patterns", FactLens.Rhythm, FactCertainty.Detected, text, Basis);
    }

    /// <summary>"sixteenth", or "odd length" for a length that is no single note value.</summary>
    private static string ValueName(long ticks)
    {
        int index = Array.FindIndex(s_values, value => value.Ticks == ticks);
        return index < 0 ? "odd length" : s_values[index].One;
    }

    /// <summary>"bar 2 beat 1", or "bar 2 after beat 1" off the beat.</summary>
    private static string Place(MetricPosition position) =>
        position.BeatOffset == 0 ? $"bar {position.Bar} beat {position.Beat}" : $"bar {position.Bar} after beat {position.Beat}";

    /// <summary>Items joined with semicolons, the list cut after <see cref="MaxListed"/>.</summary>
    private static string Listed(IReadOnlyList<string> items) =>
        items.Count <= MaxListed
            ? string.Join("; ", items)
            : $"{string.Join("; ", items.Take(MaxListed))}; and {items.Count - MaxListed} more";

    /// <summary>"1 and 3", "1, 2 and 4".</summary>
    private static string Joined(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";
}