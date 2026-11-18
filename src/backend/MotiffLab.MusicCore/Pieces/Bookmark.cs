using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Pieces;

/// <summary>
/// A bookmark (0018, section 5): a titled stretch of the piece in a category, the player's own entry in
/// the Contents of the piece. Bookmarks replace the Tag mark of 0013.
/// </summary>
/// <remarks>
/// A bookmark is kept as its span, so it stays put when the layout or the phrase level changes.
/// One made from a detected phrase (<paramref name="FromPhrase"/>) follows that phrase through merges and
/// splits: after the phrases change it takes the span of the phrase its onset lies in
/// (<see cref="Follow"/>). The order of a project's bookmarks is the player's order; the score order is
/// by span.
/// </remarks>
/// <param name="Title">The title, trimmed and never blank; pre-filled with the bar range when made.</param>
public sealed record Bookmark(string Id, string Title, string CategoryId, TickSpan Span, bool FromPhrase, DateTimeOffset CreatedAt)
{
    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A bookmark needs an id.", nameof(Id));

    public string Title { get; init; } = !string.IsNullOrWhiteSpace(Title) ? Title.Trim() : throw new ArgumentException("A bookmark needs a title.", nameof(Title));

    public string CategoryId { get; init; } = !string.IsNullOrWhiteSpace(CategoryId) ? CategoryId : throw new ArgumentException("A bookmark needs a category.", nameof(CategoryId));

    /// <summary>
    /// The bookmark where it belongs after the phrases changed: a bookmark made from a phrase takes the
    /// span of the phrase its onset lies in, so a merge widens it and a split keeps it on the first part.
    /// Any other bookmark, or one whose onset lies in no phrase, stays as it is.
    /// </summary>
    public Bookmark Follow(IEnumerable<TickSpan> phrases)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        if (!FromPhrase)
        {
            return this;
        }

        TickSpan? home = phrases.Cast<TickSpan?>().FirstOrDefault(phrase => phrase!.Value.Contains(Span.Onset));
        return home is { } span && span != Span ? this with { Span = span } : this;
    }
}