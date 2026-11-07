using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The form strip of Piece DNA (testing item 22): the sentences of the piece (<see cref="SentenceMap"/>) as its sections,
/// or its phrases when it reads as one sentence, lettered by the material they share. A section whose line repeats, bar by bar from its start, at least half the bars of
/// an earlier section takes that section's letter: the same letter when every bar repeats exactly and the length is the
/// same, with a prime (A′, A″) when it comes back changed; any other section takes the next letter. Inferred, never read
/// from the file.
/// </summary>
/// <remarks>
/// Lines are the highest note at each onset of all the notes, compared as <see cref="StructureAnalyzer"/> compares bars
/// for new material: the same onsets from the bar line with the same pitches, or the same intervals for a transposed
/// repeat. Pure: the same notes, meter and sentences give the same sections.
/// </remarks>
public static class FormAnalyzer
{
    /// <summary>The share of a section's bars that must repeat bars of an earlier section for it to bring that one back.</summary>
    public const double SameMaterial = 0.5;

    /// <summary>The fewest bars a section has; a shorter part joins its neighbour.</summary>
    public const int MinBars = 2;

    private static readonly string[] s_primes = ["", "′", "″", "‴", "⁗"];

    /// <summary>The sections, in time order, one per sentence; empty without notes.</summary>
    public static IReadOnlyList<DnaSection> Sections(IReadOnlyList<Note> notes, MeterMap meter, SentenceMap sentences)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(sentences);
        if (notes.Count == 0 || sentences.Sentences.Count == 0)
        {
            return [];
        }

        // A piece read as one sentence is a single section: its phrases show its form instead (a a′ in a period).
        Sentence[] parts = sentences.Sentences.Count > 1
            ? [.. sentences.Sentences]
            : [.. sentences.Sentences[0].Phrases.Select(phrase => new Sentence(phrase.Number, phrase.Span, phrase.FirstBar, phrase.LastBar, [phrase], sentences.Sentences[0].Found, sentences.Sentences[0].Basis))];
        parts = Joined(parts);
        if (parts.Length == 0)
        {
            return [];
        }

        Dictionary<int, (string Pitches, string Shape)?> bars = BarLines(StructureAnalyzer.Line(notes), meter);
        var sections = new List<(Sentence Sentence, int Group, string Label)>();
        var own = new List<List<(string Pitches, string Shape)>>();
        var groups = new List<int>();
        foreach (Sentence sentence in parts)
        {
            int group = -1;
            double best = 0;
            bool exact = false;
            for (int i = 0; i < sections.Count; i++)
            {
                (Sentence earlier, int g, _) = sections[i];
                (double share, bool same) = Compare(earlier, own[i], sentence, bars);
                if (share >= SameMaterial && (share > best || (share == best && same && !exact)))
                {
                    (group, best, exact) = (g, share, same);
                }
            }

            own.Add(Own(sentence, own.SelectMany(bars => bars), bars));
            if (group < 0)
            {
                groups.Add(0);
                sections.Add((sentence, groups.Count - 1, Letter(groups.Count - 1)));
                continue;
            }

            int returns = groups[group] + (exact ? 0 : 1);
            groups[group] = returns;
            sections.Add((sentence, group, exact ? Letter(group) : Letter(group) + s_primes[Math.Min(returns, s_primes.Length - 1)]));
        }

        return [.. sections.Select(section => new DnaSection(
            Id(section.Sentence),
            section.Label,
            section.Sentence.Span,
            section.Sentence.FirstBar,
            section.Sentence.LastBar,
            [.. sections.Where(other => other.Group == section.Group && other.Sentence != section.Sentence).Select(other => Id(other.Sentence))]))];
    }

    /// <summary>A part shorter than <see cref="MinBars"/> is no section of its own: it joins the one before it, or the first the one after.</summary>
    private static Sentence[] Joined(Sentence[] parts)
    {
        var joined = new List<Sentence>();
        foreach (Sentence part in parts)
        {
            if (joined.Count > 0 && (part.BarCount < MinBars || joined[^1].BarCount < MinBars))
            {
                Sentence before = joined[^1];
                joined[^1] = before with
                {
                    Span = TickSpan.FromBounds(before.Span.Onset, part.Span.End),
                    LastBar = part.LastBar,
                    Phrases = [.. before.Phrases, .. part.Phrases],
                };
                continue;
            }

            joined.Add(part);
        }

        return [.. joined.Select((part, i) => part with { Number = i + 1 })];
    }

    private static string Id(Sentence sentence) => $"section_{sentence.Number}";

    private static string Letter(int group) => group < 26 ? ((char)('A' + group)).ToString() : (group + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// How much of the later section repeats the earlier one: the share of its bars with notes whose line repeats one of
    /// the earlier section's own bars, exactly or transposed; and whether it repeats the earlier one exactly, bar for bar.
    /// </summary>
    private static (double Share, bool Exact) Compare(Sentence earlier, IReadOnlyList<(string Pitches, string Shape)> own, Sentence later, Dictionary<int, (string Pitches, string Shape)?> bars)
    {
        var pitches = own.Select(bar => bar.Pitches).ToHashSet(StringComparer.Ordinal);
        var shapes = own.Select(bar => bar.Shape).ToHashSet(StringComparer.Ordinal);
        int played = 0;
        int repeating = 0;
        bool exact = earlier.BarCount == later.BarCount;
        for (int k = 0; k < later.BarCount; k++)
        {
            (string Pitches, string Shape)? bar = bars.GetValueOrDefault(later.FirstBar + k);
            exact &= bar?.Pitches == bars.GetValueOrDefault(earlier.FirstBar + k)?.Pitches;
            if (bar is not { } line)
            {
                continue;
            }

            played++;
            if (pitches.Contains(line.Pitches) || shapes.Contains(line.Shape))
            {
                repeating++;
            }
        }

        return played == 0 ? (0, false) : ((double)repeating / played, exact);
    }

    /// <summary>
    /// The bars a section brings itself: those whose line repeats no bar of an earlier section, so a section is not
    /// matched by the material it took from another.
    /// </summary>
    private static List<(string Pitches, string Shape)> Own(Sentence section, IEnumerable<(string Pitches, string Shape)> before, Dictionary<int, (string Pitches, string Shape)?> bars)
    {
        (string Pitches, string Shape)[] earlier = [.. before];
        var pitches = earlier.Select(bar => bar.Pitches).ToHashSet(StringComparer.Ordinal);
        var shapes = earlier.Select(bar => bar.Shape).ToHashSet(StringComparer.Ordinal);
        return [.. Enumerable.Range(section.FirstBar, section.BarCount)
            .Select(bars.GetValueOrDefault)
            .OfType<(string Pitches, string Shape)>()
            .Where(bar => !pitches.Contains(bar.Pitches) && !shapes.Contains(bar.Shape))];
    }

    /// <summary>Each bar's line keys; a bar with no onset is absent, and a bar of one note has no shape to compare.</summary>
    private static Dictionary<int, (string Pitches, string Shape)?> BarLines(Note[] line, MeterMap meter)
    {
        var bars = new Dictionary<int, (string Pitches, string Shape)?>();
        foreach (IGrouping<int, Note> bar in line.GroupBy(note => meter.BarAt(note.Span.Onset).Number))
        {
            Note[] inBar = [.. bar];
            (string pitches, string shape) = StructureAnalyzer.BarLine(meter.GetBar(bar.Key), inBar);
            bars[bar.Key] = (pitches, inBar.Length > 1 ? shape : pitches);
        }

        return bars;
    }
}