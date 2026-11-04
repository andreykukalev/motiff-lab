namespace MotiffLab.Web.Score;

/// <summary>A phrase the score draws as a pencil bracket above the top staff (decision record 0018, section 3).</summary>
/// <param name="Start">Where the phrase starts, in ticks.</param>
/// <param name="End">Where it ends, in ticks.</param>
/// <param name="Number">Its number at the level shown, written at the bracket's left end; a tap on the bracket gives it back.</param>
/// <param name="Kind">How its start was found, as a class: "start", "imported", "inferred" or "yours".</param>
/// <param name="Current">Whether it is the selected phrase: its bracket is drawn in the accent.</param>
/// <param name="Ideas">Whether the player kept ideas from it: a plum dot at the bracket's right end.</param>
/// <param name="Label">What a screen reader says for the bracket, for example "Phrase 3, bars 9–16".</param>
public sealed record ScorePhrase(long Start, long End, int Number, string Kind, bool Current, bool Ideas, string Label);