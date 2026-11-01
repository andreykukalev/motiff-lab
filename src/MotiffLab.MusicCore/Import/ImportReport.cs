namespace MotiffLab.MusicCore.Import;

/// <summary>What an importer did with a source file, for the import report (S1-I8).</summary>
/// <param name="MeasureCount">Bars from the start of the piece to its end, counting a final partial bar.</param>
/// <remarks>
/// Issues are grouped by code and track with a count, so a large file gives a short report.
/// They appear in the order each kind of issue was first met, which is deterministic for a given
/// file. Messages are plain English; <see cref="ImportIssue.Text"/> gives them in the player's language
/// (0018, preparation item 13), and <see cref="ImportIssue.Code"/> is the stable key for tests.
/// </remarks>
public sealed record ImportReport(
    string Format,
    int TrackCount,
    int NoteCount,
    int MeasureCount,
    IReadOnlyList<ImportIssue> Issues)
{
    public IEnumerable<ImportIssue> OfKind(ImportIssueKind kind) => Issues.Where(issue => issue.Kind == kind);

    /// <summary>Total count of an issue code across all tracks; 0 when it did not occur.</summary>
    public int CountOf(string code) => Issues.Where(issue => issue.Code == code).Sum(issue => issue.Count);
}

/// <summary>One kind of thing the importer rounded, assumed, repaired or dropped.</summary>
/// <param name="Track">Zero-based source track, or null when the issue concerns the whole file.</param>
/// <param name="Message">The issue in plain English.</param>
public sealed record ImportIssue(ImportIssueKind Kind, string Code, int? Track, int Count, string Message)
{
    /// <summary>The issue in Russian; null where only the English is known.</summary>
    public string? RussianMessage { get; init; }

    /// <summary>The issue in the language: the Russian when there is one, else the English.</summary>
    public string Text(Words words) => words.IsRussian && RussianMessage is { } russian ? russian : Message;

    /// <summary>"Track 2: …"; "Дорожка 2: …" for an issue of one track.</summary>
    internal static string OnTrack(int? track, string message, Words words) =>
        track is int index ? $"{words.T("Track", "Дорожка")} {index + 1}: {message}" : message;
}

public enum ImportIssueKind
{
    /// <summary>Source timing was rounded to the canonical grid (at most half a tick per boundary).</summary>
    Rounded,

    /// <summary>The source was silent or ambiguous and the importer chose a value; shown as a guess.</summary>
    Assumed,

    /// <summary>The source was malformed and the importer kept what it could read.</summary>
    Repaired,

    /// <summary>Source data that Stage 1 does not use was left out.</summary>
    Dropped,
}

/// <summary>Collects issues during an import and builds the grouped list.</summary>
internal sealed class ImportIssueCollector
{
    private readonly Dictionary<(string Code, int? Track), int> _counts = [];
    private readonly List<(ImportIssueKind Kind, string Code, int? Track)> _order = [];

    public void Add(ImportIssueKind kind, string code, int? track = null, int count = 1)
    {
        if (count <= 0)
        {
            return;
        }

        var key = (code, track);
        if (_counts.TryGetValue(key, out int existing))
        {
            _counts[key] = existing + count;
            return;
        }

        _counts[key] = count;
        _order.Add((kind, code, track));
    }

    public int CountOf(string code) => _counts.Where(entry => entry.Key.Code == code).Sum(entry => entry.Value);

    /// <summary>The issues, each described in English and Russian.</summary>
    public IReadOnlyList<ImportIssue> Build(Func<string, int, Words, string> describe) =>
        _order
            .Select(entry =>
            {
                int count = _counts[(entry.Code, entry.Track)];
                return new ImportIssue(entry.Kind, entry.Code, entry.Track, count, ImportIssue.OnTrack(entry.Track, describe(entry.Code, count, Words.English), Words.English))
                {
                    RussianMessage = ImportIssue.OnTrack(entry.Track, describe(entry.Code, count, Words.Russian), Words.Russian),
                };
            })
            .ToList();
}