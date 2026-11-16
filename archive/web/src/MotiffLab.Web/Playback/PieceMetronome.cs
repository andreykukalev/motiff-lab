using Microsoft.JSInterop;

using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Playback;

/// <summary>
/// The metronome (decision record 0023; v0.5.5, #521 section 15): one for the score and the PDF view, a thin handle on
/// <c>Scripts/metronome/metronome.ts</c>, which holds its rules. Its button starts and stops it at once, with or without
/// Play; it clicks the piece tempo until the player gives it a tempo of its own, by holding the button and sliding or on
/// the PDF view's pill, kept per piece on the device ("pdf.metronome.{id}", as the iPad keeps it). While the score plays,
/// the score locks it (<see cref="LockAsync"/>): its own clicks go silent and the playback engine clicks the beats heard.
/// </summary>
public sealed class PieceMetronome(IJSRuntime js, Preferences preferences) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private IJSObjectReference? _metronome;
    private DotNetObjectReference<PieceMetronome>? _self;

    public bool Running { get; private set; }

    public int Bpm { get; private set; } = 80;

    public int Beats { get; private set; } = 4;

    /// <summary>The tempo the player gave the metronome for this piece, or null while it follows the piece tempo.</summary>
    public int? Own { get; private set; }

    /// <summary>The beat sounding now, from 0, or -1 when stopped or silent while the score plays.</summary>
    public int Beat { get; private set; } = -1;

    /// <summary>The tempo shown over the button while it is held and slid, or null.</summary>
    public int? Holding { get; private set; }

    /// <summary>The beat's note as the piece's tempo mark writes it: "♩", or "♩." for a dotted quarter.</summary>
    public string Note { get; private set; } = "♩";

    public string? PieceId { get; private set; }

    /// <summary>Anything shown changed: the state, the beat or the held tempo.</summary>
    public event Action? Changed;

    /// <summary>
    /// The piece's metronome as kept, at its piece tempo in beats a minute and its first bar's beats; a view that has neither
    /// (the PDF view) keeps those given last for the same piece.
    /// </summary>
    public async Task LoadAsync(string? pieceId, int? pieceBpm = null, string? note = null, int? barBeats = null)
    {
        string? kept = pieceId is null ? null : await preferences.GetAsync(Key(pieceId));
        IJSObjectReference metronome = await MetronomeAsync();
        if (pieceId is null || pieceId != PieceId)
        {
            Note = "♩";
        }

        Note = note ?? Note;
        PieceId = pieceId;
        Take(await metronome.InvokeAsync<MetronomeState>("load", pieceId, kept, pieceBpm, barBeats));
    }

    public async Task ToggleAsync() => Take(await (await MetronomeAsync()).InvokeAsync<MetronomeState>("toggle"));

    public async Task StopAsync()
    {
        if (_metronome is not null && Running)
        {
            Take(await _metronome.InvokeAsync<MetronomeState>("stop"));
        }
    }

    /// <summary>A new tempo or beats to the bar, heard from the next click and kept for the piece.</summary>
    public async Task ChangeAsync(int bpm, int beats) => await KeepAsync(await (await MetronomeAsync()).InvokeAsync<MetronomeState>("change", bpm, beats));

    /// <summary>A tap on the tempo sets it from the last few taps.</summary>
    public async Task TapAsync()
    {
        if (await (await MetronomeAsync()).InvokeAsync<MetronomeState?>("tap") is { } tapped)
        {
            await KeepAsync(tapped);
        }
    }

    /// <summary>Silent while the score plays, its own clicks again from the first beat of a bar once it stops.</summary>
    public async Task LockAsync(bool playing)
    {
        if (_metronome is not null)
        {
            await _metronome.InvokeVoidAsync("lock", playing);
        }
    }

    /// <summary>The beat that sounds now, from 0, or -1 when stopped.</summary>
    [JSInvokable]
    public void OnMetronomeBeat(int beat)
    {
        Beat = beat;
        Changed?.Invoke();
    }

    /// <summary>The button is held and slid to this tempo, or let go elsewhere (null).</summary>
    [JSInvokable]
    public void OnMetronomeHold(int? bpm)
    {
        Holding = bpm;
        Changed?.Invoke();
    }

    /// <summary>The button let go after a slide: the tempo is kept for the piece.</summary>
    [JSInvokable]
    public async Task OnMetronomeHeld(int bpm)
    {
        Holding = null;
        if (bpm != Bpm)
        {
            await ChangeAsync(bpm, Beats);
        }
        else
        {
            Changed?.Invoke();
        }
    }

    private static string Key(string pieceId) => $"pdf.metronome.{pieceId}";

    private async Task KeepAsync(MetronomeState state)
    {
        Take(state);
        if (PieceId is { } id && state.Kept is { } kept)
        {
            await preferences.SetAsync(Key(id), kept);
        }
    }

    private void Take(MetronomeState state)
    {
        (Running, Bpm, Beats, Own) = (state.Running, state.Bpm, state.Beats, state.Own);
        if (!Running)
        {
            Beat = -1;
        }

        Changed?.Invoke();
    }

    private async Task<IJSObjectReference> MetronomeAsync()
    {
        if (_metronome is null)
        {
            _self = DotNetObjectReference.Create(this);
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/metronome/metronome.js");
            _metronome = await _module.InvokeAsync<IJSObjectReference>("createMetronome", _self);
            await _module.InvokeVoidAsync("holdMetronome", _self);
        }

        return _metronome;
    }

    /// <summary>Loads the module, so the buttons can be held before the metronome first sounds.</summary>
    public async Task ReadyAsync() => await MetronomeAsync();

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_metronome is not null)
            {
                await _metronome.InvokeAsync<MetronomeState>("stop");
                await _metronome.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }

        _self?.Dispose();
    }

    private sealed record MetronomeState(bool Running, int Bpm, int Beats, int? Own, string? Kept);
}