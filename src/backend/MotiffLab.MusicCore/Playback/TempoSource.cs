namespace MotiffLab.MusicCore.Playback;

/// <summary>
/// Where a tempo comes from (version 0.5.4, #511 section 17): the tempo printed in the notes, one worked out from a
/// recording, the default taken when nothing states one, or the one the player set for the piece. They are not
/// interchangeable: a notated tempo, the player's and the piece's default are written on the score as "♩ = N", an
/// inferred one as "♩ ≈ N", and an assumed one nowhere.
/// </summary>
public enum TempoSource
{
    /// <summary>
    /// Nothing states a tempo, so a file format's default of 120 quarter notes a minute is taken (MusicXML and MIDI alike);
    /// a piece plays at its default (<see cref="Default"/>) instead, except a recording's own clock.
    /// </summary>
    Assumed,

    /// <summary>The file states it: a MusicXML <c>&lt;sound tempo&gt;</c> or metronome mark, a MIDI tempo event.</summary>
    Notated,

    /// <summary>Worked out from the playing: the bars of a recording's transcription (decision record 0016).</summary>
    Inferred,

    /// <summary>The player set it for the piece, such as "♩ = 92" read from the printed notes (project file "tempo").</summary>
    Player,

    /// <summary>
    /// Nothing states the piece's tempo, neither the file, the scan nor a recording, so it plays at the piece's default of
    /// <see cref="PlaybackTempo.PieceDefaultPerMinute"/> quarter notes a minute (Andrei, version 0.5.5, #521 section 12),
    /// which the score marks "♩ = 80" like any piece tempo until the player sets one. The project file does not keep it.
    /// </summary>
    Default,
}

/// <summary>The facade's and the apps' names of the sources.</summary>
public static class TempoSources
{
    /// <summary>"assumed", "notated", "inferred", "player" or "default".</summary>
    public static string Name(this TempoSource source) => source switch
    {
        TempoSource.Notated => "notated",
        TempoSource.Inferred => "inferred",
        TempoSource.Player => "player",
        TempoSource.Default => "default",
        _ => "assumed",
    };

    /// <summary>Quarter notes a minute of a tempo, rounded to the nearest whole one.</summary>
    public static int PerMinute(int microsecondsPerQuarter) =>
        (int)((60_000_000L + (microsecondsPerQuarter / 2)) / microsecondsPerQuarter);

    /// <summary>The microseconds a quarter note lasts at <paramref name="perMinute"/> quarter notes a minute, rounded.</summary>
    public static int MicrosecondsPerQuarter(int perMinute)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perMinute);
        return (int)((60_000_000L + (perMinute / 2)) / perMinute);
    }
}