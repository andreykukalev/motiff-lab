using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Pieces;

/// <summary>
/// A bookmark (0018, section 5): a titled stretch of the piece in a category, the player's own entry in
/// the Contents of the piece. Bookmarks replace the Tag mark of 0013.
/// </summary>
/// <remarks>
/// A bookmark is kept as its span, so it stays put when the layout, the phrase level or a phrase boundary changes, also
/// one made from a detected phrase (<paramref name="FromPhrase"/>): bookmarks and phrases are separate (the phrases spec,
/// 2026-11-20). The order of a project's bookmarks is the player's order; the score order is by span.
/// </remarks>
/// <param name="Title">The title, trimmed and never blank; pre-filled with the bar range when made.</param>
public sealed record Bookmark(string Id, string Title, string CategoryId, TickSpan Span, bool FromPhrase, DateTimeOffset CreatedAt)
{
    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A bookmark needs an id.", nameof(Id));

    public string Title { get; init; } = !string.IsNullOrWhiteSpace(Title) ? Title.Trim() : throw new ArgumentException("A bookmark needs a title.", nameof(Title));

    public string CategoryId { get; init; } = !string.IsNullOrWhiteSpace(CategoryId) ? CategoryId : throw new ArgumentException("A bookmark needs a category.", nameof(CategoryId));

    /// <summary>
    /// "bars 1–8" / "такты 1–8": the bars of <paramref name="span"/> in <paramref name="meter"/>, the title a bookmark starts
    /// with and the name of an idea kept with a note alone; the one owner of this text (marks, and the bookmarks following
    /// other working notes).
    /// </summary>
    internal static string BarsTitle(MeterMap meter, TickSpan span, Words words) =>
        Bars(meter.BarAt(span.Onset).Number, meter.BarAt(new Tick(span.End.Value - 1)).Number, words);

    /// <summary>"bar 3" / "такт 3", or "bars 1–8" / "такты 1–8".</summary>
    internal static string Bars(int first, int last, Words words) =>
        first == last ? words.T($"bar {first}", $"такт {first}") : words.T($"bars {first}–{last}", $"такты {first}–{last}");
}