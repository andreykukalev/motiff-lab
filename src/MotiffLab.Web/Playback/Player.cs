using Microsoft.JSInterop;

using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.Web.Playback;

/// <summary>
/// Browser playback (S1-W4, S1-W5): a thin C# handle on the TypeScript engine in
/// <c>Scripts/playback</c>. Scores go across whole, and timing stays in the audio thread
/// (decision records 0001 and 0006); the engine reports the position back about 20 times a second.
/// </summary>
/// <remarks>
/// A player holds one timeline (tempo map, beats, bars) and any number of sources that share it,
/// such as the original motif and a variation. Selecting sources, for A/B or layered listening,
/// keeps the musical position.
/// </remarks>
public sealed class Player(IJSRuntime js) : IAsyncDisposable
{
    public const string OriginalSource = "original";

    public const double MinTempo = 0.25;

    public const double MaxTempo = 1.5;

    private const string PianoManifest = "audio/piano/piano.json";

    private IJSObjectReference? _module;
    private IJSObjectReference? _engine;
    private DotNetObjectReference<Player>? _self;
    private Task? _initializing;
    private PlaybackScore? _timeline;

    /// <summary>Raised on the renderer's thread after the position or state changes.</summary>
    public event Action? Changed;

    public bool IsReady => _engine is not null;

    /// <summary>Why the engine could not start, for example a browser without AudioWorklet.</summary>
    public string? Error { get; private set; }

    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    /// <summary>The position being heard, as last reported by the engine.</summary>
    public Tick Position { get; private set; }

    public double Tempo { get; private set; } = 1;

    public TickSpan? Loop { get; private set; }

    public bool Metronome { get; private set; }

    public bool CountIn { get; private set; }

    public IReadOnlySet<int> Muted { get; private set; } = new HashSet<int>();

    public IReadOnlySet<int> Soloed { get; private set; } = new HashSet<int>();

    public IReadOnlyList<string> ActiveSources { get; private set; } = [];

    /// <summary>The passes of an alternating loop (<see cref="AlternateAsync"/>), or null when the engine is not alternating.</summary>
    public IReadOnlyList<LoopPass>? Passes { get; private set; }

    /// <summary>The pass of the alternating loop being heard, from 0, as last reported by the engine; -1 when not alternating.</summary>
    public int Pass { get; private set; } = -1;

    /// <summary>The script-side player, for views that read the position every frame (the score view); null until ready.</summary>
    internal IJSObjectReference? Engine => _engine;

    /// <summary>Creates the audio engine and loads the piano. Safe to call more than once.</summary>
    public Task InitializeAsync() => _initializing ??= InitializeCoreAsync();

    /// <summary>Loads a piece: its timeline, and its notes as the original source, which becomes the only active one. Playback stops at the start.</summary>
    public async Task LoadAsync(PlaybackScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        IJSObjectReference engine = await EngineAsync();

        _timeline = score;
        await engine.InvokeVoidAsync("setTimeline", PlaybackPayload.Timeline(score));
        await engine.InvokeVoidAsync("setSource", OriginalSource, PlaybackPayload.Notes(score));
        await SelectSourcesAsync(OriginalSource);
        Loop = null;
        Position = Tick.Zero;
        State = PlaybackState.Stopped;
        Changed?.Invoke();
    }

    /// <summary>Adds or replaces a source on the loaded timeline, for example a variation (S1-W5). It plays once selected.</summary>
    public async Task SetSourceAsync(string id, PlaybackScore score)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(score);
        if (_timeline is null)
        {
            throw new InvalidOperationException("Load a piece before adding sources to it.");
        }

