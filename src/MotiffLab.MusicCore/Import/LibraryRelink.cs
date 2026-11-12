using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Import;

/// <summary>Where an original of the library is in a fresh listing of its folder.</summary>
public enum RelinkStatus
{
    /// <summary>At its place, with its fingerprint: linked.</summary>
    Linked,

    /// <summary>At its place with its size; linked now, its fingerprint to confirm when there is time.</summary>
    Unconfirmed,

    /// <summary>Found elsewhere in the folder by its fingerprint (moved or renamed): linked there, with no question.</summary>
    Moved,

    /// <summary>Not at its place; files of its size are to be fingerprinted before it is known whether it moved.</summary>
    Pending,

    /// <summary>A different file is at its place (the content changed): never linked by itself, possibly the same piece.</summary>
    Changed,

    /// <summary>Not in the folder; the files that are possibly the same piece, if any.</summary>
    Missing,
}

/// <summary>A file that is possibly the same piece as an original whose content was not found: offered, never linked by itself.</summary>
/// <param name="Score">How alike, higher first: the names (0 to 100), then 20 for the same pages, 10 for a size within a tenth, 10 for the same folder.</param>
/// <param name="SamePlace">The file is at the original's place: its content changed.</param>
public sealed record RelinkCandidate(string Path, int Score, bool SamePlace);

/// <summary>One original in the relink plan.</summary>
/// <param name="Path">The file it is linked to (linked, unconfirmed, moved), or the different file at its place (changed); null otherwise.</param>
public sealed record Relinked(LibraryOriginal Original, RelinkStatus Status, string? Path)
{
    /// <summary>For a changed or missing original, the files that are possibly the same piece, the likeliest first, at most three.</summary>
    public IReadOnlyList<RelinkCandidate> Candidates { get; init; } = [];
}

/// <summary>The relink plan: each original, and the files to fingerprint next, to decide and to confirm.</summary>
/// <param name="ToFingerprint">The files whose fingerprint decides where an original went: hash these, then plan again.</param>
/// <param name="ToConfirm">The files linked by place and size, whose fingerprint confirms the link: hash these when there is time.</param>
public sealed record RelinkPlan(IReadOnlyList<Relinked> Originals, IReadOnlyList<string> ToFingerprint, IReadOnlyList<string> ToConfirm);

/// <summary>
/// Links that heal themselves when the player reorganises the folder (version 0.5, «Импорт папки»; Andrei 2026-11-12 17:47
/// UTC): from the library's originals (fingerprint, size, pages, name, the folder they were in) and a fresh listing of the
/// folder, where each original is now. First by place and size, which is fast, the fingerprint confirmed later; then,
/// for one not at its place, by fingerprint among the files of its size anywhere in the folder, so the app reads only
/// those, and a match links it again with no question (it moved or was renamed); last, when no file has its content,
/// the files that are possibly the same piece, by name, pages and size, which are offered and never linked by
/// themselves. Pure and deterministic: the same originals and listing give the same plan. Nothing is changed on disk.
/// </summary>
public static class LibraryRelink
{
    /// <summary>The fewest name points (of 100) a candidate needs, or <see cref="SomeName"/> with the same pages.</summary>
    public const int AlikeName = 50;

    public const int SomeName = 30;

    private const int Shown = 3;

