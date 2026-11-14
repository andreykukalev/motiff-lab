using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Playback;

/// <summary>
/// The tempo the player set for a piece (version 0.5.4, #511 section 17; project file "tempo"), such as "♩ = 92" read
/// from the printed notes when the scan could not read it: it replaces the tempo at the start, whichever source that
/// has, and is the player's from then on. A tempo the piece states later on (a notated change) stays as stated; the
/// tempos worked out from a recording, or the default, follow it in proportion, so a recording's rubato keeps its shape.
/// </summary>
public static class PlayerTempo
{
    /// <summary>The slowest tempo the player can set, in quarter notes a minute.</summary>
    public const int Min = 20;

    /// <summary>The fastest tempo the player can set, in quarter notes a minute.</summary>
    public const int Max = 300;

    /// <summary>Whether <paramref name="perMinute"/> is a tempo the player can set.</summary>
    public static bool IsValid(int perMinute) => perMinute is >= Min and <= Max;

    /// <summary>The piece's tempo changes with the player's tempo at the start (see the class).</summary>
    /// <param name="piece">The tempo changes as the piece states them; the first at tick 0, empty for 120 throughout.</param>
    /// <param name="perMinute">Quarter notes a minute, <see cref="Min"/> to <see cref="Max"/>.</param>
    public static IReadOnlyList<PlaybackTempo> Over(IReadOnlyList<PlaybackTempo> piece, int perMinute)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentOutOfRangeException.ThrowIfLessThan(perMinute, Min);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(perMinute, Max);

        int start = TempoSources.MicrosecondsPerQuarter(perMinute);
        long first = piece.Count == 0 ? PlaybackTempo.DefaultMicrosecondsPerQuarter : piece[0].MicrosecondsPerQuarter;
        var played = new PlaybackTempo[Math.Max(1, piece.Count)];
        played[0] = new PlaybackTempo(Tick.Zero, start, TempoSource.Player);
        for (int i = 1; i < piece.Count; i++)
        {
            PlaybackTempo tempo = piece[i];
            if (tempo.Source == TempoSource.Notated)
            {
                played[i] = tempo;
                continue;
            }

            long scaled = ((tempo.MicrosecondsPerQuarter * (long)start) + (first / 2)) / first;
            played[i] = new PlaybackTempo(tempo.At, (int)Math.Clamp(scaled, 1, int.MaxValue), TempoSource.Player);
        }

        return played;
    }
}