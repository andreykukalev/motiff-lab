using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A recording's bars worked out again with the pieces layer in place (version 0.5, «Темп живого исполнения», the v0.5 data
/// design, section 7 (а) 6): the layer's bookmarks do not keep the bars from changing, the beat is found afresh from where
/// each piece starts in the recording, and each piece moves along by the time it was played: its tick on the old bars to
/// the time in the recording (<see cref="HeardClock"/>), that time to a tick on the new ones, and to the nearest bar start.
/// Everything else placed in the bars (corrections, marks, ideas, checked bars, other bookmarks) still keeps them as they are.
/// </summary>
public static class LiveBars
{
    /// <summary>Whether the bars can change, the pieces layer moving along: <see cref="Project.CanChangeBars"/> without the layer's bookmarks.</summary>
    public static bool CanChange(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return WithoutLayer(project).CanChangeBars;
    }

    /// <summary>The live search for the recording, a section starting where each piece of the layer after the first starts, with the hint when one is given.</summary>
    /// <param name="opened">The project's piece as it opens, on its bars.</param>
    public static GridSearch SearchFor(Project project, Import.ImportedPiece opened, GridHint? hint = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(opened);
        return GridSearch.Live with { Starts = [.. StartsOf(project, opened).Skip(1)], Hint = hint };
    }

    /// <summary>
    /// The project in <paramref name="bars"/> (or the engine's when null), as <see cref="Project.ChangeBars"/> writes it, with
    /// the pieces layer moved along by the recording's time; a piece that would start no later than the one before it joins it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The project is not a transcription, or <see cref="CanChange"/> is false.</exception>
    public static Project Change(Project project, Import.ImportedPiece opened, BarGrid? bars, Words words, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(opened);
        IReadOnlyList<LayerPiece> layer = PieceLayer.Read(project, opened.Meter);
        long[] starts = StartsOf(project, opened);
        Project changed = WithoutLayer(project).ChangeBars(bars);
        if (layer.Count == 0)
        {
            return changed;
        }

        Import.ImportedPiece reopened = OpenedProject.Open(changed).Piece;
        HeardClock clock = HeardClock.Of(changed, reopened)!;
        int lastBar = reopened.Meter.BarAt(new Tick(Math.Max(0, reopened.End.Value - 1))).Number;
        var moved = new List<PieceStart>(layer.Count);
        for (int index = 0; index < layer.Count; index++)
        {
            int bar = index == 0 && layer[0].Span.Onset == Tick.Zero
                ? reopened.Meter.BarAt(Tick.Zero).Number
                : Math.Min(lastBar, NearestBar(reopened.Meter, clock.TickAt(starts[index])));
            if (moved.Count == 0 || bar > moved[^1].FirstBar)
            {
                moved.Add(new PieceStart(layer[index].Title, bar));
            }
        }

        // The layer's bookmarks keep their ids and categories: saved again over the project without them.
        return PieceLayer.Save(changed with { Bookmarks = project.Bookmarks, Categories = project.Categories }, moved, reopened.Meter, reopened.End, words, now);
    }

    /// <summary>Where each piece of the layer starts in the recording, in microseconds of the stretch sent.</summary>
    private static long[] StartsOf(Project project, Import.ImportedPiece opened) =>
        HeardClock.Of(project, opened) is { } clock
            ? [.. PieceLayer.Read(project, opened.Meter).Select(piece => clock.MicrosecondsAt(piece.Span.Onset))]
            : [];

    /// <summary>The number of the bar whose start is nearest <paramref name="tick"/>.</summary>
    private static int NearestBar(MeterMap meter, Tick tick)
    {
        Bar bar = meter.BarAt(tick);
        return tick.Value - bar.Span.Onset.Value <= bar.Span.End.Value - tick.Value ? bar.Number : bar.Number + 1;
    }

    private static Project WithoutLayer(Project project) =>
        project with { Bookmarks = [.. project.Bookmarks.Where(mark => mark.CategoryId != BookmarkCategory.PiecesId)] };
}