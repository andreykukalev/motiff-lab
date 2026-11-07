using Microsoft.AspNetCore.Components;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Piece DNA (0018 section 11), the third place: reached from the score's overflow and from the library card
/// ("&amp;view=dna"), drawn over the score, which stays exactly as it was, selection included. Its map comes from
/// music-core's <see cref="PieceDna"/>; its marks play their passages, and In the score selects one and leaves.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The place to open on arrival: "dna" for Piece DNA.</summary>
    [SupplyParameterFromQuery(Name = "view")]
    public string? View { get; set; }

    private PieceDna? _dna;

    /// <summary>Whether a passage of the map or the whole map is playing, so leaving stops it.</summary>
    private bool _dnaPlaying;

    private void OpenDna()
    {
        if (_notes is null || _meter is null || _score is null)
        {
            return;
        }

        _dnaAside = false;
        _dna = PieceDna.Build(_notes, _meter, _keys, _score.End, _selection, _selection is null ? null : _structure, Words);
        _popover = Popover.None;
        _pillMenu = PillMenu.None;
    }

    private async Task CloseDnaAsync()
    {
        _dna = null;
        await StopDnaAsync();
    }

    private async Task StopDnaAsync()
    {
        if (!_dnaPlaying)
        {
            return;
        }

        _dnaPlaying = false;
        await StopLoopingAsync();
        await Player.PauseAsync();
    }

    /// <summary>A mark, a section or a card's Hear: its passage loops until another is chosen or the map is left.</summary>
    private async Task HearDnaAsync(TickSpan span)
    {
        _looping = false;
        _alternating = false;
        await Player.SelectSourcesAsync(_hear);
        await Player.SetLoopAsync(span);
        _answerLoop = span;
        _dnaPlaying = true;
        await Player.SeekAsync(span.Onset);
        if (Player.State == PlaybackState.Stopped)
        {
            await PlayAsync();
        }
    }

    /// <summary>Plays the piece from its start, the map following, as a listening session; pressed again, it stops.</summary>
    private async Task PlayDnaMapAsync()
    {
        if (_dnaPlaying || Player.State != PlaybackState.Stopped)
        {
            _dnaPlaying = true;
            await StopDnaAsync();
            return;
        }

        await StopLoopingAsync();
        _dnaPlaying = true;
        await Player.SeekAsync(Tick.Zero);
        await PlayAsync();
    }

    /// <summary>Leaves the map for the score with the passage selected.</summary>
    private async Task ShowDnaInScoreAsync(TickSpan span)
    {
        await CloseDnaAsync();
        await SelectAsync(span, note: null);
    }
}