        IJSObjectReference engine = await EngineAsync();
        await engine.InvokeVoidAsync("setSource", id, PlaybackPayload.Notes(score));
    }

    /// <summary>Plays the given sources together: one for A/B switching, several for layering. The musical position is kept.</summary>
    public async Task SelectSourcesAsync(params string[] ids)
    {
        IJSObjectReference engine = await EngineAsync();
        ActiveSources = ids;
        EndAlternation();
        await engine.InvokeVoidAsync("setActiveSources", (object)ids);
        Changed?.Invoke();
    }

    /// <summary>Starts playback; completes once the engine has started, so the page can render after without delaying it.</summary>
    public async Task PlayAsync()
    {
        IJSObjectReference engine = await EngineAsync();
        await engine.InvokeVoidAsync("play");
    }

    public async Task PauseAsync()
    {
        IJSObjectReference engine = await EngineAsync();
        await engine.InvokeVoidAsync("pause");
    }

    public async Task SeekAsync(Tick position)
    {
        IJSObjectReference engine = await EngineAsync();
        Position = position;
        await engine.InvokeVoidAsync("seek", position.Value);
        Changed?.Invoke();
    }

    /// <summary>Plays at a share of the written tempo, from 25% to 150%.</summary>
    public async Task SetTempoAsync(double tempo)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tempo, MinTempo);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tempo, MaxTempo);
        IJSObjectReference engine = await EngineAsync();
        Tempo = tempo;
        await engine.InvokeVoidAsync("setRate", tempo);
        Changed?.Invoke();
    }

    /// <summary>Loops a range, such as the selected motif, or stops looping with null.</summary>
    public async Task SetLoopAsync(TickSpan? range)
    {
        IJSObjectReference engine = await EngineAsync();
        Loop = range;
        EndAlternation();
        if (range is { } span)
        {
            await engine.InvokeVoidAsync("setLoop", span.Onset.Value, span.End.Value);
        }
        else
        {
            await engine.InvokeVoidAsync("clearLoop");
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Loops the passes in turn, from the start of the first (#43): each plays its sources over its
    /// range, and the next starts on the frame the range ends, so the switch is sample-accurate and
    /// never depends on the page. <see cref="Pass"/>, <see cref="ActiveSources"/> and <see cref="Loop"/>
    /// follow the pass the engine reports. Choosing sources or a loop ends it.
    /// </summary>
    public async Task AlternateAsync(IReadOnlyList<LoopPass> passes)
    {
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentOutOfRangeException.ThrowIfZero(passes.Count);
        IJSObjectReference engine = await EngineAsync();
        Passes = passes;
        Pass = 0;
        ActiveSources = passes[0].Sources;
        Loop = passes[0].Range;
        Position = passes[0].Range.Onset;
        await engine.InvokeVoidAsync("setAlternation", (object)passes.Select(pass => new { ids = pass.Sources, start = pass.Range.Onset.Value, end = pass.Range.End.Value }).ToArray());
        Changed?.Invoke();
    }

    public async Task SetMetronomeAsync(bool on)
    {
        IJSObjectReference engine = await EngineAsync();
        Metronome = on;
        await engine.InvokeVoidAsync("setMetronome", on);
        Changed?.Invoke();
    }

    /// <summary>When on, play starts after one bar of clicks in the meter and tempo where playback starts.</summary>
    public async Task SetCountInAsync(bool on)
    {
        IJSObjectReference engine = await EngineAsync();
        CountIn = on;
        await engine.InvokeVoidAsync("setCountIn", on ? 1 : 0);
        Changed?.Invoke();
    }

    /// <summary>
    /// Mutes and solos tracks. A track sounds when it is not muted and, if any track is soloed,
    /// it is one of them.
    /// </summary>
    public async Task SetTracksAsync(IEnumerable<int> muted, IEnumerable<int> soloed)
    {
        IJSObjectReference engine = await EngineAsync();
        Muted = muted.ToHashSet();
        Soloed = soloed.ToHashSet();
        await engine.InvokeVoidAsync("setTracks", Muted.Order().ToArray(), Soloed.Order().ToArray());
        Changed?.Invoke();
    }

    /// <summary>Called by the engine with the position being heard, the transport state and the pass of an alternating loop (-1 for none).</summary>
    [JSInvokable]
    public void OnPlaybackPosition(double tick, string state, int pass)
    {
        // A report from before the page started or ended the alternation names no pass of it; it is ignored.
        if (Passes is { } passes && pass >= 0 && pass < passes.Count && pass != Pass)
        {
            Pass = pass;
            ActiveSources = passes[pass].Sources;
            Loop = passes[pass].Range;
        }

        Position = new Tick(Math.Max(0, (long)Math.Round(tick)));
        State = state switch
        {
            "playing" => PlaybackState.Playing,
            "countingIn" => PlaybackState.CountingIn,
            _ => PlaybackState.Stopped,
        };
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_engine is not null)
            {
                await _engine.InvokeVoidAsync("dispose");
                await _engine.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The page is going away; the browser closes the audio context with it.
        }

        _self?.Dispose();
    }

    private void EndAlternation()
    {
        Passes = null;
        Pass = -1;
    }

    private async Task<IJSObjectReference> EngineAsync()
    {
        await InitializeAsync();
        return _engine ?? throw new InvalidOperationException(Error ?? "Playback is not available.");
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            _self = DotNetObjectReference.Create(this);
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/playback/playback.js");
            _engine = await _module.InvokeAsync<IJSObjectReference>("createPlayer", _self, PianoManifest);
            Error = null;
        }
        catch (JSException exception)
        {
            Error = $"Playback could not start in this browser: {exception.Message}";
        }

        Changed?.Invoke();
    }
}

/// <summary>One pass of an alternating loop: the sources that play and the range they loop over.</summary>
public sealed record LoopPass(IReadOnlyList<string> Sources, TickSpan Range);

public enum PlaybackState
{
    Stopped,

    /// <summary>Playing the count-in bar before the music starts.</summary>
    CountingIn,

    Playing,
}