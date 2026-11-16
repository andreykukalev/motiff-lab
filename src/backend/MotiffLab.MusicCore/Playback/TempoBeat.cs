using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Playback;

/// <summary>
/// The note a tempo is counted in (version 0.5.4, #511 section 17): a quarter note, or a dotted quarter in 6/8, 9/8,
/// 12/8 and the other compound meters of eighths (Andrei, 2026-11-14), as a metronome mark is printed there: "♩. = 60"
/// is "♩ = 90". The engine, the project file and the facade's "perMinute" keep counting quarter notes; only what the
/// player reads and types is in this beat.
/// </summary>
public enum TempoBeat
{
    /// <summary>A quarter note.</summary>
    Quarter,

    /// <summary>A dotted quarter, three eighths.</summary>
    DottedQuarter,
}

/// <summary>The beat of a meter, and tempos counted in it.</summary>
public static class TempoBeats
{
    /// <summary>The dotted quarter for a compound meter of eighths (6/8, 9/8, 12/8 and on), the quarter for every other.</summary>
    public static TempoBeat Of(TimeSignature signature) =>
        signature.Denominator == 8 && signature.Numerator > 3 && signature.Numerator % 3 == 0 ? TempoBeat.DottedQuarter : TempoBeat.Quarter;

    /// <summary>The beat of the meter in force at <paramref name="at"/>.</summary>
    public static TempoBeat At(MeterMap meter, Tick at)
    {
        ArgumentNullException.ThrowIfNull(meter);
        return Of(meter.SignatureAt(at));
    }

    /// <summary>"quarter" or "dottedQuarter", as the facade answers it.</summary>
    public static string Name(this TempoBeat beat) => beat == TempoBeat.DottedQuarter ? "dottedQuarter" : "quarter";

    /// <summary>Beats a minute, rounded to the nearest whole one, of a tempo of <paramref name="microsecondsPerQuarter"/>.</summary>
    public static int PerMinute(this TempoBeat beat, int microsecondsPerQuarter)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(microsecondsPerQuarter);
        // A dotted quarter lasts three halves of a quarter: 40 000 000 / µs of dotted quarters for 60 000 000 / µs of quarters.
        long minute = beat == TempoBeat.DottedQuarter ? 40_000_000L : 60_000_000L;
        return (int)((minute + (microsecondsPerQuarter / 2)) / microsecondsPerQuarter);
    }

    /// <summary>
    /// Quarter notes a minute of <paramref name="perMinute"/> beats, rounded half up: a dotted quarter's 61 is 92 quarters,
    /// which reads back as 61 (every whole number of dotted quarters reads back as itself).
    /// </summary>
    public static int QuarterNotes(this TempoBeat beat, int perMinute) =>
        beat == TempoBeat.DottedQuarter ? ((3 * perMinute) + 1) / 2 : perMinute;
}