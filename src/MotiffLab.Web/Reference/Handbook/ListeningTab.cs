using Microsoft.AspNetCore.Components;

using MotiffLab.MusicCore;
using MotiffLab.MusicCore.Analysis;
using MotiffLab.Web.Localization;

namespace MotiffLab.Web.Reference.Handbook;

/// <summary>
/// A tab of the Reference that plays its music (version 0.5): «Аккорды и гаммы» and the explorer. Play and Pause, the loop,
/// and a change heard at once while the music plays are the same in both; each tab says what its music is.
/// </summary>
public abstract class ListeningTab : ComponentBase, IDisposable
{
    [Inject]
    protected UiText Text { get; set; } = null!;

    [Parameter, EditorRequired]
    public ReferenceListening Listening { get; set; } = null!;

    /// <summary>The name the tab's music plays under, so a tab knows its own music from another's.</summary>
    protected abstract string Name { get; }

    /// <summary>The tab's music as its choices give it.</summary>
    protected abstract ReferenceMusic Music { get; }

    /// <summary>Whether the tab's music plays round and round.</summary>
    protected abstract bool Loop { get; set; }

    protected Words Words => Words.For(Text.Language);

    protected bool Playing => Listening.Playing == Name;

    /// <summary>Where the tab's music sounds while it plays, for its notation; null when it does not.</summary>
    protected ReferencePlacement? Placement => Playing ? Listening.Placement : null;

    /// <summary>Root position by its name, each inversion by its figures, or by its number where it has none.</summary>
    protected string PositionName(ChordPosition position) =>
        position.Inversion == 0 ? Words.Capitalize(position.Title) : position.Figure ?? Text.Format("reference.inversionNumber", position.Inversion);

    protected override void OnInitialized()
    {
        Listening.Changed += OnListening;
        Text.Changed += OnListening;
    }

    protected override void OnParametersSet() => Sound();

    /// <summary>Asks music-core for what the choices give.</summary>
    protected abstract void Sound();

    /// <summary>A new choice sounds at once when the music was playing, else it waits for Play.</summary>
    protected async Task SetAsync(Action change)
    {
        change();
        Sound();
        if (Playing)
        {
            await Listening.PlayAsync(Name, Music, Loop);
        }

        StateHasChanged();
    }

    protected Task PlayAsync() => Playing ? Listening.StopAsync() : Listening.PlayAsync(Name, Music, Loop);

    protected async Task LoopAsync()
    {
        Loop = !Loop;
        if (Playing)
        {
            await Listening.PlayAsync(Name, Music, Loop);
        }
    }

    private void OnListening()
    {
        Sound();
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        Listening.Changed -= OnListening;
        Text.Changed -= OnListening;
        GC.SuppressFinalize(this);
    }
}