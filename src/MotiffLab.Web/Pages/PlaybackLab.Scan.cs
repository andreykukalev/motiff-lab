using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Check the scan (decision record 0011): a piece read from a photo or PDF shows the notes engraved from the event model,
/// and the bars the rules flag are Check flags (PlaybackLab.Check.cs). Confirming bars marks their notes as confirmed by the
/// player; corrections go to the edit log as for any piece.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The checks of the open piece when it was scanned; null for a file the player imported.</summary>
    private ScanCheck? _scanCheck;

    /// <summary>The scanned bars the selection covers, in order.</summary>
    private IReadOnlyList<int> SelectedScanBars => _scanCheck is { } check && _selection is { } selection
        ? [.. check.Bars.Where(bar => bar.Span.Overlaps(selection.Span)).Select(bar => bar.Bar)]
        : [];

    /// <summary>
    /// Starts the check for a scanned piece: the score shows the notes engraved from the event model, since the
    /// recognised file carries no printed accidentals or beams.
    /// </summary>
    private void StartScanCheck(ImportedPiece piece)
    {
        _scanCheck = _project?.Scan is not null && piece.MusicXml is not null ? ScanCheck.Of(piece) : null;
        if (_scanCheck is not null)
        {
            _asWritten = null;
            _scoreChoice = ScoreChoice.Generated;
        }
    }

    /// <summary>Bars Check did not flag, confirmed once the player compared them with the page.</summary>
    private Task ConfirmScanBarsAsync(IReadOnlyList<int> bars) =>
        _project?.Scan is { } scan ? ChangeScanAsync(scan.Confirm(bars)) : Task.CompletedTask;

    /// <summary>Marks the notes again for the checked bars and saves the project, keeping corrections, selection and position.</summary>
    private async Task ChangeScanAsync(ScanOrigin scan)
    {
        _project = _project! with { Scan = scan };
        _imported = _scanCheck!.Mark(_imported, scan.ConfirmedBars);
        await ApplyEditsAsync(_edits);
    }
}