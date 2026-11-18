namespace MotiffLab.MusicCore.Notation;

/// <summary>Where note values may start in a bar (<see cref="BarValues"/>): on multiples of their own length or of the pulse, not across the middle.</summary>
/// <param name="Origin">Where a full bar would start: before the start of a pickup.</param>
internal readonly record struct ValueGrid(long Origin, long Pulse, long Middle);