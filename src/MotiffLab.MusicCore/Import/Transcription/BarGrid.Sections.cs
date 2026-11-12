using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Transcription;

public sealed partial record BarGrid
{
    /// <summary>
    /// The grid that fits a live performance (version 0.5, «Темп живого исполнения»): the beat found afresh in each
    /// section of the recording (<see cref="GridSearch"/>), so a concert of several pieces, or a piece whose tempo jumps,
    /// keeps its bars. Each section is searched over its own opening, at the 16th of the one before when that does nearly
    /// as well, and tracked within 3/5 to 8/5 of its own start; its first downbeat starts the bars afresh. One signature
    /// for the whole recording, the one the sections support best together, and one number of 16ths a beat, the first
    /// section's. The grid is written as any other (<see cref="Beats"/>, <see cref="BarStarts"/>).
    /// </summary>
    public static GridFound FindLive(MidiImport import, GridSearch search, TimeSignature? signature = null, int? stepsPerBeat = null)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(search);
        Validate(signature, stepsPerBeat);
        var clock = new Clock(import.Tempos);
        Onset[] onsets = Cluster(import.Notes, clock);
        if (onsets.Length < MinOnsets)
        {
            return GridFound.None;
        }

        var live = new LiveSearch(onsets, Silences(import.Notes, clock, onsets), search, stepsPerBeat);
        List<LiveSection> sections = live.Sections();
        (long[] beats, int[] firstBeats) = Join(sections);
        long[] gaps = [.. beats.Zip(beats.Skip(1), (a, b) => b - a).Order()];
        long sixteenth = Math.Clamp(gaps[gaps.Length / 2] / live.PerBeat, ShortestSixteenth, LongestSixteenth);
        Tick origin = onsets[0].Tick;
        var grid = new BarGrid(origin, (int)sixteenth, TimeSignature.CommonTime, [0], beats, live.PerBeat);
        long[] positions = [.. live.Times.Select(grid.ThousandthsOfSteps)];
        if (!live.FirstHasBass && stepsPerBeat is null && 8 * Distance(positions) > positions.Length * 1000L)
        {
            return GridFound.None;
        }

