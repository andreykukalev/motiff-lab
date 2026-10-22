namespace MotiffLab.MusicCore;

/// <summary>
/// Musical time resolution shared by every input format (docs/05-domain-architecture.md, "Time").
/// MIDI files and MusicXML divisions use their own resolution; parsers rescale to this one.
/// </summary>
public static class TickResolution
{
    /// <summary>Ticks per quarter note in the shared event model.</summary>
    public const int TicksPerQuarter = 480;

    /// <summary>
    /// Rescales a tick value from a source resolution (MIDI PPQ or MusicXML divisions) to
    /// <see cref="TicksPerQuarter"/>, rounding half away from zero.
    /// </summary>
    public static long FromSource(long sourceTicks, int sourceTicksPerQuarter)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceTicksPerQuarter);

        return (long)Math.Round(
            (decimal)sourceTicks * TicksPerQuarter / sourceTicksPerQuarter,
            MidpointRounding.AwayFromZero);
    }
}