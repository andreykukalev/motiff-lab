using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A recording's bars worked out again with the pieces layer in place (version 0.5, «Темп живого исполнения», the v0.5 data
/// design, section 7 (а) 6): the layer's bookmarks do not keep the bars from changing, the beat is found afresh from where
/// each piece starts in the recording, and each piece moves along by the time it was played: its tick on the old bars to
/// the time in the recording (<see cref="HeardClock"/>), that time to a tick on the new ones, and to the nearest bar start.
/// Everything else placed in the bars (corrections, marks, ideas, checked bars, other bookmarks) still keeps them as they are.
/// It also finds a transcription's bars afresh from the notes it keeps (<see cref="FindBars"/>, <see cref="Find"/>).
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
    public static GridSearch SearchFor(Project project, ImportedPiece opened, GridHint? hint = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(opened);
        return GridSearch.Live with { Starts = [.. StartsOf(project, opened).Skip(1)], Hint = hint };
    }

    /// <summary>
    /// The bars that fit the transcription's notes best (decision record 0016), in the signature and with a beat of
    /// as many 16ths as asked when given; null when the notes follow no beat.
    /// </summary>
    /// <param name="project">A transcription.</param>
    /// <exception cref="InvalidOperationException">The project is not a transcription.</exception>
    /// <param name="search">How to follow a live performance (version 0.5, <see cref="RecordingBars.FindLive"/>); null finds the beat once.</param>
    public static BarGrid? FindBars(this Project project, TimeSignature? signature = null, int? stepsPerBeat = null, GridSearch? search = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        MidiImport heard = HeardNotes(project)
            ?? throw new ProjectFileException("This project says it was transcribed from a recording, but its piece is not MIDI.");
        return RecordingBars.Find(heard, signature, stepsPerBeat, search);
    }

    /// <summary>The bars of the project's recording found by <paramref name="search"/>, with how many sections and whether a hinted signature was taken.</summary>
    /// <exception cref="InvalidOperationException">The project is not a transcription.</exception>
    public static GridFound Find(Project project, GridSearch search)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(search);
        return HeardNotes(project) is { } heard ? RecordingBars.FindLive(heard, search) : GridFound.None;
    }

    /// <summary>
    /// Where the recording's bars read better in an odd signature (version 0.5, «Смена размера в записи», a preview: nothing
    /// is changed or saved): <see cref="RecordingBars.OddMeters"/> on the project's bars, each stretch as the numbers of the first
    /// and last bars of <paramref name="opened"/> it covers. None for a project without bars worked out from its notes.
    /// </summary>
    /// <param name="opened">The project's piece as it opens, on its bars.</param>
    public static IReadOnlyList<OddBars> OddMeters(Project project, ImportedPiece opened)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(opened);
        if (project.Transcription?.Bars is not { } grid || Import.PieceImporter.Import(project.FileName, project.Source).Piece?.Midi is not { } heard)
        {
            return [];
        }

        long end = Math.Max(1, opened.End.Value);
        return [.. grid.OddMeters(heard).Select(stretch => new OddBars(
            opened.Meter.BarAt(new Tick(Math.Min(end - 1, stretch.From * BarGrid.StepTicks))).Number,
            opened.Meter.BarAt(new Tick(Math.Min(end, stretch.To * BarGrid.StepTicks) - 1)).Number,
            stretch.Signature))];
    }

    /// <summary>
    /// What a score of the piece says about its bars, a hint for the recording: the first version written from a score
    /// (<see cref="PerformanceSource.Score"/>), its first signature when the bars offer it and its first tempo; null without one.
    /// </summary>
    public static GridHint? HintOf(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        foreach (SavedPerformance version in project.Performances.Where(version => version.Source == PerformanceSource.Score))
        {
            if (Import.PieceImporter.Import("score.mid", version.Midi).Piece is { Midi: { } midi } score)
            {
                TimeSignature signature = score.Meter.Changes[0].Signature;
                int? tempo = midi.Tempos.Count > 0 && !midi.Tempos[0].IsAssumed ? (int)((60_000_000L + (midi.Tempos[0].MicrosecondsPerQuarter / 2)) / midi.Tempos[0].MicrosecondsPerQuarter) : null;
                return new GridHint(BarGrid.Signatures.Contains(signature) ? signature : null, tempo);
            }
        }

        return null;
    }

    /// <summary>
    /// The project in <paramref name="bars"/> (or the engine's when null), as <see cref="Project.ChangeBars"/> writes it, with
    /// the pieces layer moved along by the recording's time; a piece that would start no later than the one before it joins it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The project is not a transcription, or <see cref="CanChange"/> is false.</exception>
    public static Project Change(Project project, ImportedPiece opened, BarGrid? bars, Words words, DateTimeOffset now)
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

        ImportedPiece reopened = OpenedProject.Open(changed).Piece;
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

    /// <summary>The notes the transcription keeps, as the engine wrote them; null when its piece is not MIDI.</summary>
    /// <exception cref="InvalidOperationException">The project is not a transcription.</exception>
    private static MidiImport? HeardNotes(Project project)
    {
        if (project.Transcription is null)
        {
            throw new InvalidOperationException("Only a transcription has bars worked out from its notes.");
        }

        return Import.PieceImporter.Import(project.FileName, project.Source).Piece?.Midi;
    }

    /// <summary>Where each piece of the layer starts in the recording, in microseconds of the stretch sent.</summary>
    private static long[] StartsOf(Project project, ImportedPiece opened) =>
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

/// <summary>Bars <paramref name="FirstBar"/> to <paramref name="LastBar"/> of a recording that read better in <paramref name="Signature"/> (a preview).</summary>
public sealed record OddBars(int FirstBar, int LastBar, TimeSignature Signature);