    /// <exception cref="ArgumentException">Two listed files have the same path.</exception>
    public static RelinkPlan Plan(IEnumerable<LibraryOriginal> originals, IEnumerable<FoundFile> listing)
    {
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(listing);
        List<FoundFile> files = [.. listing.OrderBy(static file => file.Path, StringComparer.Ordinal)];
        Dictionary<string, FoundFile> byPath = files.Count == files.Select(static file => file.Path).Distinct(StringComparer.Ordinal).Count()
            ? files.ToDictionary(static file => file.Path, StringComparer.Ordinal)
            : throw new ArgumentException("Each listed file has its own path.", nameof(listing));
        List<LibraryOriginal> ordered = [.. originals.OrderBy(static original => original.ProjectId, StringComparer.Ordinal).ThenBy(static original => original.OriginalId, StringComparer.Ordinal)];

        List<Relinked> found = [.. ordered.Select(original => Find(original, files, byPath))];
        HashSet<string> linked = [.. found.Where(static each => each.Status is RelinkStatus.Linked or RelinkStatus.Unconfirmed or RelinkStatus.Moved).Select(static each => each.Path!)];
        List<Relinked> planned = [.. found.Select(each => each.Status is RelinkStatus.Changed or RelinkStatus.Missing ? each with { Candidates = Candidates(each, files, linked) } : each)];

        List<string> toFingerprint = [.. planned.Where(static each => each.Status == RelinkStatus.Pending)
            .SelectMany(each => SameSize(each.Original, files)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        HashSet<string> deciding = [.. toFingerprint];
        List<string> toConfirm = [.. planned.Where(static each => each.Status == RelinkStatus.Unconfirmed && each.Original.Sha256 is not null)
            .Select(static each => each.Path!).Where(path => !deciding.Contains(path)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        return new RelinkPlan(planned, toFingerprint, toConfirm);
    }

    /// <summary>Where the original is: at its place, moved, still to fingerprint, changed or missing (candidates come after).</summary>
    private static Relinked Find(LibraryOriginal original, List<FoundFile> files, Dictionary<string, FoundFile> byPath)
    {
        FoundFile? atPlace = PlaceOf(original) is { } place && byPath.TryGetValue(place, out FoundFile? file) ? file : null;
        if (atPlace is not null && IsAtPlace(original, atPlace))
        {
            return new(original, original.Sha256 is not null && atPlace.Sha256 is not null ? RelinkStatus.Linked : RelinkStatus.Unconfirmed, atPlace.Path);
        }

        if (original.Sha256 is { } sha && files.Where(each => each.Sha256 == sha).ToList() is { Count: > 0 } moved)
        {
            return new(original, RelinkStatus.Moved, (moved.FirstOrDefault(each => each.Folder == original.Folder) ?? moved[0]).Path);
        }

        if (original.Sha256 is not null && SameSize(original, files).Any())
        {
            return new(original, RelinkStatus.Pending, null);
        }

        return atPlace is null ? new(original, RelinkStatus.Missing, null) : new(original, RelinkStatus.Changed, atPlace.Path);
    }

    /// <summary>
    /// Whether the file at the original's place is it: by fingerprint when both are known, else by size (or by place alone
    /// when the original's size is not known), to be confirmed by fingerprint later.
    /// </summary>
    private static bool IsAtPlace(LibraryOriginal original, FoundFile atPlace) =>
        original.Sha256 is { } sha && atPlace.Sha256 is { } there ? sha == there : original.Bytes is not { } bytes || bytes == atPlace.Bytes;

    /// <summary>The files not fingerprinted yet with the original's size: only they can have its content.</summary>
    private static IEnumerable<string> SameSize(LibraryOriginal original, List<FoundFile> files) =>
        original.Bytes is { } bytes ? files.Where(file => file.Sha256 is null && file.Bytes == bytes).Select(static file => file.Path) : [];

    /// <summary>The original's place in the folder: its folder and file name; null when it did not come from a folder.</summary>
    private static string? PlaceOf(LibraryOriginal original)
    {
        if (original.Folder is not { } folder || original.FileName is not { } name)
        {
            return null;
        }

        try
        {
            return FoundFile.PathOf(folder.Length > 0 ? $"{folder}/{name}" : name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The files that are possibly the same piece: of its kind, not linked to an original, alike enough, the likeliest first.</summary>
    private static RelinkCandidate[] Candidates(Relinked relinked, List<FoundFile> files, HashSet<string> linked)
    {
        LibraryOriginal original = relinked.Original;
        OriginalKind? kind = original.FileName is { } name ? new FoundFile(name, 0).Kind : null;
        return [.. files
            .Where(file => !linked.Contains(file.Path) && (kind is null || file.Kind == kind))
            .Select(file => Scored(original, file, file.Path == relinked.Path))
            .OfType<RelinkCandidate>()
            .OrderByDescending(static candidate => candidate.SamePlace)
            .ThenByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Path, StringComparer.Ordinal)
            .Take(Shown)];
    }

    private static RelinkCandidate? Scored(LibraryOriginal original, FoundFile file, bool samePlace)
    {
        string named = NameText.Folded(PieceDetails.TitleOf(file.Name));
        int name = Math.Max(
            original.FileName is { } fileName ? Similarity(NameText.Folded(PieceDetails.TitleOf(fileName)), named) : 0,
            original.Title is { } title ? Similarity(NameText.Folded(title), named) : 0);
        bool samePages = original.Pages is { } pages && file.Pages == pages;
        if (!samePlace && name < AlikeName && !(samePages && name >= SomeName))
        {
            return null;
        }

        bool nearSize = original.Bytes is { } bytes && Math.Abs(file.Bytes - bytes) * 10 <= bytes;
        int score = name + (samePages ? 20 : 0) + (nearSize ? 10 : 0) + (file.Folder == original.Folder ? 10 : 0);
        return new RelinkCandidate(file.Path, score, samePlace);
    }

    /// <summary>How alike two folded names are, 0 to 100: twice the pairs of letters they share over all their pairs (Dice).</summary>
    internal static int Similarity(string first, string second)
    {
        if (first.Length == 0 || second.Length == 0)
        {
            return 0;
        }

        if (first == second)
        {
            return 100;
        }

        List<string> pairs = Pairs(first);
        List<string> others = Pairs(second);
        int shared = 0;
        foreach (string pair in pairs)
        {
            if (others.Remove(pair))
            {
                shared++;
            }
        }

        int total = pairs.Count + Pairs(second).Count;
        return total == 0 ? 0 : 200 * shared / total;
    }

    private static List<string> Pairs(string text) => [.. Enumerable.Range(0, Math.Max(0, text.Length - 1)).Select(index => text.Substring(index, 2))];
}