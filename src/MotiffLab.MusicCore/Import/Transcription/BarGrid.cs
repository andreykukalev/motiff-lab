using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Transcription;

/// <summary>
/// Bars and a tempo worked out from the onsets of a transcription (decision record 0016). The
/// engine writes what it hears at 120 BPM in 4/4, so its bar lines say nothing about the music; this
/// is a beat grid fitted to the playing, following its tempo, with bar lines where the bass falls.
/// </summary>
/// <remarks>
/// <para>
/// A grid counts from <see cref="Origin"/>, the engine's first note, in beats of
/// <see cref="StepsPerBeat"/> 16ths. <see cref="Beats"/> says when each beat falls in the recording,
/// so the tempo follows the playing; after the last one, beats of <see cref="StepsPerBeat"/> times
/// <see cref="SixteenthMicroseconds"/> continue. Bars of <see cref="Signature"/> are laid end to end
/// from <see cref="BarStarts"/>: the first is the first downbeat (a pickup before it when it is not
/// 0), and each later one starts the bars afresh after a short bar, where a repeat or a held beat
/// moved the downbeat.
/// </para>
/// <para>
/// <see cref="Apply"/> re-expresses the same notes against the grid: it moves no note in time, keeps
/// every id, and plays back as the recording was heard, with a tempo change on every beat. The grid is
/// an inference shown as one, and the project keeps the engine's MIDI as its source, so the player
/// can change or drop the grid. Everything is integer arithmetic and deterministic: the same notes
/// give the same grid.
/// </para>
/// </remarks>
public sealed record BarGrid
{
    /// <summary>One grid step in canonical ticks: a 16th note.</summary>
    public const long StepTicks = CanonicalTime.TicksPerQuarter / 4;

    /// <summary>The fewest separate onsets a grid is fitted to.</summary>
    public const int MinOnsets = 16;

    /// <summary>The shortest 16th the search tries: 80 ms, a quarter at 187.5 BPM.</summary>
    public const int ShortestSearched = 80_000;

    /// <summary>The longest 16th the search tries: 500 ms, a quarter at 30 BPM.</summary>
    public const int LongestSearched = 500_000;

    /// <summary>The shortest and longest 16th a grid may have, so that halving and doubling stay in reach.</summary>
    public const int ShortestSixteenth = 10_000;

    public const int LongestSixteenth = 2_000_000;

    /// <summary>The fewest and most 16ths a tracked beat is written as: a 16th to a whole note.</summary>
    public const int FewestStepsPerBeat = 1;

    public const int MostStepsPerBeat = 16;

    /// <summary>A 16th at a quarter of 60 a minute: the search takes a longer step for an eighth.</summary>
    private const int SlowestUsualSixteenth = 250_000;

    /// <summary>The shortest and longest beat the bass can mark: 300 ms and 2 s.</summary>
    private const long ShortestBassBeat = 300_000;

    private const long LongestBassBeat = 2_000_000;

    /// <summary>The longest 16th a bass beat is divided into: a quarter at 50 a minute.</summary>
    private const long SlowestBassSixteenth = 300_000;

    /// <summary>Onsets this close together are one chord: 30 ms.</summary>
    private const long ChordMicroseconds = 30_000;

    private const int MiddleC = 60;

    /// <summary>The bars a player can pick, shortest bar first within each number of steps.</summary>
    public static IReadOnlyList<TimeSignature> Signatures { get; } =
        [new(2, 4), new(3, 4), new(4, 4), new(3, 8), new(6, 8)];

