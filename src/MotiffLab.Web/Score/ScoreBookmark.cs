namespace MotiffLab.Web.Score;

/// <summary>A bookmark the score draws as a bracket in its category's ink above the phrase brackets (decision record 0018, section 5).</summary>
/// <param name="Id">The bookmark's id; a tap on its bracket gives it back.</param>
/// <param name="Start">Where it starts, in ticks.</param>
/// <param name="End">Where it ends, in ticks.</param>
/// <param name="Title">Its title, written in italic at the bracket's left end.</param>
/// <param name="Ink">Its category's ink as a class: "graphite", "carmine", "forest" or "ochre".</param>
/// <param name="Label">What a screen reader says for the bracket, for example "Bookmark: The leap, bars 23–24".</param>
public sealed record ScoreBookmark(string Id, long Start, long End, string Title, string Ink, string Label);

/// <summary>A page of the score as the Contents drawer's Pages tab shows it: its picture and the stretch of the piece on it.</summary>
/// <param name="Page">From 1.</param>
/// <param name="Start">Where the first bar on the page starts, in ticks.</param>
/// <param name="End">Where the last bar on the page ends, in ticks.</param>
/// <param name="Url">The page's picture, an object URL the score keeps until it draws new ones.</param>
public sealed record PageOutline(int Page, long Start, long End, string Url);

/// <summary>A margin note the score marks with its number beside the bar it is about (decision record 0018, section 5).</summary>
/// <param name="Tick">Where the bar it is about starts.</param>
/// <param name="Number">Its number beside the bar, from 1 in score order.</param>
/// <param name="Label">What a screen reader says for the marker, with the note's first words.</param>
public sealed record ScoreMarginNote(string Id, long Tick, int Number, string Label);

/// <summary>A highlight the score draws as a translucent band behind the staves of its bars.</summary>
/// <param name="Ink">Its ink as a class: "amber", "carmine", "forest", "ochre" or "graphite".</param>
public sealed record ScoreHighlightMark(string Id, long Start, long End, string Ink);
/// <summary>A bar Check asks about (0018 section 9): a small amber flag above the top right of the bar, gone once resolved.</summary>
/// <param name="Bar">The bar's number, as the workspace shows it.</param>
/// <param name="Tick">Where the bar starts.</param>
/// <param name="Label">What a screen reader says for the flag: the bar and its reasons.</param>
public sealed record ScoreCheckFlag(int Bar, long Tick, string Label);

/// <summary>A bar a performance plays differently, for the score (decision record 0022, section 4): its number, where it starts, the label's text, and what a screen reader says.</summary>
public sealed record ScorePerformanceMarker(int Bar, long Tick, string Text, string Label);