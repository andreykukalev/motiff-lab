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