    /// <param name="beats">When each beat falls, in microseconds after the origin, from 0; empty for a steady beat throughout.</param>
    /// <param name="stepsPerBeat">How many 16ths a beat is written as, from 1 to 16.</param>
    public BarGrid(Tick origin, int sixteenthMicroseconds, TimeSignature signature, IReadOnlyList<int> barStarts, IReadOnlyList<long>? beats = null, int stepsPerBeat = 2)
    {
        ArgumentNullException.ThrowIfNull(barStarts);
        ArgumentOutOfRangeException.ThrowIfLessThan(sixteenthMicroseconds, ShortestSixteenth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sixteenthMicroseconds, LongestSixteenth);
        if (stepsPerBeat is < FewestStepsPerBeat or > MostStepsPerBeat)
        {
            throw new ArgumentOutOfRangeException(nameof(stepsPerBeat), stepsPerBeat, "A beat is written as 1 to 16 16ths.");
        }

        if (signature.BarTicks % StepTicks != 0)
        {
            throw new ArgumentException("A bar must hold a whole number of 16ths.", nameof(signature));
        }

        int[] starts = [.. barStarts];
        if (starts.Length == 0 || starts[0] < 0 || starts[0] >= signature.BarTicks / StepTicks)
        {
            throw new ArgumentException("The first downbeat must fall within the first bar.", nameof(barStarts));
        }

        for (int i = 1; i < starts.Length; i++)
        {
            if (starts[i] <= starts[i - 1])
            {
                throw new ArgumentException("Bar starts must be in strictly increasing order.", nameof(barStarts));
            }
        }

        long[] times = [.. beats ?? []];
        if (times.Length > 0 && times[0] != 0)
        {
            throw new ArgumentException("The first beat is at the origin.", nameof(beats));
        }

        for (int i = 1; i < times.Length; i++)
        {
            if (times[i] <= times[i - 1])
            {
                throw new ArgumentException("Beats must be in strictly increasing order.", nameof(beats));
            }
        }

        Origin = origin;
        SixteenthMicroseconds = sixteenthMicroseconds;
        Signature = signature;
        BarStarts = starts;
        Beats = times;
        StepsPerBeat = stepsPerBeat;
    }

    /// <summary>Where the grid starts in the engine's MIDI: at its first note.</summary>
    public Tick Origin { get; }

    /// <summary>How long a 16th lasts in the recording: the typical one when the tempo follows the playing.</summary>
    public int SixteenthMicroseconds { get; }

    public TimeSignature Signature { get; }

    /// <summary>In 16ths from <see cref="Origin"/>: the first downbeat, then where the bars start afresh after a short bar.</summary>
    public IReadOnlyList<int> BarStarts { get; }

    /// <summary>When each beat falls, in microseconds after <see cref="Origin"/>; empty for a steady beat.</summary>
    public IReadOnlyList<long> Beats { get; }

    /// <summary>How many 16ths a beat is written as.</summary>
    public int StepsPerBeat { get; }

    /// <summary>True when the beats follow the playing rather than one tempo.</summary>
    public bool FollowsPlaying => Beats.Count > 1;

    public int StepsPerBar => (int)(Signature.BarTicks / StepTicks);

    public int MicrosecondsPerQuarter => 4 * SixteenthMicroseconds;

    /// <summary>Quarter notes a minute, rounded to the nearest whole one for display.</summary>
    public int QuarterNotesPerMinute => (int)((60_000_000L + (MicrosecondsPerQuarter / 2)) / MicrosecondsPerQuarter);

    public bool HasPickup => BarStarts[0] > 0;

    /// <summary>Bars cut short where the downbeat moved.</summary>
    public int ShortBarCount => BarStarts.Count - 1;

    private long BeatMicroseconds => (long)SixteenthMicroseconds * StepsPerBeat;

    public bool Equals(BarGrid? other) =>
        other is not null
        && Origin == other.Origin
        && SixteenthMicroseconds == other.SixteenthMicroseconds
        && Signature == other.Signature
        && StepsPerBeat == other.StepsPerBeat
        && BarStarts.SequenceEqual(other.BarStarts)
        && Beats.SequenceEqual(other.Beats);

    public override int GetHashCode() => HashCode.Combine(Origin, SixteenthMicroseconds, Signature, StepsPerBeat, BarStarts.Count, Beats.Count);

    /// <summary>
    /// The same grid with every bar line moved by whole beats of its signature, later for a positive
    /// number and earlier for a negative one: for a downbeat the bass misplaced.
    /// </summary>
    public BarGrid Shifted(int beats)
    {
        int by = beats * 16 / Signature.Denominator;
        int first = (((BarStarts[0] + by) % StepsPerBar) + StepsPerBar) % StepsPerBar;
        int[] later = [.. BarStarts.Skip(1).Select(start => start + by).Where(start => start > first)];
        return new BarGrid(Origin, SixteenthMicroseconds, Signature, [first, .. later], Beats, StepsPerBeat);
    }

    /// <summary>When beat <paramref name="index"/> falls, in microseconds after the origin.</summary>
    public long BeatAt(long index)
    {
        if (index < Beats.Count)
        {
            return Beats[(int)index];
        }

        return Beats.Count == 0 ? index * BeatMicroseconds : Beats[^1] + ((index - Beats.Count + 1) * BeatMicroseconds);
    }

