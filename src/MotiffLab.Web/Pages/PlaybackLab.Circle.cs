using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The circle of fifths in the Insights stream (Andrei, 2026-11-06): music-core's <see cref="KeyCircle"/> for the
/// selection, opened from the key and the chords. Its places ring on the score through the facts' own choice; Hear the
/// scale plays the key's scale once from the selection's start (testing, 2026-11-06: a loop could not be stopped), and pressed
/// again it stops. When the scale stops, the piece's own notes are what Play plays again.
/// </summary>
public partial class PlaybackLab
{
    private const string CircleSource = "circle";

    /// <summary>The scale is sounding, or about to: <see cref="_scaleStarted"/> once the engine has started it.</summary>
    private bool _scaleHeard;

    private bool _scaleStarted;

    private Task HearScaleAsync(KeyCircleView circle) => _selection is { } selection ? HearScaleAsync(circle, selection.Span.Onset) : Task.CompletedTask;

    /// <summary>The scale of a section's key in Piece DNA, from the section's start (testing item 30).</summary>
    private Task HearDnaScaleAsync(DnaCircle circle) => HearScaleAsync(circle.Circle, circle.Span.Onset);

    private async Task HearScaleAsync(KeyCircleView circle, Tick at)
    {
        if (_scaleHeard)
        {
            await StopScaleAsync();
            return;
        }

        if (_score is null)
        {
            return;
        }

        // The scale sounds where the selection or the section starts, or ends with the piece when there is no room after it.
        await HearAloneAsync(circle.ScaleNotes(at, _score.End));
    }

    /// <summary>Notes heard once on their own over the piece's timeline, in place of the piece, until they end or are stopped.</summary>
    private async Task HearAloneAsync(IReadOnlyList<Note> scale)
    {
        if (_score is null || scale.Count == 0)
        {
            return;
        }

        TickSpan span = TickSpan.FromBounds(scale[0].Span.Onset, scale[^1].Span.End);
        _looping = false;
        _alternating = false;
        _answerLoop = null;
        _scaleHeard = true;
        _scaleStarted = Player.State != PlaybackState.Stopped;
        await Player.SetSourceAsync(CircleSource, _score.WithNotes(scale));
        await Player.SelectSourcesAsync(CircleSource);
        await Player.SetLoopAsync(span, once: true);
        await Player.SeekAsync(span.Onset);
        if (Player.State == PlaybackState.Stopped)
        {
            await PlayAsync();
        }
    }

    /// <summary>Stops the scale and gives Play back the notes the player chose to hear.</summary>
    private async Task StopScaleAsync()
    {
        _scaleHeard = false;
        _scaleStarted = false;
        if (Player.State != PlaybackState.Stopped)
        {
            await Player.PauseAsync();
        }

        await Player.SetLoopAsync(null);
        await Player.SelectSourcesAsync(_hear);
    }

    /// <summary>The scale has played to its end, or playback was paused over it: the piece's notes come back.</summary>
    private async Task FollowScaleAsync()
    {
        if (!_scaleHeard)
        {
            return;
        }

        if (Player.State != PlaybackState.Stopped)
        {
            _scaleStarted = true;
        }
        else if (_scaleStarted)
        {
            await StopScaleAsync();
        }
    }
}