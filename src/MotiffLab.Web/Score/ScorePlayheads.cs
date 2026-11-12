namespace MotiffLab.Web.Score;

/// <summary>
/// The two playheads of version 0.5 as the score draws them (the experiment Two playheads and modes; product spec
/// two-playheads). <paramref name="SelectionHolds"/>: the selection has the player, so the player's place is the selection
/// playhead's and the global playhead waits at <paramref name="Global"/>; otherwise the player's place is the global
/// playhead's and the selection playhead waits at <paramref name="Selection"/> (null without a selection).
/// <paramref name="Fade"/>: the other bars fade while the global Play plays.
/// </summary>
public sealed record ScorePlayheads(bool SelectionHolds, long Global, long? Selection, bool Fade);