    /// <summary>The beat a time falls in: the last beat at or before it.</summary>
    private long BeatIndexAt(long microseconds)
    {
        if (Beats.Count > 1 && microseconds < Beats[^1])
        {
            int found = ((List<long>)[.. Beats]).BinarySearch(microseconds);
            return found >= 0 ? found : ~found - 1;
        }

        long last = Beats.Count == 0 ? 0 : Beats[^1];
        return Math.Max(0, Beats.Count - 1) + ((microseconds - last) / BeatMicroseconds);
    }

    /// <summary>
    /// The grid that fits the transcription's onsets best, or null when there are too few onsets or
    /// they follow no beat, even one that bends with the playing.
    /// </summary>
    /// <param name="signature">The bars to lay out; null picks the one the bass supports best.</param>
    /// <param name="stepsPerBeat">How many 16ths a beat is written as, for example twice or half as many as were found; null takes the ones found.</param>
    public static BarGrid? Find(MidiImport import, TimeSignature? signature = null, int? stepsPerBeat = null)
    {
        ArgumentNullException.ThrowIfNull(import);
        if (signature is { } chosen && !Signatures.Contains(chosen))
        {
            throw new ArgumentException($"Bars of {chosen} are not offered.", nameof(signature));
        }

        if (stepsPerBeat is < FewestStepsPerBeat or > MostStepsPerBeat)
        {
            throw new ArgumentOutOfRangeException(nameof(stepsPerBeat), stepsPerBeat, "A beat is written as 1 to 16 16ths.");
        }

        var clock = new Clock(import.Tempos);
        Onset[] onsets = Cluster(import.Notes, clock);
        if (onsets.Length < MinOnsets)
        {
            return null;
        }

        Tick origin = onsets[0].Tick;
        long originUnits = onsets[0].Units;
        long[] times = [.. onsets.Select(onset => (onset.Units - originUnits) / Clock.UnitsPerMicrosecond)];
        long[] lows = Lows(onsets, times);
        long[] beats;
        int found;
        long? bassBeat = BassBeat(lows);
        if (bassBeat is { } period)
        {
            // The bass keeps a steady beat: follow it, and write it in 16ths, or in triplets of
            // them when those fit far better.
            beats = TrackBass(times, lows, period);
            found = Divisions(origin, times, beats, period);
        }
        else
        {
            // The opening gives the 16th; the beat, an eighth, then follows the playing.
            (long step, _) = Search(times);
            // The shortest notes found are taken as 16ths. When they are eighths, that reads as a
            // quarter slower than 60 a minute; such slow tempos are rare, so halve the step.
            while (step > SlowestUsualSixteenth && step / 2 >= ShortestSearched)
            {
                step /= 2;
            }

            beats = Track(times, 2 * step);
            found = 2;
        }

        int perBeat = stepsPerBeat ?? found;
        long[] sorted = [.. beats.Zip(beats.Skip(1), (a, b) => b - a).Order()];
        long sixteenth = Math.Clamp(sorted[sorted.Length / 2] / perBeat, ShortestSixteenth, LongestSixteenth);
        var grid = new BarGrid(origin, (int)sixteenth, TimeSignature.CommonTime, [0], beats, perBeat);

        // Onsets that ignore the beat sit a quarter of a step from it on average, as at random, and a
        // beat that bends to follow them still leaves them an eighth of a step away. A bass that
        // keeps the beat is evidence enough, however freely the hands around it play.
        long[] positions = [.. times.Select(grid.ThousandthsOfSteps)];
        if (bassBeat is null && stepsPerBeat is null && 8 * Distance(positions) > positions.Length * 1000L)
        {
            return null;
        }

        int[] steps = [.. positions.Select(position => (int)((position + 500) / 1000))];
        int[] lowest = [.. onsets.Select(onset => onset.Lowest)];
        TimeSignature picked;
        Layout layout;
        if (signature is { } wanted)
        {
            picked = wanted;
            layout = Layout.Of(steps, lowest, (int)(wanted.BarTicks / StepTicks));
        }
        else
        {
            (picked, layout) = Pick(steps, lowest);
        }

        return new BarGrid(origin, (int)sixteenth, picked, layout.Starts, beats, perBeat);
    }

    /// <summary>The summed distance of positions, in thousandths of a 16th, to the nearest 16th.</summary>
    private static long Distance(long[] positions) => positions.Sum(position => Math.Min(position % 1000, 1000 - (position % 1000)));

