using Microsoft.JSInterop;

using MotiffLab.MusicCore.Time;

namespace MotiffLab.Web.Playback;

/// <summary>
/// The playback model of version 0.5.2 (#495 UX-10, the architect's design playback-model-v0.5.2, slice W1; the experiment
/// Two playheads and modes): one position in the engine, two playheads on screen, one Play button and the selection's Цикл.
/// The script's player owns the model (<c>Scripts/playback/playback-owner.ts</c>, the rules in <c>playback-model.ts</c> that
/// the iPad runs too): the page sends it the events and shows what it reports back, the loop, the colour of Play and the
/// places that do not move with the position.
/// </summary>
public sealed partial class Player
{
    /// <summary>Whether the model holds: the page told it so (the experiment on, not in focus).</summary>
    public bool ModelHeld { get; private set; }

    /// <summary>Цикл: the selection loops while it is on; it turns on by itself with a new selection.</summary>
    public bool SelectionLoop { get; private set; } = true;

    /// <summary>The colour of the Play button: playing, where the sound is; stopped, what Play will play.</summary>
    public PlaybackContext Context { get; private set; } = PlaybackContext.Piece;

    /// <summary>Whether the position lies inside the selection.</summary>
    public bool Inside { get; private set; }

    /// <summary>The blue playhead while it waits outside the selection (at its start or its end); null inside it or without one.</summary>
    public Tick? Waits { get; private set; }

    /// <summary>The place Play names when it is not the position: stopped with Цикл on outside the selection, its start.</summary>
    public Tick? PlayFrom { get; private set; }

    /// <summary>The model holds or lets go; holding, it reports where it stands.</summary>
    public async Task HoldModelAsync(bool on)
    {
        IJSObjectReference engine = await EngineAsync();
        ModelHeld = on;
        await engine.InvokeVoidAsync("holdModel", on);
    }

    /// <summary>The selection as the range it plays, or none (T9–T11): the engine loops it while Цикл is on; the position stays.</summary>
    public async Task SetSelectionAsync(TickSpan? range)
    {
        IJSObjectReference engine = await EngineAsync();
        Follow(await engine.InvokeAsync<long[]?>("setSelection", range?.Onset.Value, range?.End.Value));
    }

    /// <summary>The one Play button, Space or a pedal (T1–T3); completes once the engine plays when it was told to.</summary>
    public async Task PressAsync()
    {
        IJSObjectReference engine = await EngineAsync();
        Follow(await engine.InvokeAsync<long[]?>("press"));
    }

    /// <summary>Цикл on (T7: the position goes to the selection's start) or off (T8: playback goes on past its end).</summary>
    public async Task SetSelectionLoopAsync(bool on)
    {
        IJSObjectReference engine = await EngineAsync();
        Follow(await engine.InvokeAsync<long[]?>("setSelectionLoop", on));
    }

    /// <summary>
    /// The player moves the position by a tap (T12): while the model holds, out of the selection Цикл turns off first; else
    /// a plain seek.
    /// </summary>
    public async Task MoveAsync(Tick at)
    {
        if (!ModelHeld)
        {
            await SeekAsync(at);
            return;
        }

        IJSObjectReference engine = await EngineAsync();
        Position = at;
        Follow(await engine.InvokeAsync<long[]?>("moveTo", at.Value));
        Changed?.Invoke();
    }

    /// <summary>The engine loops the model's passage again, once another owner of the loop (a scale, the answer) has ended.</summary>
    public async Task HoldLoopAsync()
    {
        IJSObjectReference engine = await EngineAsync();
        Follow(await engine.InvokeAsync<long[]?>("holdLoop"));
        Changed?.Invoke();
    }

    /// <summary>Called by the script when what the model shows changes: Цикл, the colour of Play and the places that stand.</summary>
    [JSInvokable]
    public void OnPlaybackModel(bool loop, string context, bool inside, double? waits, double? at)
    {
        SelectionLoop = loop;
        Context = context == "selection" ? PlaybackContext.Selection : PlaybackContext.Piece;
        Inside = inside;
        Waits = waits is { } tick ? new Tick((long)Math.Round(tick)) : null;
        PlayFrom = at is { } from ? new Tick((long)Math.Round(from)) : null;
        Changed?.Invoke();
    }

    /// <summary>The engine's loop after an event of the model: null untouched, empty none, else its start and end.</summary>
    private void Follow(long[]? loop)
    {
        if (loop is null)
        {
            return;
        }

        EndAlternation();
        Loop = loop.Length == 2 ? TickSpan.FromBounds(new Tick(loop[0]), new Tick(loop[1])) : null;
    }
}

/// <summary>What the Play button stands for: the selection (cobalt) or the piece (ink).</summary>
public enum PlaybackContext
{
    Piece,

    Selection,
}