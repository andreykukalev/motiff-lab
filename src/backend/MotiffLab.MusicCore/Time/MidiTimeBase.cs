namespace MotiffLab.MusicCore.Time;

/// <summary>
/// The timing unit of a Standard MIDI File: ticks per quarter note from the header's division
/// field. It applies to the whole file and every track.
/// </summary>
/// <remarks>
/// MIDI ticks are absolute event times after summing delta times within a track. They are not
/// MusicXML divisions and must not be converted as such.
/// </remarks>
public sealed record MidiTimeBase
{
    /// <summary>The largest PPQ a header can carry (15 bits).</summary>
    public const int MaxTicksPerQuarter = 0x7FFF;

    public MidiTimeBase(int ticksPerQuarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ticksPerQuarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ticksPerQuarter, MaxTicksPerQuarter);
        TicksPerQuarter = ticksPerQuarter;
    }

    public int TicksPerQuarter { get; }

    /// <summary>
    /// Reads the header division field. Metrical time (bit 15 clear) is supported; SMPTE
    /// time code (bit 15 set) is not, and throws <see cref="NotSupportedException"/>.
    /// </summary>
    public static MidiTimeBase FromHeaderDivision(ushort division)
    {
        if ((division & 0x8000) != 0)
        {
            throw new NotSupportedException("MIDI files timed in SMPTE frames are not supported; only ticks per quarter note.");
        }

        return new MidiTimeBase(division);
    }

    /// <summary>Exact position in quarter notes of an absolute MIDI tick.</summary>
    public QuarterFraction ToQuarters(long midiTick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(midiTick);
        return new QuarterFraction(midiTick, TicksPerQuarter);
    }

    public ConvertedTick ToCanonical(long midiTick) => CanonicalTime.ToTick(ToQuarters(midiTick));

    /// <summary>Converts a note's note-on and note-off ticks. Throws unless note-off is after note-on.</summary>
    public ConvertedSpan ToCanonicalSpan(long noteOnTick, long noteOffTick) =>
        CanonicalTime.ToSpan(ToQuarters(noteOnTick), ToQuarters(noteOffTick));
}