    /// <summary>
    /// When the onsets that are the lowest of their neighbours and below middle C fall: where the bass
    /// plays, in microseconds after the first onset.
    /// </summary>
    private static long[] Lows(Onset[] onsets, long[] times)
    {
        var lows = new List<long>();
        for (int i = 0; i < onsets.Length; i++)
        {
            int from = Math.Max(0, i - 3), to = Math.Min(onsets.Length, i + 4);
            if (onsets[i].Lowest < MiddleC && onsets[from..to].All(other => other.Lowest >= onsets[i].Lowest))
            {
                lows.Add(times[i]);
            }
        }

        return [.. lows];
    }

    /// <summary>
    /// The beat the bass keeps, or null when it keeps none: the typical time between bass notes, when
    /// most of those times are about that beat, half of it or twice it.
    /// </summary>
    private static long? BassBeat(long[] lows)
    {
        if (lows.Length < MinOnsets)
        {
            return null;
        }

        long[] gaps = [.. lows.Zip(lows.Skip(1), (a, b) => b - a)];
        long[] usable = [.. gaps.Where(gap => gap is >= ShortestBassBeat and <= LongestBassBeat).Order()];
        if (usable.Length < MinOnsets)
        {
            return null;
        }

        long beat = usable[usable.Length / 2];
        static bool Near(long gap, long length) => 5 * Math.Abs(gap - length) < length;
        int steady = gaps.Count(gap => Near(gap, beat) || Near(gap, beat / 2) || Near(gap, 2 * beat));
        return 10 * steady >= 6 * gaps.Length ? beat : null;
    }

    /// <summary>
    /// Beats of the bass, each moved to the bass note nearest where it is expected, or failing that to
    /// any onset close to it, with the expected length following the last few beats. The beats before
    /// the first bass note count back from it, so that the first beat is at the first onset.
    /// </summary>
    private static long[] TrackBass(long[] times, long[] lows, long period)
    {
        var beats = new List<long> { 0 };
        long first = lows[0];
        for (long back = DivideRounded(first, period) - 1; back >= 1; back--)
        {
            if (first - (back * period) >= beats[^1] + (period / 2))
            {
                beats.Add(first - (back * period));
            }
        }

        if (first > beats[^1])
        {
            beats.Add(first);
        }

        long beat = period, at = beats[^1], last = times[^1];
        long shortest = period * 3 / 5, longest = period * 8 / 5;
        while (at < last)
        {
            long expected = at + beat;
            long next = Nearest(lows, expected, beat / 4, at + (beat * 3 / 10))
                ?? Nearest(times, expected, beat / 8, at)
                ?? expected;
            beat = Math.Clamp(((3 * beat) + (next - at)) / 4, shortest, longest);
            beats.Add(next);
            at = next;
        }

        return [.. beats];
    }

    /// <summary>
    /// How many 16ths a bass beat is written as: the fewest of 2, 4, 8 or 16 that leave a 16th no
    /// longer than <see cref="SlowestBassSixteenth"/>, or half as many again when triplets of them
    /// leave the onsets far closer to the grid.
    /// </summary>
    private static int Divisions(Tick origin, long[] times, long[] beats, long period)
    {
        int straight = 2;
        while (straight < 16 && period / straight > SlowestBassSixteenth)
        {
            straight *= 2;
        }

        int triple = straight * 3 / 2;
        long Cost(int steps)
        {
            var grid = new BarGrid(origin, ShortestSixteenth, TimeSignature.CommonTime, [0], beats, steps);
            return Distance([.. times.Select(grid.ThousandthsOfSteps)]);
        }

        return triple <= MostStepsPerBeat && 10 * Cost(triple) < 6 * Cost(straight) ? triple : straight;
    }

    /// <summary>The time nearest to <paramref name="expected"/>, closer than <paramref name="reach"/> and later than <paramref name="after"/>.</summary>
    private static long? Nearest(long[] times, long expected, long reach, long after)
    {
        int index = Array.BinarySearch(times, expected);
        index = index >= 0 ? index : ~index;
        long? found = null;
        long nearest = reach;
        foreach (int candidate in new[] { index - 1, index })
        {
            if (candidate >= 0 && candidate < times.Length && times[candidate] > after && Math.Abs(times[candidate] - expected) < nearest)
            {
                nearest = Math.Abs(times[candidate] - expected);
                found = times[candidate];
            }
        }

        return found;
    }

