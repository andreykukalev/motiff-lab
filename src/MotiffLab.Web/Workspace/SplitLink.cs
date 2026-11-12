using System.Globalization;

using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Localization;

namespace MotiffLab.Web.Workspace;

/// <summary>
/// The pieces made from the collection open (the «Пьесы в файле» experiment, version 0.4), as their project files tell it:
/// the pieces of the library that keep this collection's original (<see cref="PieceParts.IsPartOf"/>), and the playlist
/// they joined, so the collection can say «Разделено на N пьес».
/// </summary>
/// <param name="Playlist">The playlist the first piece is in; null when it is in none.</param>
public sealed record SplitLink(string? Playlist, IReadOnlyList<PartLink> Parts)
{
    /// <summary>
    /// The pieces of the library made from the collection whose original is <paramref name="original"/>, by their place in it,
    /// then by title (a score cut by bars keeps no place); null when there are none.
    /// </summary>
    public static SplitLink? Of(IEnumerable<ProjectSummary> library, string collectionId, PieceOriginal original)
    {
        ProjectSummary[] made = [.. library
            .Where(summary => summary.Id != collectionId && summary.Part is { } part && PieceParts.IsPartOf(part, original))
            .OrderBy(static summary => summary.Part!.Place?.Pages?.First ?? int.MaxValue)
            .ThenBy(static summary => summary.Part!.Place?.Milliseconds?.From ?? long.MaxValue)
            .ThenBy(TitleOf, StringComparer.CurrentCulture)];
        return made.Length == 0 ? null : new SplitLink(made[0].Details?.Playlists.FirstOrDefault(), [.. made.Select(static summary =>
            new PartLink(summary.Id, TitleOf(summary), null, null, summary.Part!.Place))]);
    }

    private static string TitleOf(ProjectSummary summary) => summary.Details?.Title.Value ?? summary.FileName;
}

/// <summary>
/// One piece made from a collection: its project, its title, the collection's bars it took (known only right after the
/// split: the format has no place in bars) and its place in the collection's original (pages, or a recording's stretch).
/// </summary>
public sealed record PartLink(string Id, string Title, int? First, int? Last, PartOf? Place);

/// <summary>Where the piece open came from: the collection's original by the name the piece keeps, the playlist, and its place there.</summary>
public sealed record PartOrigin(string Collection, string? Playlist, PartOf? Place);

/// <summary>A piece's place in its collection's original in words: «с. 6–11», «с. 4» or «12:30–19:40»; empty with none.</summary>
public static class PartPlace
{
    /// <summary>The experiment's switch in Settings · Experimental (Pieces in a file), off by default and kept on the device.</summary>
    public const string Setting = "experimental.pieceParts";

    /// <summary>A library card's line for a piece made from a collection: «Из «Сборник» · с. 6–11»; null for a piece of its own.</summary>
    public static string? CardLine(UiText text, ProjectSummary summary)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.Part is not { } part)
        {
            return null;
        }

        string from = text.Format("library.partOf", part.Original.Shown);
        return Of(text, part.Place) is { Length: > 0 } place ? $"{from} · {place}" : from;
    }

    /// <summary>
    /// Whether a row is of a piece made from a collection (it keeps the collection's original) but was saved before rows
    /// said so (version 0.4): its row is then written again from the piece.
    /// </summary>
    public static bool Unsaid(ProjectSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return summary.Part is null && summary.Originals.Any(static original => original.Id == PieceParts.OriginalId);
    }

    public static string Of(UiText text, PartOf? place)
    {
        ArgumentNullException.ThrowIfNull(text);
        return place?.Pages is var (first, last) ? first == last ? text.Format("parts.onPage", first) : text.Format("parts.onPages", first, last)
            : place?.Milliseconds is var (from, to) ? Times(from, to)
            : "";
    }

    /// <summary>«12:30–19:40»: a stretch of the recording, with the hours when it runs past one.</summary>
    public static string Times(long from, long to) => $"{Clock(from)}–{Clock(to)}";

    private static string Clock(long milliseconds)
    {
        long seconds = Math.Max(0, milliseconds) / 1000;
        return seconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }
}