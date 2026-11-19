namespace MotiffLab.Web.Pages;

/// <summary>
/// The way back (testing item 20): a place opened from another place (from More, from Piece DNA, from the Insights
/// stream) shows "‹ that place" at its top left, which puts it away and opens that place again as it was; its ✕ puts
/// both away. A place opened straight from the bar has no way back to show, only its ✕.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The places that can be opened from another one.</summary>
    private enum Place
    {
        Contents,
        Check,
        Performance,
        Settings,
        Layers,
        Dna,
        Ask,
    }

    /// <summary>Where a place was opened from, so it can go back there.</summary>
    private enum Origin
    {
        More,
        Dna,
        Insights,
    }

    /// <summary>The place open now that came from another, and that other; null when it was opened from the bar.</summary>
    private (Place Place, Origin From)? _cameFrom;

    /// <summary>Piece DNA put aside, not closed, while the assistant opened from it is open, so going back finds it as it was.</summary>
    private bool _dnaAside;

    /// <summary>Piece DNA on the screen: open and not put aside under the assistant.</summary>
    private bool DnaShown => _dna is not null && !_dnaAside;

    private bool IsOpen(Place place) => place switch
    {
        Place.Contents => _contentsOpen,
        Place.Check => _checkOpen && HasCheck,
        Place.Performance => _performanceOpen,
        Place.Settings => _settingsOpen,
        Place.Layers => _popover == Popover.Layers,
        Place.Dna => DnaShown,
        Place.Ask => _insightsOpen && _asking,
        _ => false,
    };

    /// <summary>Where the place was opened from, while it is open; null when it has no way back to show.</summary>
    private Origin? CameFrom(Place place) => _cameFrom is { } came && came.Place == place && IsOpen(place) ? came.From : null;

    private string OriginName(Origin origin) => origin switch
    {
        Origin.More => Text["bar.more"],
        Origin.Dna => Text["dna.title"],
        _ => Text["bar.insights"],
    };

    /// <summary>The label of the place's way back, such as "More"; null when it was opened from the bar.</summary>
    private string? BackTo(Place place) => CameFrom(place) is { } origin ? OriginName(origin) : null;

    /// <summary>An entry of More: the place opens, and remembers that it came from More.</summary>
    private void FromMore(Place place, Action open)
    {
        open();
        _cameFrom = IsOpen(place) ? (place, Origin.More) : null;
    }

    private async Task FromMoreAsync(Place place, Func<Task> open)
    {
        await open();
        _cameFrom = IsOpen(place) ? (place, Origin.More) : null;
    }

    /// <summary>‹ on a place that came from another: it goes, and the other opens again as it was.</summary>
    private async Task GoBackAsync()
    {
        if (_cameFrom is not { } came)
        {
            return;
        }

        _cameFrom = null;
        await PutAwayAsync(came.Place);
        switch (came.From)
        {
            case Origin.More:
                _popover = Popover.More;
                _pillMenu = PillMenu.None;
                break;
            case Origin.Dna:
                _dnaAside = false;
                break;
            case Origin.Insights:
                _insightsOpen = true;
                _asking = false;
                break;
        }
    }

    /// <summary>✕ on a place: it goes, and so does what it was opened over (Piece DNA under the assistant).</summary>
    private async Task CloseFromAsync(Place place)
    {
        _cameFrom = null;
        await PutAwayAsync(place);
        await DropDnaAsideAsync();
    }

    private async Task PutAwayAsync(Place place)
    {
        switch (place)
        {
            case Place.Contents:
                _contentsOpen = false;
                break;
            case Place.Check:
                _checkOpen = false;
                break;
            case Place.Performance:
                _performanceOpen = false;
                break;
            case Place.Settings:
                _settingsOpen = false;
                break;
            case Place.Layers:
                _popover = Popover.None;
                break;
            case Place.Dna:
                await CloseDnaAsync();
                break;
            case Place.Ask:
                _asking = false;
                _insightsOpen = false;
                break;
        }
    }

    /// <summary>Piece DNA put aside under the assistant goes for good once the assistant does.</summary>
    private async Task DropDnaAsideAsync()
    {
        if (_dnaAside)
        {
            _dnaAside = false;
            await CloseDnaAsync();
        }
    }

    /// <summary>Escape or ‹ in Piece DNA: back to More when it came from there, otherwise back to the score.</summary>
    private Task LeaveDnaAsync() => CameFrom(Place.Dna) is not null ? GoBackAsync() : CloseFromAsync(Place.Dna);
}