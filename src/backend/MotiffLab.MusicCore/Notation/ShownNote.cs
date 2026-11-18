using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Notation;

/// <summary>A note as the notation shows it: where it is written to start and end, which may differ from when it sounds.</summary>
internal sealed record ShownNote(Note Note, long Onset, long RawEnd)
{
    /// <summary>At least one grid step after the onset, so a very short note still shows; a grace note takes no time.</summary>
    public long End => IsGrace ? Onset : Math.Max(RawEnd, Onset + NotationGenerator.Grid);

    /// <summary>Written as a grace note in front of the chord that holds <see cref="Target"/>.</summary>
    public bool IsGrace { get; init; }

    public string? Target { get; init; }
}