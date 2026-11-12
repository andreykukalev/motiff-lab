using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>One piece of the pieces layer: its bookmark, its title, and its bars from its first to the bar before the next piece.</summary>
public sealed record LayerPiece(string BookmarkId, string Title, TickSpan Span, int FirstBar, int LastBar);

/// <summary>Where a piece starts, as the player confirmed it, with its title; a blank title is «Пьеса N».</summary>
public sealed record PieceStart(string? Title, int FirstBar);

/// <summary>
/// The pieces layer (version 0.5): the pieces of a long score or recording, kept as ordinary bookmarks in the reserved
/// category <see cref="BookmarkCategory.PiecesId"/>, so the project file does not change. Each bookmark is one piece, its
/// title the piece's and its span from its first bar to the first bar of the next (the last to the end). Analysis never
/// reads it: it is the player's.
/// </summary>
/// <remarks>
/// Read: the bookmarks by onset; where two overlap (after two copies were merged, 0026 S9, bookmarks join by id), the later
/// cuts the earlier short, and an empty one is left out. Save replaces the whole layer and touches no other category.
/// </remarks>
public static class PieceLayer
{
    /// <summary>The pieces of the layer in score order; empty when the project has none.</summary>
    public static IReadOnlyList<LayerPiece> Read(Project project, MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(meter);
        Bookmark[] marks = [.. Bookmarks(project).OrderBy(mark => mark.Span.Onset).ThenBy(mark => mark.Id, StringComparer.Ordinal)];
        var pieces = new List<LayerPiece>(marks.Length);
        for (int index = 0; index < marks.Length; index++)
        {
            Bookmark mark = marks[index];
            Tick end = index + 1 < marks.Length && marks[index + 1].Span.Onset < mark.Span.End ? marks[index + 1].Span.Onset : mark.Span.End;
            if (end <= mark.Span.Onset)
            {
                continue;
            }

            pieces.Add(new LayerPiece(mark.Id, mark.Title, TickSpan.FromBounds(mark.Span.Onset, end), meter.BarAt(mark.Span.Onset).Number, meter.BarAt(new Tick(end.Value - 1)).Number));
        }

        return pieces;
    }

    /// <summary>Whether the project has a pieces layer.</summary>
    public static bool Has(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Bookmarks(project).Any();
    }

    /// <summary>
    /// The project with the pieces layer replaced by <paramref name="pieces"/>: one bookmark each, from its first bar to the
    /// first bar of the next, the last to <paramref name="end"/>. The layer's bookmarks keep their ids in score order, so
    /// saving again on another device changes them rather than adding more; the category is added, named in the player's
    /// language, when the project has none yet. None clears the layer.
    /// </summary>
    /// <exception cref="ArgumentException">The pieces are not in order, a bar before the piece's first, or after its last.</exception>
    public static Project Save(Project project, IReadOnlyList<PieceStart> pieces, MeterMap meter, Tick end, Words words, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(meter);
        int firstBar = meter.BarAt(Tick.Zero).Number;
        CheckOrder(pieces, firstBar, meter.BarAt(new Tick(Math.Max(0, end.Value - 1))).Number);
        Bookmark[] kept = [.. Bookmarks(project).OrderBy(mark => mark.Span.Onset).ThenBy(mark => mark.Id, StringComparer.Ordinal)];
        Project cleared = project with { Bookmarks = [.. project.Bookmarks.Where(mark => mark.CategoryId != BookmarkCategory.PiecesId)] };
        if (pieces.Count > 0 && cleared.FindCategory(BookmarkCategory.PiecesId) is null)
        {
            cleared = cleared.AddCategory(new BookmarkCategory(BookmarkCategory.PiecesId, BookmarkCategory.PiecesNameIn(words), InkColour.Graphite));
        }

        Project saved = cleared;
        for (int index = 0; index < pieces.Count; index++)
        {
            Tick onset = index == 0 && pieces[0].FirstBar == firstBar ? Tick.Zero : meter.GetBar(pieces[index].FirstBar).Span.Onset;
            Tick until = index + 1 < pieces.Count ? meter.GetBar(pieces[index + 1].FirstBar).Span.Onset : end;
            Bookmark? reused = index < kept.Length ? kept[index] : null;
            saved = saved.AddBookmark(new Bookmark(reused?.Id ?? saved.NextBookmarkId(), TitleOf(pieces[index], index + 1, words), BookmarkCategory.PiecesId, TickSpan.FromBounds(onset, until), FromPhrase: false, reused?.CreatedAt ?? now));
        }

        return saved;
    }

    /// <exception cref="ArgumentException">A piece starts no later than the one before it, before the first bar or after the last.</exception>
    private static void CheckOrder(IReadOnlyList<PieceStart> pieces, int firstBar, int lastBar)
    {
        for (int index = 0; index < pieces.Count; index++)
        {
            int bar = pieces[index].FirstBar;
            int after = index == 0 ? firstBar - 1 : pieces[index - 1].FirstBar;
            if (bar <= after || bar > lastBar)
            {
                throw new ArgumentException($"Piece {index + 1} starts at a bar from {after + 1} to {lastBar}, after the piece before it; not at bar {bar}.", nameof(pieces));
            }
        }
    }

    private static string TitleOf(PieceStart piece, int number, Words words) =>
        string.IsNullOrWhiteSpace(piece.Title) ? words.T($"Piece {number}", $"Пьеса {number}") : piece.Title;

    private static IEnumerable<Bookmark> Bookmarks(Project project) => project.Bookmarks.Where(mark => mark.CategoryId == BookmarkCategory.PiecesId);
}