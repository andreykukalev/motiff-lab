namespace MotiffLab.Web.Score;

/// <summary>A phrase start the score marks above the staves (#18).</summary>
/// <param name="Tick">Where the phrase starts; the mark goes at the first engraved note at or after it.</param>
/// <param name="Label">The mark's text, for example "Phrase 2".</param>
/// <param name="Kind">How the start was found, as a class: "start", "imported", "inferred" or "yours".</param>
/// <param name="Current">Whether it is the selected phrase, so the score shows where that phrase sits in the piece (#126).</param>
public sealed record ScorePhrase(long Tick, string Label, string Kind, bool Current = false);