    /// <summary>
    /// Beats an eighth apart at first, each moved to the onset nearest where it is expected, within a
    /// quarter of a beat, with the expected length following the last few beats: a tempo that bends
    /// with the playing. Where nothing is played, the beat goes on at the tempo it had.
    /// </summary>
    private static long[] Track(long[] times, long period)
    {
        var beats = new List<long> { 0 };
        long beat = period, at = 0, last = times[^1];
        long shortest = period * 3 / 5, longest = period * 8 / 5;
        while (at < last)
        {
            long expected = at + beat;
            long next = Nearest(times, expected, beat / 4, at) ?? expected;
            beat = Math.Clamp(((3 * beat) + (next - at)) / 4, shortest, longest);
            beats.Add(next);
            at = next;
        }

        return [.. beats];
    }

    /// <summary>Where a time falls on the grid, in thousandths of a 16th, rounded down.</summary>
    private long ThousandthsOfSteps(long microseconds)
    {
        long index = BeatIndexAt(microseconds);
        long from = BeatAt(index), to = BeatAt(index + 1);
        return StepsPerBeat * ((index * 1000) + ((microseconds - from) * 1000 / (to - from)));
    }

    /// <summary>
    /// The piece with its notes, bars, tempo and keys on this grid: the same notes with the same ids,
    /// placed against the beat that was found rather than the engine's 120 BPM.
    /// </summary>
    /// <exception cref="ArgumentException">The piece was not imported from MIDI, which is what transcription writes.</exception>
    public ImportedPiece Apply(ImportedPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        MidiImport midi = piece.Midi ?? throw new ArgumentException("A transcription is read as MIDI.", nameof(piece));

        var clock = new Clock(midi.Tempos);
        long originUnits = clock.Units(Origin);
        QuarterFraction At(Tick tick)
        {
            long units = clock.Units(tick) - originUnits;
            if (units <= 0)
            {
                return QuarterFraction.Zero;
            }

            long index = BeatIndexAt(units / Clock.UnitsPerMicrosecond);
            long from = BeatAt(index) * Clock.UnitsPerMicrosecond;
            long length = (BeatAt(index + 1) * Clock.UnitsPerMicrosecond) - from;
            return new QuarterFraction(StepsPerBeat * ((index * length) + (units - from)), 4 * length);
        }

        Tick Moved(Tick tick) => CanonicalTime.ToTick(At(tick)).Tick;
        Note Placed(Note note)
        {
            QuarterFraction onset = At(note.Span.Onset);
            QuarterFraction end = At(note.Span.End);
            TickSpan span = end > onset ? CanonicalTime.ToSpan(onset, end).Span : new TickSpan(CanonicalTime.ToTick(onset).Tick, 1);
            return note with { Span = span };
        }

        Note[] notes = [.. piece.Notes.Select(Placed)];
        Note[] midiNotes = [.. midi.Notes.Select(Placed)];
        Tick end = Moved(midi.End);
        foreach (Note note in notes)
        {
            end = note.Span.End > end ? note.Span.End : end;
        }

        MeterChange[] changes = [new MeterChange(Tick.Zero, Signature), .. BarStarts.Skip(1).Select(start => new MeterChange(new Tick(start * StepTicks), Signature))];
        var meter = new MeterMap(changes, BarStarts[0] * StepTicks);

        // A tempo change on every beat, so the notes sound when they were played.
        var tempos = new List<MidiTempoChange>();
        long beatTicks = StepsPerBeat * StepTicks;
        for (long index = 0; index * beatTicks <= end.Value; index++)
        {
            int perQuarter = (int)Math.Clamp((BeatAt(index + 1) - BeatAt(index)) * 4 / StepsPerBeat, 1, int.MaxValue);
            if (tempos.Count == 0 || tempos[^1].MicrosecondsPerQuarter != perQuarter)
            {
                tempos.Add(new MidiTempoChange(new Tick(index * beatTicks), index * beatTicks, perQuarter, IsAssumed: true));
            }

            if (!FollowsPlaying)
            {
                break;
            }
        }

        MidiKeySignature[] keys = [.. midi.KeySignatures.Select(key => key with { At = Moved(key.At) })];
        var import = midi with
        {
            Notes = midiNotes,
            Tempos = tempos,
            TimeSignatures = [.. changes.Select(change => new MidiTimeSignature(change.Position, change.Position.Value, Signature.Numerator, Signature.Denominator, IsAssumed: true))],
            Meter = meter,
            KeySignatures = keys,
            End = end,
        };

        // The engine's 120 BPM and 4/4 are no longer what the bars say, so their notices go.
        ImportIssue[] issues =
        [
            .. piece.Report.Issues.Where(issue => issue.Code is not (MidiIssueCodes.TempoDefault or MidiIssueCodes.MeterDefault)),
            new ImportIssue(ImportIssueKind.Assumed, IssueCode, null, 1, Describe()),
        ];
        return piece with
        {
            Notes = notes,
            Meter = meter,
            Keys = [.. piece.Keys.Select(key => key with { At = Moved(key.At) })],
            End = end,
            Report = piece.Report with { MeasureCount = meter.BarsBefore(end).Count(), Issues = issues },
            Midi = import,
            Bars = this,
        };
    }