        int[] steps = [.. positions.Select(position => (int)((position + 500) / 1000))];
        int[] lowest = [.. onsets.Select(onset => onset.Lowest)];
        int[] sectionStarts = [.. firstBeats.Select(beat => beat * live.PerBeat)];
        var bars = new SectionBars(sections, sectionStarts, steps, lowest);
        (TimeSignature picked, bool? fits) = bars.Pick(signature, search.Hint?.Signature);
        return new GridFound(new BarGrid(origin, (int)sixteenth, picked, bars.Starts(picked), beats, live.PerBeat), sections.Count, fits);
    }

    private static void Validate(TimeSignature? signature, int? stepsPerBeat)
    {
        if (signature is { } chosen && !Signatures.Contains(chosen))
        {
            throw new ArgumentException($"Bars of {chosen} are not offered.", nameof(signature));
        }

        if (stepsPerBeat is < FewestStepsPerBeat or > MostStepsPerBeat)
        {
            throw new ArgumentOutOfRangeException(nameof(stepsPerBeat), stepsPerBeat, "A beat is written as 1 to 16 16ths.");
        }
    }

    /// <summary>For each onset, how long nothing sounded before it, in microseconds; 0 for the first.</summary>
    private static long[] Silences(IReadOnlyList<Note> notes, Clock clock, Onset[] onsets)
    {
        (long Onset, long End)[] spans = [.. notes
            .Select(note => (clock.Units(note.Span.Onset) / Clock.UnitsPerMicrosecond, clock.Units(note.Span.End) / Clock.UnitsPerMicrosecond))
            .OrderBy(span => span.Item1)];
        var silences = new long[onsets.Length];
        long sounding = long.MinValue;
        int next = 0;
        for (int i = 0; i < onsets.Length; i++)
        {
            while (next < spans.Length && spans[next].Onset < onsets[i].Microseconds)
            {
                sounding = Math.Max(sounding, spans[next].End);
                next++;
            }

            silences[i] = i == 0 ? 0 : Math.Max(0, onsets[i].Microseconds - sounding);
        }

        return silences;
    }

    /// <summary>
    /// The sections' beats as one list: each section's from its first onset, the beat before it carried on at the tempo it
    /// had through the pause; and the index of each section's first beat.
    /// </summary>
    private static (long[] Beats, int[] FirstBeats) Join(List<LiveSection> sections)
    {
        var beats = new List<long>();
        var firsts = new int[sections.Count];
        for (int k = 0; k < sections.Count; k++)
        {
            long[] own = sections[k].Beats;
            if (beats.Count > 1)
            {
                long beat = beats[^1] - beats[^2];
                while (beats[^1] + beat + (beat / 2) < own[0])
                {
                    beats.Add(beats[^1] + beat);
                }
            }

            while (beats.Count > 0 && beats[^1] >= own[0])
            {
                beats.RemoveAt(beats.Count - 1);
            }

            firsts[k] = beats.Count;
            beats.AddRange(own);
        }

        return ([.. beats], firsts);
    }

    /// <summary>One section of a live performance: its onsets <c>[From, To)</c> and its beats, in microseconds after the grid's origin.</summary>
    private sealed record LiveSection(int From, int To, long[] Beats)
    {
        /// <summary>The section's typical beat.</summary>
        public long Beat => Beats.Length < 2 ? 0 : Beats.Zip(Beats.Skip(1), (a, b) => b - a).Order().ElementAt((Beats.Length - 1) / 2);
    }

    /// <summary>The sections of a live performance, found one after the other, each with its beat tracked afresh.</summary>
    private sealed class LiveSearch
    {
        private readonly Onset[] _onsets;
        private readonly long[] _silences;
        private readonly HashSet<int> _starts;
        private readonly GridHint? _hint;
        private readonly int? _steps;

        public LiveSearch(Onset[] onsets, long[] silences, GridSearch search, int? steps)
        {
            _onsets = onsets;
            _silences = silences;
            _hint = search.Hint;
            _steps = steps;
            long originUnits = onsets[0].Units;
            Times = [.. onsets.Select(onset => (onset.Units - originUnits) / Clock.UnitsPerMicrosecond)];
            long origin = onsets[0].Microseconds;
            _starts = [.. search.Starts.Select(start => Array.FindIndex(Times, time => time >= start - origin)).Where(index => index > 0)];
        }

        /// <summary>Every onset's time after the first, in microseconds.</summary>
        public long[] Times { get; }

        /// <summary>How many 16ths a beat is written as: the first section's, or as asked.</summary>
        public int PerBeat { get; private set; } = 2;

        public bool FirstHasBass { get; private set; }

        public List<LiveSection> Sections()
        {
            var sections = new List<LiveSection>();
            int from = 0;
            while (from < Times.Length)
            {
                LiveSection? previous = sections.Count == 0 ? null : sections[^1];
                LiveSection section = Track(from, Boundary(from, from + 1), previous);
                sections.Add(section);
                from = section.To;
            }

            return sections;
        }

        /// <summary>The first onset from <paramref name="after"/> on that starts a section: after a pause of 2.5 s, or at a start; else the end.</summary>
        private int Boundary(int from, int after)
        {
            for (int i = after; i < Times.Length; i++)
            {
                bool section = _starts.Contains(i) || _silences[i] >= GridSearch.SectionSilence;
                // The first section is long enough to find a beat in.
                if (section && (from > 0 || i >= MinOnsets))
                {
                    return i;
                }
            }

            return Times.Length;
        }

        private LiveSection Track(int from, int to, LiveSection? previous)
        {
            long[] local = [.. Times[from..to].Select(time => time - Times[from])];
            Onset[] slice = _onsets[from..to];
            long[] lows = Lows(slice, local);
            long[] beats;
            long? jumped;
            if (previous is null)
            {
                beats = First(local, lows, out jumped);
            }
            else
            {
                long step = local.Length >= MinOnsets ? NearestStep(local, previous.Beat / PerBeat) : previous.Beat / PerBeat;
                long period = Math.Max(1, PerBeat * step);
                beats = BassBeat(lows) is { } bass
                    ? Divided(TrackBass(local, lows, bass, GridSearch.JumpRun, out jumped), Within(bass, period))
                    : BarGrid.Track(local, period, GridSearch.JumpRun, out jumped);
            }

            int end = jumped is { } at ? from + Array.IndexOf(local, at) : to;
            return new LiveSection(from, end, [.. beats.Select(beat => beat + Times[from])]);
        }

        /// <summary>How many beats near <paramref name="period"/> a beat of the bass holds: 1, 2, 4 or 8, the beat nearest the period.</summary>
        private static int Within(long bass, long period) => new[] { 1, 2, 4, 8 }
            .MinBy(count => Math.Max(bass / count, period) * 1000 / Math.Max(1, Math.Min(bass / count, period)));

        /// <summary>The bass's beats, each divided evenly into <paramref name="count"/>, so the section keeps the 16ths of the one before.</summary>
        private static long[] Divided(long[] bass, int count)
        {
            var beats = new List<long>();
            for (int i = 0; i + 1 < bass.Length; i++)
            {
                for (int k = 0; k < count; k++)
                {
                    beats.Add(bass[i] + ((bass[i + 1] - bass[i]) * k / count));
                }
            }

            beats.Add(bass[^1]);
            return [.. beats];
        }

        /// <summary>The first section's beats, found as <see cref="Find"/> finds them, the tempo within the hint's window when there is one.</summary>
        private long[] First(long[] local, long[] lows, out long? jumped)
        {
            if (BassBeat(lows) is { } period)
            {
                long[] bass = TrackBass(local, lows, period, GridSearch.JumpRun, out jumped);
                PerBeat = _steps ?? Divisions(Tick.Zero, local, bass, period);
                FirstHasBass = true;
                return bass;
            }

            (long shortest, long longest) = Window(_hint?.Sixteenth);
            (long step, _) = Search(local, shortest, longest);
            while (_hint?.Sixteenth is null && step > SlowestUsualSixteenth && step / 2 >= ShortestSearched)
            {
                step /= 2;
            }

            PerBeat = _steps ?? 2;
            return BarGrid.Track(local, PerBeat * step, GridSearch.JumpRun, out jumped);
        }

        /// <summary>The 16ths searched: within 0.6 to 1.6 of a hinted 16th, else all.</summary>
        private static (long Shortest, long Longest) Window(long? hinted) => hinted is { } sixteenth
            ? (Math.Clamp(sixteenth * 3 / 5, ShortestSearched, LongestSearched), Math.Clamp(sixteenth * 8 / 5, ShortestSearched, LongestSearched))
            : (ShortestSearched, LongestSearched);

        /// <summary>
        /// The section's 16th: of the one found, half of it and twice it, the nearest to the section before's among those
        /// that leave the opening's onsets, on average, no more than a 16th of a step further from the grid than the finest
        /// does, so that a pause inside a piece does not halve or double its tempo.
        /// </summary>
        private static long NearestStep(long[] local, long before)
        {
            (long found, _) = Search(local);
            long[] opening = Opening(local, 32_000_000);
            var costs = new List<(long Step, long Cost)>();
            foreach (long step in new[] { found / 2, found, found * 2 })
            {
                if (step is >= ShortestSixteenth and <= LongestSixteenth)
                {
                    (long Step, long Phase, long Cost) best = (0, 0, 0);
                    Consider(opening, step, 16, ref best);
                    costs.Add((step, best.Cost));
                }
            }

            long least = costs.Min(cost => cost.Cost);
            return costs
                .Where(cost => 16 * (cost.Cost - least) <= opening.Length * cost.Step)
                .OrderBy(cost => Math.Max(cost.Step, before) * 1000 / Math.Max(1, Math.Min(cost.Step, before)))
                .ThenBy(cost => cost.Step)
                .First().Step;
        }
    }

    /// <summary>The bars of each section of a live performance, laid out from its own first downbeat.</summary>
    private sealed class SectionBars(List<LiveSection> sections, int[] starts, int[] steps, int[] lowest)
    {
        private static readonly int[] s_sizes = [16, 6, 12, 8];

        /// <summary>The signature the sections support best together, or the one asked; the hint's when it does three quarters as well.</summary>
        public (TimeSignature Signature, bool? HintFits) Pick(TimeSignature? asked, TimeSignature? hint)
        {
            if (asked is { } wanted)
            {
                return (wanted, null);
            }

            long[] scores = [.. s_sizes.Select(Score)];
            long best = scores.Max();
            int index = best <= 0 ? 0 : Array.FindIndex(scores, score => 10 * score >= 9 * best);
            TimeSignature picked = Named(s_sizes[index]);
            if (hint is not { } hinted || !Signatures.Contains(hinted))
            {
                return (picked, null);
            }

            int size = (int)(hinted.BarTicks / StepTicks);
            int at = Array.IndexOf(s_sizes, size);
            bool fits = at >= 0 && 4 * scores[at] >= 3 * best;
            return fits ? (hinted, true) : (picked, false);
        }

        /// <summary>In 16ths from the origin: each section's bar starts after its own first beat, in order.</summary>
        public int[] Starts(TimeSignature signature)
        {
            int size = (int)(signature.BarTicks / StepTicks);
            var all = new List<int>();
            for (int k = 0; k < sections.Count; k++)
            {
                foreach (int start in Of(k, size).Starts.Select(start => starts[k] + start))
                {
                    if (all.Count == 0 || start > all[^1])
                    {
                        all.Add(start);
                    }
                }
            }

            return [.. all];
        }

        private Layout Of(int k, int size)
        {
            LiveSection section = sections[k];
            int[] local = [.. steps[section.From..section.To].Select(step => Math.Max(0, step - starts[k]))];
            return Layout.Of(local, lowest[section.From..section.To], size);
        }

        /// <summary>How well the bass falls on the bar lines in every section, each weighed by its onsets.</summary>
        private long Score(int size) => Enumerable.Range(0, sections.Count).Sum(k => Of(k, size).Score * (sections[k].To - sections[k].From));

        private TimeSignature Named(int size) => size switch
        {
            6 => new TimeSignature(3, 8),
            8 => new TimeSignature(2, 4),
            12 => Compound() ? new TimeSignature(6, 8) : new TimeSignature(3, 4),
            _ => TimeSignature.CommonTime,
        };

        /// <summary>Twelve 16ths: 6/8 when the sections that read so hold more onsets than the others.</summary>
        private bool Compound()
        {
            long six = 0, three = 0;
            for (int k = 0; k < sections.Count; k++)
            {
                long weight = sections[k].To - sections[k].From;
                if (Of(k, 12).CompoundTwelve)
                {
                    six += weight;
                }
                else
                {
                    three += weight;
                }
            }

            return six > three;
        }
    }
}