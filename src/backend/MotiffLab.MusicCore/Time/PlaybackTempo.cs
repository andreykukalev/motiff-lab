namespace MotiffLab.MusicCore.Time;

/// <summary>A tempo change: from <paramref name="At"/> on, a quarter note lasts <paramref name="MicrosecondsPerQuarter"/>.</summary>
public readonly record struct PlaybackTempo
{
    /// <summary>120 BPM, the MIDI default.</summary>
    public const int DefaultMicrosecondsPerQuarter = 500_000;

    /// <summary>The tempo a piece plays at when nothing states one, in quarter notes a minute (<see cref="TempoSource.Default"/>).</summary>
    public const int PieceDefaultPerMinute = 80;

    /// <summary><see cref="PieceDefaultPerMinute"/> as microseconds a quarter note.</summary>
    public const int PieceDefaultMicrosecondsPerQuarter = 750_000;

    /// <param name="source">Where the tempo comes from (version 0.5.4); assumed unless something states it.</param>
    public PlaybackTempo(Tick at, int microsecondsPerQuarter, TempoSource source = TempoSource.Assumed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(microsecondsPerQuarter);
        At = at;
        MicrosecondsPerQuarter = microsecondsPerQuarter;
        Source = source;
    }

    public Tick At { get; init; }

    public int MicrosecondsPerQuarter { get; }

    /// <summary>Where the tempo comes from: the notes, a recording, the default or the player (#511 section 17).</summary>
    public TempoSource Source { get; init; }

    /// <summary>Quarter notes a minute, rounded to the nearest whole one.</summary>
    public int PerMinute => TempoSources.PerMinute(MicrosecondsPerQuarter);
}