using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>One piece of the pieces layer in Check: its title and bars, and how many of its flagged bars are still open, of how many.</summary>
public sealed record CheckPiece(string Title, int FirstBar, int LastBar, int Open, int Total);

/// <summary>
/// Check by pieces (version 0.5): the Check flags of a score or recording of several pieces grouped by the pieces layer
/// (<see cref="PieceLayer"/>), each piece with its bars to check, and «Выглядит верно» for a whole piece: every flagged
/// bar in it resolved, one new project, as <see cref="Project.ResolveCheck"/> resolves one (a scan confirms the bars).
/// Counts are of bars, not flags: a bar with two reasons is one bar to check. The project file does not change.
/// </summary>
public static class CheckPieces
{
    /// <summary>The pieces of the layer with their flagged bars, open and in all, in score order; empty without a layer or flags.</summary>
    public static IReadOnlyList<CheckPiece> Of(Project project, MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(meter);
        IReadOnlyList<CheckFlag> flags = project.Check ?? [];
        if (flags.Count == 0)
        {
            return [];
        }

        return [.. PieceLayer.Read(project, meter).Select(piece => new CheckPiece(
            piece.Title,
            piece.FirstBar,
            piece.LastBar,
            Bars(flags, piece.FirstBar, piece.LastBar, open: true).Count,
            Bars(flags, piece.FirstBar, piece.LastBar, open: null).Count))];
    }

    /// <summary>The bars from <paramref name="first"/> to <paramref name="last"/> with a flag still open, in order.</summary>
    public static IReadOnlyList<int> OpenBars(Project project, int first, int last)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Bars(project.Check ?? [], first, last, open: true);
    }

    /// <summary>
    /// The project with every flag on the bars from <paramref name="first"/> to <paramref name="last"/> resolved, or open
    /// again: the bars whose flags were otherwise, folded through <see cref="Project.ResolveCheck"/>. The project itself
    /// when nothing there changes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The bars are not in order, or start below 0.</exception>
    public static Project Resolve(Project project, int first, int last, bool resolved = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (first < 0 || last < first)
        {
            throw new ArgumentOutOfRangeException(nameof(first), $"Bars {first}–{last} are not bars from 0 in order.");
        }

        return Resolve(project, Bars(project.Check ?? [], first, last, open: resolved), resolved);
    }

    /// <summary>The project with every flag on each of <paramref name="bars"/> resolved, or open again (Undo of a whole piece: the bars it resolved).</summary>
    /// <exception cref="KeyNotFoundException">Check has nothing about one of the bars.</exception>
    public static Project Resolve(Project project, IEnumerable<int> bars, bool resolved)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(bars);
        return bars.Distinct().Order().Aggregate(project, (changed, bar) => changed.ResolveCheck(bar, resolved));
    }

    /// <summary>The flagged bars in a stretch, in order: those with a flag open (true), with every flag resolved (false) or any (null).</summary>
    private static List<int> Bars(IEnumerable<CheckFlag> flags, int first, int last, bool? open) =>
        [.. flags.Where(flag => flag.Bar >= first && flag.Bar <= last)
            .GroupBy(static flag => flag.Bar)
            .Where(bar => open is not { } wanted || bar.Any(static flag => !flag.Resolved) == wanted)
            .Select(static bar => bar.Key)
            .Order()];
}