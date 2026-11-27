using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Tests;

/// <summary>How many times the importer met an issue, read from the report's issues as the import answer writes them.</summary>
internal static class IssueCounts
{
    /// <summary>The count of an issue code across all tracks; 0 when it did not occur.</summary>
    public static int CountOf(this ImportReport report, string code) => CountOf(report.Issues, code);

    /// <summary>The count of an issue code in the issues collected so far, as the report will list them.</summary>
    public static int CountOf(this ImportIssueCollector issues, string code) => CountOf(issues.Build(static (_, _, _) => ""), code);

    private static int CountOf(IEnumerable<ImportIssue> issues, string code) => issues.Where(issue => issue.Code == code).Sum(issue => issue.Count);
}