using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Web.Pages;

/// <summary>
/// «Смена размера в записи» (an experiment, Settings · Experimental, version 0.5, a preview): for a transcription with bars,
/// the Check drawer's head says where music-core finds that the bars read better in an odd signature (5/8, 7/8, 9/8, 5/4,
/// 12/8; <see cref="LiveBars.OddMeters"/>). Nothing is changed or saved. Worked out once for each project and piece opened.
/// </summary>
public partial class PlaybackLab
{
    public const string MetersSetting = "experimental.meters";

    private (Project Project, ImportedPiece Piece, IReadOnlyList<OddBars> Stretches)? _oddMeters;

    private bool MetersOn => SettingsStore.IsOn(MetersSetting);

    /// <summary>The stretches for the open project's bars; none without bars.</summary>
    private IReadOnlyList<OddBars> OddMetersShown
    {
        get
        {
            if (_project is not { Transcription.Bars: not null } project || CurrentPiece.Piece is not { } opened)
            {
                return [];
            }

            if (_oddMeters is { } cached && ReferenceEquals(cached.Project, project) && ReferenceEquals(cached.Piece, opened))
            {
                return cached.Stretches;
            }

            IReadOnlyList<OddBars> stretches = LiveBars.OddMeters(project, opened);
            _oddMeters = (project, opened, stretches);
            return stretches;
        }
    }
}