    /// <summary>The report code of the notice <see cref="Apply"/> adds.</summary>
    public const string IssueCode = "transcription.bars-inferred";

    /// <summary>What was worked out, for the import report.</summary>
    public string Describe()
    {
        string tempo = FollowsPlaying ? $"the tempo, which follows the playing (a quarter at about {QuarterNotesPerMinute} a minute)" : $"the tempo (a quarter at {QuarterNotesPerMinute} a minute)";
        string text = $"The bars ({Signature}) and {tempo} were worked out from the notes, with the bar lines where the bass falls";
        text += HasPickup ? $", after a pickup of {BarStarts[0]} 16ths" : "";
        text += ShortBarCount switch
        {
            0 => ".",
            1 => ". One bar is cut short where the downbeat moves.",
            int count => $". {count} bars are cut short where the downbeat moves.",
        };
        return text;
    }

    /// <summary>The bars the bass supports best: the shortest bar that does nearly as well as the best.</summary>
    private static (TimeSignature Signature, Layout Layout) Pick(int[] steps, int[] lowest)
    {
        // By 16ths in a bar, in the order that breaks a near tie: when the bass does as well on
        // every half bar as on the bar line, the shorter bar says as much, except that 4/4 is far
        // more common than 2/4.
        int[] sizes = [16, 6, 12, 8];
        Layout[] layouts = [.. sizes.Select(size => Layout.Of(steps, lowest, size))];
        long[] scores = [.. layouts.Select(layout => layout.Score)];
        long best = scores.Max();
        int index = best <= 0 ? 0 : Array.FindIndex(scores, score => 10 * score >= 9 * best);
        Layout chosen = layouts[index];
        TimeSignature signature = sizes[index] switch
        {
            6 => new TimeSignature(3, 8),
            8 => new TimeSignature(2, 4),
            12 => chosen.CompoundTwelve ? new TimeSignature(6, 8) : new TimeSignature(3, 4),
            _ => TimeSignature.CommonTime,
        };
        return (signature, chosen);
    }

    /// <summary>The 16th and phase in microseconds that leave the onsets closest to the grid, relative to its step.</summary>
    private static (long Step, long Phase) Search(long[] times)
    {
        // A step that is slightly off drifts a whole step away within a minute or two, so the search
        // looks at the start of the piece: a coarse pass over the first 16 seconds, a fine pass over
        // the first 32, then a straight-line fit over ever longer stretches up to the whole piece.
        (long step, long phase, long cost) best = (0, 0, 0);
        long[] start = Opening(times, 16_000_000);
        for (long step = ShortestSearched; step <= LongestSearched; step += 500)
        {
            Consider(start, step, 16, ref best);
        }

        long coarse = best.step;
        best = (0, 0, 0);
        start = Opening(times, 32_000_000);
        for (long step = Math.Max(ShortestSearched, coarse - 1_000); step <= Math.Min(LongestSearched, coarse + 1_000); step += 50)
        {
            Consider(start, step, 32, ref best);
        }

        (long Step, long Phase) fit = (best.step, best.phase);
        foreach (long seconds in new long[] { 32, 64, 128, 256 })
        {
            fit = Refine(Opening(times, seconds * 1_000_000), fit.Step, fit.Phase);
        }

        return Refine(times, fit.Step, fit.Phase);
    }

    /// <summary>The onsets within a time of the first, and at least the first 64.</summary>
    private static long[] Opening(long[] times, long microseconds)
    {
        int count = 0;
        while (count < times.Length && (count < 64 || times[count] - times[0] < microseconds))
        {
            count++;
        }

        return times[..count];
    }

