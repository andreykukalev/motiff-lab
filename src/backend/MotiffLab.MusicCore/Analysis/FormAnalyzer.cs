using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The form strip of Piece DNA (testing item 22) and the Contents (#347): the sections of the piece, lettered by the
/// material they share. Where the opening comes back (#365), a return of the opening starts a section, even inside a
/// sentence: a return is a phrase that repeats the opening phrase after one that does not. The section a return starts
/// holds the phrases that bring back, one by one, the phrases that followed the opening, at most as many as since the
/// return before; the opening's own section is as long as the longest of them, ending on a phrase that repeats the
/// opening, so a return inside it (the last a of a refrain a a b a) stays in it. The music between two such sections, an
/// episode, keeps its own sentences as sections (A B C D A, a sonata-rondo A B A C A B′ A), except a last sentence that
/// ends on no full close: that is the episode's lead-in back to the refrain and joins the sentence before it. Where the
/// opening never comes back, the sections are the sentences (<see cref="SentenceMap"/>), or the phrases when the piece
/// reads as one sentence. A section whose line repeats, bar by bar from its start, at least half the bars of an earlier
/// section takes that section's letter: the same letter when it brings no material of its own, every bar of its line
/// repeating exactly a bar of the first section with that letter (a pickup bar it starts within and a close that leaves
/// out a written ending's lead-in aside), with a prime (A′, A″) when it comes back changed; any other section takes the
/// next letter. Inferred, never read from the file.
/// </summary>
/// <remarks>
/// Lines are the highest note at each onset of all the notes, compared as <see cref="StructureAnalyzer"/> compares bars
/// for new material: the same onsets from the bar line with the same pitches, or the same intervals for a transposed
/// repeat. A phrase repeats another as a section brings an earlier one back: at least half its bars repeat the other's.
/// Pure: the same notes, meter and sentences give the same sections.
/// </remarks>
public static class FormAnalyzer
{
    /// <summary>The share of a section's bars that must repeat bars of an earlier section for it to bring that one back.</summary>
    public const double SameMaterial = 0.5;

    /// <summary>The fewest bars a section has; a shorter part joins its neighbour.</summary>
    public const int MinBars = 2;

    private static readonly string[] s_primes = ["", "′", "″", "‴"];

    private static readonly string[] s_superscripts = ["⁰", "¹", "²", "³", "⁴", "⁵", "⁶", "⁷", "⁸", "⁹"];

    /// <summary>The sections, in time order; empty without notes.</summary>
    public static IReadOnlyList<DnaSection> Sections(IReadOnlyList<Note> notes, MeterMap meter, SentenceMap sentences)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(sentences);
        if (notes.Count == 0 || sentences.Sentences.Count == 0)
        {
            return [];
        }

        (Dictionary<int, (string Pitches, string Shape)?> bars, Dictionary<int, string[]> openings) = BarLines(StructureAnalyzer.Line(notes), meter);

        // Each phrase as a part, numbered by its sentence until Joined numbers the sections.
        Sentence[] phrases = [.. sentences.Sentences.SelectMany(sentence => sentence.Phrases.Select(phrase =>
            sentence with { Span = phrase.Span, FirstBar = phrase.FirstBar, LastBar = phrase.LastBar, Phrases = [phrase] }))];

        // Without a return of the opening, a piece read as one sentence shows its phrases instead (a a′ in a period).
        HashSet<int> closed = [.. sentences.Closes.Where(close => close.Kind == CloseKind.Full).Select(close => close.Phrase.Number)];
        Sentence[] parts = Joined(ByReturns(phrases, closed, bars) ?? (sentences.Sentences.Count > 1 ? [.. sentences.Sentences] : phrases));
        var sections = new List<(Sentence Sentence, int Group, string Label)>();
        var own = new List<List<(string Pitches, string Shape)>>();
        // Each letter's first section, which a return is unchanged against, and how many times it came back changed.
        var groups = new List<(Sentence First, int Returns)>();
        foreach (Sentence sentence in parts)
        {
            int group = -1;
            double best = 0;
            bool unchanged = false;
            for (int i = 0; i < sections.Count; i++)
            {
                int g = sections[i].Group;
                double share = Compare(own[i], sentence, bars);
                if (share < SameMaterial)
                {
                    continue;
                }

                bool same = Unchanged(groups[g].First, sentence, bars, openings, meter);
                if (share > best || (share == best && same && !unchanged))
                {
                    (group, best, unchanged) = (g, share, same);
                }
            }

            own.Add(Own(sentence, own.SelectMany(bars => bars), bars));
            if (group < 0)
            {
                groups.Add((sentence, 0));
                sections.Add((sentence, groups.Count - 1, Letter(groups.Count - 1)));
                continue;
            }

            int returns = groups[group].Returns + (unchanged ? 0 : 1);
            groups[group] = groups[group] with { Returns = returns };
            sections.Add((sentence, group, unchanged ? Letter(group) : Letter(group) + Primes(returns)));
        }

        return [.. sections.Select(section => new DnaSection(
            Id(section.Sentence),
            section.Label,
            section.Sentence.Span,
            section.Sentence.FirstBar,
            section.Sentence.LastBar,
            [.. sections.Where(other => other.Group == section.Group && other.Sentence != section.Sentence).Select(other => Id(other.Sentence))]))];
    }

    /// <summary>
    /// The span a tap on <paramref name="section"/> selects: the whole section when it fits a selection
    /// (<see cref="Motifs.MotifSelection.MaxStructureBars"/>), otherwise its first bars up to that many, as
    /// <see cref="PhraseLevels.Selectable"/> gives for a long sentence.
    /// </summary>
    public static TickSpan Selectable(DnaSection section, MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(meter);
        if (section.LastBar - section.FirstBar + 1 <= Motifs.MotifSelection.MaxStructureBars)
        {
            return section.Span;
        }

        Tick stop = meter.GetBar(section.FirstBar + Motifs.MotifSelection.MaxStructureBars - 1).Span.End;
        return TickSpan.FromBounds(section.Span.Onset, stop < section.Span.End ? stop : section.Span.End);
    }

    /// <summary>
    /// The sections where the opening comes back (#365), as the summary says, or null when it never does. Each phrase is
    /// one part of <paramref name="phrases"/>, numbered by its sentence; <paramref name="closed"/> holds the numbers of
    /// the phrases that end on a full close.
    /// </summary>
    private static Sentence[]? ByReturns(Sentence[] phrases, HashSet<int> closed, Dictionary<int, (string Pitches, string Shape)?> bars)
    {
        List<(string Pitches, string Shape)>[] own = [.. phrases.Select(phrase => Own(phrase, [], bars))];
        bool Repeats(int earlier, int later) => Compare(own[earlier], phrases[later], bars) >= SameMaterial;
        bool[] opening = [.. phrases.Select((_, at) => Repeats(0, at))];

        // How many phrases from `at` repeat, one by one, those from the opening, at most `most`.
        int Brought(int at, int most)
        {
            int count = 0;
            while (count < most && at + count < phrases.Length && Repeats(count, at + count))
            {
                count++;
            }

            return count;
        }

        // The phrases that repeat the opening phrase from `at`, in a row: the opening's own answer stays with it.
        int InARow(int at)
        {
            int count = 0;
            while (at + count < phrases.Length && opening[at + count])
            {
                count++;
            }

            return count;
        }

        int[] returns = [.. Enumerable.Range(1, Math.Max(0, phrases.Length - 1)).Where(at => opening[at] && !opening[at - 1])];
        if (returns.Length == 0)
        {
            return null;
        }

        // A return brings back no more than the stretch since the return before, so a refrain heard twice in a row is two
        // returns, not one twice as long; the opening's section ends on a phrase that repeats the opening, so an episode
        // that follows the refrain every time is not taken into it (A B A B A stays five sections).
        int[] brought = [.. returns.Select((at, i) => Brought(at, at - (i == 0 ? 0 : returns[i - 1])))];
        int refrain = brought.Max();
        while (refrain > 1 && !opening[refrain - 1])
        {
            refrain--;
        }

        var sections = new List<Sentence>();
        int end = Math.Max(refrain, InARow(0));
        sections.Add(Merged(phrases[..end]));
        for (int i = 0; i < returns.Length; i++)
        {
            int at = returns[i];
            if (at < end)
            {
                continue;
            }

            if (at > end)
            {
                sections.AddRange(Episode(phrases, end, at, closed));
            }

            end = at + Math.Max(Math.Min(refrain, brought[i]), InARow(at));
            sections.Add(Merged(phrases[at..end]));
        }

        if (end < phrases.Length)
        {
            sections.AddRange(Episode(phrases, end, phrases.Length, closed));
        }

        return [.. sections];
    }

    /// <summary>
    /// The episode from phrase <paramref name="from"/> to <paramref name="to"/>, one section for each of its sentences; a
    /// last sentence whose last phrase ends on no full close leads back into the return at <paramref name="to"/> and
    /// joins the sentence before it.
    /// </summary>
    private static IEnumerable<Sentence> Episode(Sentence[] phrases, int from, int to, HashSet<int> closed)
    {
        Sentence[][] pieces = [.. phrases[from..to].GroupBy(phrase => phrase.Number, (_, piece) => piece.ToArray())];
        if (pieces.Length > 1 && to < phrases.Length && !closed.Contains(phrases[to - 1].Phrases[0].Number))
        {
            pieces = [.. pieces[..^2], [.. pieces[^2], .. pieces[^1]]];
        }

        return pieces.Select(Merged);
    }

    /// <summary>A part shorter than <see cref="MinBars"/> is no section of its own: it joins the one before it, or the first the one after.</summary>
    private static Sentence[] Joined(Sentence[] parts)
    {
        var joined = new List<Sentence>();
        foreach (Sentence part in parts)
        {
            if (joined.Count > 0 && (part.BarCount < MinBars || joined[^1].BarCount < MinBars))
            {
                joined[^1] = Merged([joined[^1], part]);
                continue;
            }

            joined.Add(part);
        }

        return [.. joined.Select((part, i) => part with { Number = i + 1 })];
    }

    /// <summary>Parts in a row as one part, from the first's start to the last's end.</summary>
    private static Sentence Merged(Sentence[] parts) => parts[0] with
    {
        Span = TickSpan.FromBounds(parts[0].Span.Onset, parts[^1].Span.End),
        LastBar = parts[^1].LastBar,
        Phrases = [.. parts.SelectMany(part => part.Phrases)],
    };

    /// <summary>′, ″ and ‴ for the first three returns, then the return's number raised: A⁴, A⁵.</summary>
    private static string Primes(int returns) =>
        returns < s_primes.Length
            ? s_primes[returns]
            : string.Concat(returns.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(digit => s_superscripts[digit - '0']));

    private static string Id(Sentence sentence) => $"section_{sentence.Number}";

    private static string Letter(int group) => group < 26 ? ((char)('A' + group)).ToString() : (group + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// How much of the later section repeats an earlier one: the share of its bars with notes whose line repeats one of
    /// the earlier section's own bars, exactly or transposed.
    /// </summary>
    private static double Compare(IReadOnlyList<(string Pitches, string Shape)> own, Sentence later, Dictionary<int, (string Pitches, string Shape)?> bars)
    {
        var pitches = own.Select(bar => bar.Pitches).ToHashSet(StringComparer.Ordinal);
        var shapes = own.Select(bar => bar.Shape).ToHashSet(StringComparer.Ordinal);
        int played = 0;
        int repeating = 0;
        for (int k = 0; k < later.BarCount; k++)
        {
            if (bars.GetValueOrDefault(later.FirstBar + k) is not { } line)
            {
                continue;
            }

            played++;
            if (pitches.Contains(line.Pitches) || shapes.Contains(line.Shape))
            {
                repeating++;
            }
        }

        return played == 0 ? 0 : (double)repeating / played;
    }

    /// <summary>
    /// Whether the later section brings the earlier one, the first with its letter, back unchanged, with no material of its
    /// own: every bar of its line repeats exactly a bar of the earlier one's, except a first bar it starts within (its
    /// pickup, which holds the end of the section before) and a last bar that plays only the opening of one (the close,
    /// without the lead-in a written ending adds), so a refrain heard without its pickup or its repeat comes back the same.
    /// </summary>
    private static bool Unchanged(Sentence earlier, Sentence later, Dictionary<int, (string Pitches, string Shape)?> bars, Dictionary<int, string[]> openings, MeterMap meter)
    {
        int[] range = [.. Enumerable.Range(earlier.FirstBar, earlier.BarCount)];
        var pitches = range.Select(bars.GetValueOrDefault).OfType<(string Pitches, string Shape)>().Select(bar => bar.Pitches).ToHashSet(StringComparer.Ordinal);
        var opened = range.SelectMany(bar => openings.GetValueOrDefault(bar, [])).ToHashSet(StringComparer.Ordinal);
        int from = later.Span.Onset > meter.GetBar(later.FirstBar).Span.Onset ? later.FirstBar + 1 : later.FirstBar;
        return Enumerable.Range(from, Math.Max(0, later.LastBar - from + 1)).All(bar =>
            bars.GetValueOrDefault(bar) is not { } line || pitches.Contains(line.Pitches) || (bar == later.LastBar && opened.Contains(line.Pitches)));
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

    /// <summary>
    /// Each bar's line keys, and the pitch keys of its openings (its first notes, one note or more short of the whole
    /// line); a bar with no onset is absent, and a bar of one note has no shape to compare.
    /// </summary>
    private static (Dictionary<int, (string Pitches, string Shape)?> Bars, Dictionary<int, string[]> Openings) BarLines(Note[] line, MeterMap meter)
    {
        var bars = new Dictionary<int, (string Pitches, string Shape)?>();
        var openings = new Dictionary<int, string[]>();
        foreach (IGrouping<int, Note> bar in line.GroupBy(note => meter.BarAt(note.Span.Onset).Number))
        {
            Note[] inBar = [.. bar];
            Bar at = meter.GetBar(bar.Key);
            (string pitches, string shape) = StructureAnalyzer.BarLine(at, inBar);
            bars[bar.Key] = (pitches, inBar.Length > 1 ? shape : pitches);
            openings[bar.Key] = [.. Enumerable.Range(1, inBar.Length - 1).Select(count => StructureAnalyzer.BarLine(at, inBar[..count]).Pitches)];
        }

        return (bars, openings);
    }
}