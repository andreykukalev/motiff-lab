using MotiffLab.MusicCore.Analysis;
using MotiffLab.Web.Playback;

namespace MotiffLab.Web.Reference.Handbook;

/// <summary>Music of the Reference to hear, once or round and round.</summary>
public sealed record ReferencePlay(ReferenceMusic Music, bool Loop);

/// <summary>The piece heard in a key (<paramref name="Hear"/>) or its notes shown in it (#351).</summary>
public sealed record PieceInKey(MusicalKey Key, bool Hear);

/// <summary>Where the host put the music on the player's timeline: from a tick, at the music's tempo over the timeline's.</summary>
public sealed record ReferencePlacement(long At, int MusicTempo, int TimelineTempo);

/// <summary>
/// What the Reference is playing (version 0.5, «Справочник»), shared by its tabs: the host plays the music (on its own in the
/// library, over the piece's timeline in a piece) and says where; this follows the player until the music stops, so a tab
/// shows Pause while its music plays and its notation lights the notes sounding.
/// </summary>
public sealed class ReferenceListening : IDisposable
{
    private readonly Player _player;
    private readonly Func<ReferencePlay, Task<ReferencePlacement?>> _play;
    private readonly Func<Task> _stop;
    private bool _started;
    private bool _starting;

    public ReferenceListening(Player player, Func<ReferencePlay, Task<ReferencePlacement?>> play, Func<Task> stop)
    {
        _player = player;
        _play = play;
        _stop = stop;
        _player.Changed += OnPlayer;
    }

    /// <summary>Raised when what plays changes.</summary>
    public event Action? Changed;

    /// <summary>What is playing, by the name the tab gave it; null when nothing of the Reference plays.</summary>
    public string? Playing { get; private set; }

    /// <summary>Whether the music playing loops.</summary>
    public bool Looping { get; private set; }

    /// <summary>Where the music playing sounds, for its notation; null when nothing plays.</summary>
    public ReferencePlacement? Placement { get; private set; }

    /// <summary>Plays <paramref name="music"/>, named <paramref name="what"/>, in place of whatever the Reference played.</summary>
    public async Task PlayAsync(string what, ReferenceMusic music, bool loop)
    {
        // The music before stops while the new one loads, and the engine may report that stop late: the new music has ended
        // only on a stop after the engine has reported it playing.
        _starting = true;
        Playing = what;
        Looping = loop;
        try
        {
            Placement = await _play(new ReferencePlay(music, loop));
        }
        finally
        {
            _starting = false;
        }

        _started = false;
        if (Placement is null)
        {
            Playing = null;
        }

        Changed?.Invoke();
    }

    /// <summary>Stops the Reference's music.</summary>
    public async Task StopAsync()
    {
        Clear();
        await _stop();
    }

    private void OnPlayer()
    {
        if (Playing is null || _starting)
        {
            return;
        }

        if (_player.State != PlaybackState.Stopped)
        {
            _started = true;
        }
        else if (_started)
        {
            Clear();
        }
    }

    private void Clear()
    {
        Playing = null;
        Placement = null;
        _started = false;
        Changed?.Invoke();
    }

    public void Dispose() => _player.Changed -= OnPlayer;
}