    private static void Consider(long[] times, long step, int phases, ref (long Step, long Phase, long Cost) best)
    {
        for (int k = 0; k < phases; k++)
        {
            long phase = step * k / phases;
            long cost = Distance(times, step, phase);
            // Relative to the step: cost / step < best.Cost / best.Step.
            if (best.Step == 0 || (Int128)cost * best.Step < (Int128)best.Cost * step)
            {
                best = (step, phase, cost);
            }
        }
    }

    /// <summary>A least-squares line through the onsets near the grid: the step and phase that fit them best.</summary>
    private static (long Step, long Phase) Refine(long[] times, long step, long phase)
    {
        Int128 n = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (long time in times)
        {
            long index = DivideRounded(time - phase, step);
            if (4 * Math.Abs(time - phase - (index * step)) >= step)
            {
                continue;
            }

            n++;
            sx += index;
            sy += time;
            sxx += (Int128)index * index;
            sxy += (Int128)index * time;
        }

        Int128 denominator = (n * sxx) - (sx * sx);
        if (n < MinOnsets || denominator == 0)
        {
            return (step, phase);
        }

        Int128 numerator = (n * sxy) - (sx * sy);
        long refined = (long)(((2 * numerator) + denominator) / (2 * denominator));
        if (10 * Math.Abs(refined - step) > step || refined < ShortestSixteenth)
        {
            return (step, phase);
        }

        long intercept = (long)(((2 * (sy - (refined * sx))) + n) / (2 * n));
        return (refined, intercept);
    }

    /// <summary>The sum of every onset's distance to the nearest grid point.</summary>
    private static long Distance(long[] times, long step, long phase)
    {
        long sum = 0;
        foreach (long time in times)
        {
            long offset = (time - phase) % step;
            offset = offset < 0 ? offset + step : offset;
            sum += Math.Min(offset, step - offset);
        }

        return sum;
    }

    /// <summary>a / b rounded to the nearest whole number, halves away from zero; b is positive.</summary>
    private static long DivideRounded(long a, long b) => a >= 0 ? ((2 * a) + b) / (2 * b) : -(((-2 * a) + b) / (2 * b));

    /// <summary>The notes' onsets with chords merged, each with its lowest pitch.</summary>
    private static Onset[] Cluster(IReadOnlyList<Note> notes, Clock clock)
    {
        var onsets = new List<Onset>();
        foreach (Note note in notes.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber))
        {
            long units = clock.Units(note.Span.Onset);
            long microseconds = units / Clock.UnitsPerMicrosecond;
            if (onsets.Count > 0 && microseconds - onsets[^1].Microseconds < ChordMicroseconds)
            {
                onsets[^1] = onsets[^1] with { Lowest = Math.Min(onsets[^1].Lowest, note.Pitch.MidiNumber) };
                continue;
            }

            onsets.Add(new Onset(note.Span.Onset, units, microseconds, note.Pitch.MidiNumber));
        }

