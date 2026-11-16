using MotiffLab.MusicCore.Playback;
using MotiffLab.Web.Playback;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The pill's metronome (v0.5.5, #521 section 15): the one <see cref="PieceMetronome"/> of the score and the PDF view. Its
/// button clicks at once, with or without Play, at the piece tempo (the score's mark, never times the playback pace) until
/// the player holds it and slides to a tempo of their own. While the music plays, its own clicks go silent and the engine
/// clicks the beats heard, so they never drift apart at another pace; once it stops the metronome clicks on its own again.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>What the metronome was last given: the piece, its tempo, the beat's note and the first bar's beats.</summary>
    private (string? Piece, int Bpm, string Note, int? Beats)? _metronomeGiven;

    private bool _metronomeLocked;

    private Task ToggleMetronomeAsync() => Metronome.ToggleAsync();

    private void OnMetronomeChanged() => _ = InvokeAsync(StateHasChanged);

    /// <summary>After each render: the metronome follows the piece tempo, keeps silent while the music plays, and the engine clicks while it runs.</summary>
    private async Task SyncMetronomeAsync()
    {
        if (TempoNow is { } tempo && _score is { } score)
        {
            int numerator = score.Bars.Count > 0 ? score.Bars[0].BeatsPerBar : 0;
            // The time signature's numerator counts eighths in 6/8, where the metronome clicks dotted quarters.
            int? beats = numerator <= 0 ? null : tempo.Beat == TempoBeat.DottedQuarter && numerator % 3 == 0 ? numerator / 3 : numerator;
            var given = (Piece: _project?.Id, Bpm: tempo.PlayedPerMinute, Note: tempo.Note, Beats: beats);
            if (_metronomeGiven != given)
            {
                _metronomeGiven = given;
                await Metronome.LoadAsync(given.Piece, given.Bpm, given.Note, given.Beats);
            }
        }

        bool playing = Player.State == PlaybackState.Playing;
        if (playing != _metronomeLocked)
        {
            _metronomeLocked = playing;
            await Metronome.LockAsync(playing);
        }

        if (Player.Metronome != Metronome.Running)
        {
            await Player.SetMetronomeAsync(Metronome.Running);
        }
    }
}