using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// «Рядом» and «Сопоставление» while comparing (decision record 0024, section 6; mockups 04 and 14): a drawer under the
/// source on the page with one other source, engraved in its own bars, that follows the page from bar to bar; or, as
/// «Сопоставление», the map of a version against the notes bar by bar, where a bar is tapped to be shown. The page above
/// stays as it was: nothing in the drawer changes the selection, the sound or the notes.
/// </summary>
public partial class PlaybackLab
{
    private bool _besideOpen;
    private bool _besideMap;
    private string? _besideSource;
    private ScoreView? _besideView;

    /// <summary>The source in the drawer: the one chosen, or the notes under a version, or the first version under the notes.</summary>
    private string? BesideSource
    {
        get
        {
            if (_besideSource is { } chosen && chosen != _assemblyShown && AssemblyMusic(chosen) is not null)
            {
                return chosen;
            }

            return _assemblyShown != NotesSource ? NotesSource : _project?.Performances.FirstOrDefault(version => Work?.Source(version.Id) is not null)?.Id;
        }
    }

    /// <summary>The sources the drawer can show: every chip but the one on the page.</summary>
    private IReadOnlyList<Versions.AssemblyChip> BesideChoices => [.. AssemblyChips.Where(chip => chip.Id != _assemblyShown)];

    private ScoreSource? BesideScore => BesideSource is { } id && AssemblyMusic(id) is { } music ? EngravedAndSounded(music, id).Score : null;

    /// <summary>Where the selection on the page lies in the drawer's source: by its match, or, while the carry sheet is open, the fragment carried.</summary>
    private TickSpan? BesidePick
    {
        get
        {
            if (BesideSource is not { } id)
            {
                return null;
            }

            if (_carry is { } carry)
            {
                return id == carry.SourceId ? carry.From : null;
            }

            return AssemblyPick is { } pick ? Mapped(_assemblyShown, id, pick) : null;
        }
    }

    /// <summary>A place in one source as it lies in another, through the notes: a version's place by its match.</summary>
    private TickSpan? Mapped(string from, string to, TickSpan span)
    {
        if (from == to || Work is not { } work)
        {
            return span;
        }

        TickSpan? inNotes = from is NotesSource or BeforeSource ? span : work.Source(from) is not null ? work.Match(from).Similar(span)?.Span : null;
        if (inNotes is not { } notes)
        {
            return null;
        }

        return to is NotesSource or BeforeSource ? notes : work.Source(to) is not null ? work.Match(to).InSource(notes) : null;
    }

    private void ToggleBeside()
    {
        _besideOpen = !_besideOpen;
        _besideMap = false;
        if (_besideOpen)
        {
            // One drawer at a time: the roll steps aside for «Рядом».
            _rollOpen = false;
        }
    }

    private async Task ChooseBesideAsync(string id)
    {
        _besideSource = id;
        _besideMap = false;
        StateHasChanged();
        await FollowBesideAsync();
    }

    private void ToggleBesideMap() => _besideMap = !_besideMap;

    /// <summary>The drawer follows the page: to where the selection lies in its source, or to the same bar as the playhead.</summary>
    private async Task FollowBesideAsync()
    {
        if (!_besideOpen || _besideMap || _besideView is null || BesideSource is not { } id || AssemblyMusic(id) is not { } music || ShownMusic is not { } shown)
        {
            return;
        }

        Tick at = BesidePick is { } pick ? pick.Onset : SameBar(shown, music, AssemblyPick?.Onset ?? Player.Position);
        await Task.Yield();
        await _besideView.ShowTickAsync(at.Value);
    }

    /// <summary>«Сопоставление»: a cell per bar of the version, by how it matches the notes; null for the notes themselves.</summary>
    private IReadOnlyList<MatchBar>? BesideMapBars =>
        _assemblyShown is not NotesSource and not BeforeSource && Work is { } work && work.Source(_assemblyShown) is not null ? work.Match(_assemblyShown).Bars
        : BesideSource is { } id && Work is { } other && other.Source(id) is not null ? other.Match(id).Bars
        : null;

    /// <summary>The version the map is of: the one on the page, else the one in the drawer.</summary>
    private string? BesideMapSource => _assemblyShown is not NotesSource and not BeforeSource ? _assemblyShown : BesideSource;

    /// <summary>A bar of the map tapped: that bar of the version selected on the page.</summary>
    private async Task ShowMapBarAsync(MatchBar bar)
    {
        if (BesideMapSource is not { } id)
        {
            return;
        }

        if (id != _assemblyShown)
        {
            await ShowSourceAsync(id);
        }

        _pickFrom = null;
        await SetPickAsync(id, bar.Span);
        if (_assemblyView is not null)
        {
            await _assemblyView.ShowTickAsync(bar.Span.Onset.Value);
        }
    }

    private static string MapClass(MatchBar bar) => bar.Kind switch
    {
        MatchKind.Same => "same",
        MatchKind.Partly => "partly",
        MatchKind.Differs => "differs",
        _ => "none",
    };

    private string MapLabel(MatchBar bar, MusicCore.Assembly.SourceMusic source) =>
        $"{CarrySheet.BarsText(source, bar.Span.Onset, bar.Span.End, Words)}: {AssemblyMatch.Word(bar.Kind, Words)}";

    private void ForgetBeside()
    {
        _besideOpen = false;
        _besideMap = false;
        _besideSource = null;
    }
}