        return [.. onsets];
    }

    /// <param name="Units">The onset's time in the recording, in <see cref="Clock"/> units.</param>
    private readonly record struct Onset(Tick Tick, long Units, long Microseconds, int Lowest);

    /// <summary>
    /// Time in the recording from canonical ticks and the tempo map, exactly: a tick at a tempo of
    /// m microseconds a quarter lasts m / 480 microseconds, so time is counted in 480ths of a microsecond.
    /// </summary>
    private sealed class Clock
    {
        public const long UnitsPerMicrosecond = CanonicalTime.TicksPerQuarter;

        private readonly (long At, long Units, long PerTick)[] _segments;

        public Clock(IReadOnlyList<MidiTempoChange> tempos)
        {
            var segments = new List<(long, long, long)>();
            long at = 0, units = 0, perTick = MidiTempoChange.DefaultMicrosecondsPerQuarter;
            foreach (MidiTempoChange tempo in tempos.OrderBy(tempo => tempo.At))
            {
                units += (tempo.At.Value - at) * perTick;
                at = tempo.At.Value;
                perTick = tempo.MicrosecondsPerQuarter;
                if (segments.Count > 0 && segments[^1].Item1 == at)
                {
                    segments[^1] = (at, units, perTick);
                    continue;
                }

                segments.Add((at, units, perTick));
            }

            if (segments.Count == 0 || segments[0].Item1 != 0)
            {
                segments.Insert(0, (0, 0, MidiTempoChange.DefaultMicrosecondsPerQuarter));
            }

            _segments = [.. segments];
        }

        public long Units(Tick tick)
        {
            (long at, long units, long perTick) = _segments[SegmentAt(segment => segment.At <= tick.Value)];
            return units + ((tick.Value - at) * perTick);
        }

        /// <summary>The last tick that is not later than a time.</summary>
        public Tick TickAtOrBefore(long units)
        {
            if (units <= 0)
            {
                return Tick.Zero;
            }

            (long at, long start, long perTick) = _segments[SegmentAt(segment => segment.Units <= units)];
            return new Tick(at + ((units - start) / perTick));
        }

        private int SegmentAt(Func<(long At, long Units, long PerTick), bool> starts)
        {
            int found = 0;
            for (int i = 1; i < _segments.Length && starts(_segments[i]); i++)
            {
                found = i;
            }

            return found;
        }
    }

    /// <summary>Bar lines for one bar length, from where the bass falls.</summary>
    private sealed record Layout(int[] Starts, long Score, bool CompoundTwelve)
    {
        /// <summary>How far, in 16ths, a likely downbeat is the lowest onset: a dotted eighth.</summary>
        private const int LowReach = 3;

        public static Layout Of(int[] steps, int[] lowest, int size)
        {
            int[] lows = Lows(steps, lowest);
            int last = steps[^1] + 1;

            // The downbeat's place in the bar, judged over four bars around each bar start.
            int windows = (last / size) + 1;
            var phases = new int?[windows];
            var counts = new int[size];
            for (int w = 0; w < windows; w++)
            {
                Array.Clear(counts);
                long from = ((long)w * size) - (2 * size), to = ((long)w * size) + (2 * size);
                foreach (int low in lows.Where(low => low >= from && low < to))
                {
                    counts[low % size]++;
                }

                int max = counts.Max();
                phases[w] = max > 0 ? Array.IndexOf(counts, max) : null;
            }

            int current = phases.FirstOrDefault(phase => phase is not null) ?? 0;
            var starts = new List<int> { current };
            var lines = new HashSet<int>();
            int position = current;
            while (position < last)
            {
                lines.Add(position);
                int next = (position / size) + 1;
                int? ahead = next + 1 < windows ? phases[next] : null;
                if (ahead is { } moved && moved != current && phases[next + 1] == moved)
                {
                    // The downbeat moves for two windows running: this bar is cut short.
                    position += ((moved - current) % size + size) % size;
                    current = moved;
                    starts.Add(position);
                    continue;
                }

                position += size;
            }

            // How much better than chance the lows land on bar lines, less a cost for each short bar.
            int hits = lows.Count(lines.Contains) - (2 * (starts.Count - 1));
            long score = lows.Length == 0 ? 0 : Math.Max(0, (long)hits * size * 1000 / lows.Length);

            // Twelve 16ths: 6/8 when the bass falls on the half bar more than on the second and third quarters.
            bool compound = false;
            if (size == 12)
            {
                var offsets = new int[size];
                int[] sortedStarts = [.. lines.Order()];
                foreach (int low in lows)
                {
                    int index = Array.BinarySearch(sortedStarts, low);
                    int bar = index >= 0 ? sortedStarts[index] : (~index > 0 ? sortedStarts[~index - 1] : -1);
                    if (bar >= 0 && low - bar < size)
                    {
                        offsets[low - bar]++;
                    }
                }

                compound = 2 * offsets[6] > offsets[4] + offsets[8];
            }

            return new Layout([.. starts], score, compound);
        }

        /// <summary>
        /// Onsets whose lowest note is the lowest within <see cref="LowReach"/> 16ths either way and
        /// below middle C: likely downbeats. Without any below middle C, the lowest within that reach.
        /// The reach is the same for every bar length, so their scores compare.
        /// </summary>
        private static int[] Lows(int[] steps, int[] lowest)
        {
            var lows = new List<int>();
            var fallback = new List<int>();
            for (int i = 0; i < steps.Length; i++)
            {
                bool isLowest = true;
                for (int j = i - 1; j >= 0 && steps[i] - steps[j] <= LowReach && isLowest; j--)
                {
                    isLowest = lowest[j] >= lowest[i];
                }

                for (int j = i + 1; j < steps.Length && steps[j] - steps[i] <= LowReach && isLowest; j++)
                {
                    isLowest = lowest[j] >= lowest[i];
                }

                if (isLowest)
                {
                    (lowest[i] < MiddleC ? lows : fallback).Add(steps[i]);
                }
            }

            return [.. (lows.Count > 0 ? lows : fallback).Distinct()];
        }
    }
}