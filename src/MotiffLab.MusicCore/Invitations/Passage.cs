using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Invitations;

/// <summary>A passage of whole bars to play, with where it sounds.</summary>
/// <param name="FirstBar">0 for a pickup, otherwise from 1.</param>
public sealed record Passage(TickSpan Span, int FirstBar, int LastBar);