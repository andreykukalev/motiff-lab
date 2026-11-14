using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Insights;
using MotiffLab.Web.Playback;
using MotiffLab.Web.Score;

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

    /// <summary>The MusicXML of the piece as its file writes it, before the player's tempo is marked on it; null for other pieces.</summary>
    private string? _writtenXml;

    /// <summary>What the tempo control shows (version 0.5.4, #511 section 17): the tempo at the start as played and as the piece states it.</summary>
    private TempoReading? TempoNow => _score is { } score
        ? new TempoReading(score.StartTempo, score.PieceTempos[0], _project is not null, TempoBeats.At(score.Meter, Tick.Zero))
        : null;

    /// <summary>
    /// The piece's tempo the player set ("♩ = 92" read from the printed notes), or null to play it as it states again (version
    /// 0.5.4): kept in the project ("tempo"), played from the place heard with the loop kept, and marked on the score.
    /// </summary>
    private async Task SetPlayerTempoAsync(int? perMinute)
    {
        if (_project is not { } project || _score is null || _notes is null || project.PlayerTempo == perMinute
            || (perMinute is { } given && !PlayerTempo.IsValid(given)))
        {
            return;
        }

        _project = project with { PlayerTempo = perMinute };
        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
        if (CurrentPiece.Piece is { } opened)
        {
            _characterContext = CharacterContext.FromPiece(opened with { PlayerTempo = perMinute });
        }

        _score = _score.WithPlayerTempo(perMinute);
        _generated = ScoreSource.FromNotes(Notation(_notes, _score.End));
        if (_writtenXml is { } xml)
        {
            _asWritten = ScoreSource.AsWritten(WrittenTempo.With(xml, _score));
        }

        // The timeline is sent again, so the place and the loop are put back as they were.
        Tick position = Player.Position;
        TickSpan? loop = Player.Loop;
        await Player.LoadAsync(_score);
        await ApplyFilterAsync();
        await SetVariationSourcesAsync();
        if (loop is { } kept)
        {
            await Player.SetLoopAsync(kept);
        }

        await Player.SeekAsync(position);
    }

    private bool HasScoreVersion => _project?.Performances.Any(version => version.Source == PerformanceSource.Score) == true;
}