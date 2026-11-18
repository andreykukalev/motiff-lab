using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Pieces;

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
/// Applied to the piece (<c>Import.Transcription.RecordingBars</c>, which also finds the grid), it re-expresses the same
/// notes against the grid: it moves no note in time, keeps every id, and plays back as the recording was heard, with a
/// tempo change on every beat. The grid is
/// an inference shown as one, and the project keeps the engine's MIDI as its source, so the player
/// can change or drop the grid. Everything is integer arithmetic and deterministic: the same notes
/// give the same grid.
/// </para>
/// </remarks>
public sealed partial record BarGrid
{
    /// <summary>One grid step in canonical ticks: a 16th note.</summary>
    public const long StepTicks = CanonicalTime.TicksPerQuarter / 4;

    /// <summary>The shortest and longest 16th a grid may have, so that halving and doubling stay in reach.</summary>
    public const int ShortestSixteenth = 10_000;

    public const int LongestSixteenth = 2_000_000;

    /// <summary>The fewest and most 16ths a tracked beat is written as: a 16th to a whole note.</summary>
    public const int FewestStepsPerBeat = 1;

    public const int MostStepsPerBeat = 16;

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

    /// <summary>
    /// Where the bars change signature (version 0.5, «Смена размера в записи»), in order: from each one's 16th on, its bars
    /// are of its signature, which may be one <see cref="Signatures"/> does not offer, such as 7/8; before the first, and
    /// without any, <see cref="Signature"/>. Each starts the bars afresh, so its 16th is one of <see cref="BarStarts"/> after
    /// the first, and a build before 0.5, which reads only <see cref="Signature"/>, lays its bars from the same starts.
    /// </summary>
    /// <exception cref="ArgumentException">A change is not at a bar start after the first, not after the one before, or its bar is not a whole number of 16ths.</exception>
    public IReadOnlyList<GridMeter> Meters
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            for (int i = 0; i < value.Count; i++)
            {
                if (value[i].Signature.BarTicks % StepTicks != 0 || !BarStarts.Skip(1).Contains(value[i].At) || (i > 0 && value[i].At <= value[i - 1].At))
                {
                    throw new ArgumentException("Each change of signature starts the bars at one of the later bar starts, in order, with bars of whole 16ths.", nameof(Meters));
                }
            }

            field = [.. value];
        }
    } = [];

    /// <summary>The signature of the bars from a 16th on: the last change at or before it, else <see cref="Signature"/>.</summary>
    public TimeSignature SignatureAt(int step)
    {
        TimeSignature signature = Signature;
        foreach (GridMeter meter in Meters.TakeWhile(meter => meter.At <= step))
        {
            signature = meter.Signature;
        }

        return signature;
    }

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
        && Beats.SequenceEqual(other.Beats)
        && Meters.SequenceEqual(other.Meters);

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
        return new BarGrid(Origin, SixteenthMicroseconds, Signature, [first, .. later], Beats, StepsPerBeat)
        {
            Meters = [.. Meters.Select(meter => meter with { At = meter.At + by }).Where(meter => meter.At > first)],
        };
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
    internal long BeatIndexAt(long microseconds)
    {
        if (Beats.Count > 1 && microseconds < Beats[^1])
        {
            int found = ((List<long>)[.. Beats]).BinarySearch(microseconds);
            return found >= 0 ? found : ~found - 1;
        }

        long last = Beats.Count == 0 ? 0 : Beats[^1];
        return Math.Max(0, Beats.Count - 1) + ((microseconds - last) / BeatMicroseconds);
    }

    /// <summary>Where a time falls on the grid, in thousandths of a 16th, rounded down.</summary>
    internal long ThousandthsOfSteps(long microseconds)
    {
        long index = BeatIndexAt(microseconds);
        long from = BeatAt(index), to = BeatAt(index + 1);
        return StepsPerBeat * ((index * 1000) + ((microseconds - from) * 1000 / (to - from)));
    }

    /// <summary>The report code of the notice applying the grid to the piece adds.</summary>
    public const string IssueCode = "transcription.bars-inferred";

    /// <summary>What was worked out, for the import report.</summary>
    public string Describe() => Describe(Words.English);

    /// <summary>What was worked out, for the import report, in a language.</summary>
    public string Describe(Words words)
    {
        if (words.IsRussian)
        {
            string темп = FollowsPlaying
                ? $"темп, который следует за игрой (четверть примерно {QuarterNotesPerMinute} в минуту)"
                : $"темп (четверть {QuarterNotesPerMinute} в минуту)";
            string текст = $"Такты ({Signature}) и {темп} выведены из нот, тактовые черты стоят там, где вступает бас";
            текст += HasPickup ? $", после затакта в {words.Count(BarStarts[0], "16th", "16ths", "шестнадцатую", "шестнадцатые", "шестнадцатых")}" : "";
            текст += ShortBarCount switch
            {
                0 => ".",
                int count => $". {words.Count(count, "bar", "bars", "такт", "такта", "тактов")} {Words.RussianForm(count, "укорочен", "укорочены", "укорочены")} там, где сдвигается сильная доля.",
            };
            return текст;
        }

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
}

/// <summary>A change of signature in a grid: from the 16th <paramref name="At"/> (from the origin) on, bars of <paramref name="Signature"/>.</summary>
public readonly record struct GridMeter(int At, TimeSignature Signature);