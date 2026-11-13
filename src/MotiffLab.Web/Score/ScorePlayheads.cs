namespace MotiffLab.Web.Score;

/// <summary>
/// The two playheads of version 0.5 as the score draws them (the experiment Two playheads and modes; product spec
/// two-playheads). <paramref name="SelectionHolds"/>: the selection has the player, so the player's place is the selection
/// playhead's and the global playhead waits at <paramref name="Global"/>; otherwise the player's place is the global
/// playhead's and the selection playhead waits at <paramref name="Selection"/> (null without a selection). The global
/// playhead is a track under the bar that plays, with a dot on the place (v0.5.2, #479 item 19); no bar fades.
/// </summary>
public sealed record ScorePlayheads(bool SelectionHolds, long Global, long? Selection);