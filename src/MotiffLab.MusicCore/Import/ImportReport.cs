namespace MotiffLab.MusicCore.Import;

/// <summary>What an importer did with a source file, for the import report (S1-I8).</summary>
/// <param name="MeasureCount">Bars from the start of the piece to its end, counting a final partial bar.</param>
/// <remarks>
/// Issues are grouped by code and track with a count, so a large file gives a short report.
/// They appear in the order each kind of issue was first met, which is deterministic for a given
/// file. Messages are plain English for display; <see cref="ImportIssue.Code"/> is the stable key
/// for localisation and tests.
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
public sealed record ImportIssue(ImportIssueKind Kind, string Code, int? Track, int Count, string Message);

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

    public IReadOnlyList<ImportIssue> Build(Func<string, int, string> describe) =>
        _order
            .Select(entry =>
            {
                int count = _counts[(entry.Code, entry.Track)];
                string message = describe(entry.Code, count);
                if (entry.Track is int track)
                {
                    message = $"Track {track + 1}: {message}";
                }

                return new ImportIssue(entry.Kind, entry.Code, entry.Track, count, message);
            })
            .ToList();
}