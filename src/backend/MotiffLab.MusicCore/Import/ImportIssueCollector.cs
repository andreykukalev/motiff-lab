using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Import;

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