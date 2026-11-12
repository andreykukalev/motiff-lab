using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Insights;

namespace MotiffLab.Web.Pages;

/// <summary>
/// «Темп живого исполнения» (an experiment, Settings · Experimental, version 0.5): the tempo card in Insights, from music-core's
/// <see cref="TempoProfile"/> of the piece and of each piece of its layer, and in the Check drawer's head for a recording the
/// bars found again section by section (<see cref="LiveBars"/>), with «Такты по партитуре» taking a score version's metre and
/// tempo as a hint. The import report says whether the hint's metre fitted.
/// </summary>
public partial class PlaybackLab
{
    private (object Meter, object Score, TempoView? View)? _tempo;

    private bool LiveTempoOn => SettingsStore.IsOn(TempoCard.Setting);

    /// <summary>The card's data: none while the experiment is off or before the piece has bars.</summary>
    private TempoView? TempoShown
    {
        get
        {
            if (!LiveTempoOn || _meter is not { } meter || _score is not { } score)
            {
                return null;
            }

            if (_tempo is { } cached && ReferenceEquals(cached.Meter, meter) && ReferenceEquals(cached.Score, score))
            {
                return cached.View;
            }

            bool inferred = _project?.Transcription?.Bars is not null;
            TempoView? view = TempoProfile.Of(meter, score.Tempos, score.End, inferred) is { } whole
                ? new TempoView(whole, _project is { } project
                    ? [.. PieceLayer.Read(project, meter)
                        .Select(piece => (piece, TempoProfile.Of(meter, score.Tempos, score.End, inferred, piece.Span)))
                        .Where(row => row.Item2 is not null)
                        .Select(row => new TempoPieceRow(row.piece.Title, row.Item2!, row.piece.FirstBar, row.piece.LastBar))]
                    : [])
                : null;
            _tempo = (meter, score, view);
            return view;
        }
    }

    private Task ShowTempoBarsAsync((int First, int Last) bars) => SelectBarRangeAsync(bars.First, bars.Last);

    /// <summary>The bars of the recording found again section by section, with a score version's hint when asked; the piece opens again in them.</summary>
    private async Task FindLiveBarsAsync(bool fromScore)
    {
        if (_project is not { Transcription: not null } project || CurrentPiece.Piece is not { } opened || !LiveBars.CanChange(project))
        {
            return;
        }

        GridHint? hint = fromScore ? LiveBars.HintOf(project) : null;
        GridFound found = LiveBars.Find(project, LiveBars.SearchFor(project, opened, hint));
        if (found.Grid is not { } grid)
        {
            _marksStatus = Text["transcribe.barsNone"];
            return;
        }

        Project changed = LiveBars.Change(project, opened, grid, Words, DateTimeOffset.Now);
        await Projects.SaveAsync(changed);
        OpenedProject reopened = OpenedProject.Open(changed);
        _checkFixing = null;
        _scoreChecks = null;
        _marksStatus = LiveBarsStatus(found, hint, grid);
        CurrentPiece.Open(reopened.Piece, _filter, changed, reopened);
    }

    private string LiveBarsStatus(GridFound found, GridHint? hint, BarGrid grid)
    {
        string sections = Text.Format("tempo.sectionsFound", found.Sections, grid.Signature, grid.QuarterNotesPerMinute);
        return (found.HintFits, hint?.Signature) switch
        {
            (true, { } signature) => $"{sections} {Text.Format("tempo.hintFit", signature)}",
            (false, { } signature) => $"{sections} {Text.Format("tempo.hintNoFit", signature, grid.Signature)}",
            _ => sections,
        };
    }

    private bool HasScoreVersion => _project?.Performances.Any(version => version.Source == PerformanceSource.Score) == true;
}