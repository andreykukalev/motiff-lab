using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The circle of fifths in the Insights stream (Andrei, 2026-11-06): music-core's <see cref="KeyCircle"/> for the
/// selection, opened from the key and the chords. Its places ring on the score through the facts' own choice; Hear the
/// scale loops the key's scale from the selection's start until the selection changes or the loop is stopped.
/// </summary>
public partial class PlaybackLab
{
    private const string CircleSource = "circle";

    private async Task HearScaleAsync(KeyCircleView circle)
    {
        if (_score is null || _selection is not { } selection)
        {
            return;
        }

        // The scale sounds where the selection starts, or ends with the piece when there is no room after it.
        long length = circle.ScaleNotes(Tick.Zero) is { Count: > 0 } probe ? probe[^1].Span.End.Value : 0;
        Tick at = new(Math.Max(0, Math.Min(selection.Span.Onset.Value, _score.End.Value - length)));
        IReadOnlyList<Note> scale = circle.ScaleNotes(at);
        if (scale.Count == 0)
        {
            return;
        }

        TickSpan span = TickSpan.FromBounds(scale[0].Span.Onset, scale[^1].Span.End);
        _looping = false;
        _alternating = false;
        await Player.SetSourceAsync(CircleSource, _score.WithNotes(scale));
        await Player.SelectSourcesAsync(CircleSource);
        await Player.SetLoopAsync(span);
        _answerLoop = span;
        await Player.SeekAsync(span.Onset);
        if (Player.State == PlaybackState.Stopped)
        {
            await PlayAsync();
        }
    }
}