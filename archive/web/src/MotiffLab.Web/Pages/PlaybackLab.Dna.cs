using Microsoft.AspNetCore.Components;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Piece DNA (0018 section 11), the third place: reached from the score's overflow and from the library card
/// ("&amp;view=dna"), drawn over the score, which stays exactly as it was, selection included. Its map comes from
/// music-core's <see cref="PieceDna"/>; its marks play their passages, and In the score selects one and leaves.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The place to open on arrival: "dna" for Piece DNA.</summary>
    [SupplyParameterFromQuery(Name = "view")]
    public string? View { get; set; }

    /// <summary>«Идеи по смыслу» (#582 item 15): the map's ideas by what they are in the music, an experiment.</summary>
    public const string MusicalIdeasSetting = "experimental.musicalIdeas";

    private PieceDna? _dna;

    /// <summary>What the map's last action did, such as the bookmarks it added.</summary>
    private string? _dnaStatus;

    /// <summary>Whether a passage of the map or the whole map is playing, so leaving stops it.</summary>
    private bool _dnaPlaying;

    /// <summary>The passage a card's Hear started, played once (#582 item 14); null when a mark, a section or the map plays.</summary>
    private TickSpan? _dnaCard;

    /// <summary>The card's passage while it sounds: the engine still loops it and plays; null once it ends, is stopped or another starts.</summary>
    private TickSpan? DnaCardSounding =>
        _dnaPlaying && _dnaCard is { } card && Player.State != PlaybackState.Stopped && Player.Loop == card ? card : null;

    private void OpenDna()
    {
        if (_notes is null || _meter is null || _score is null)
        {
            return;
        }

        _dnaAside = false;
        _dnaStatus = null;
        _dna = BuildDna();
        _popover = Popover.None;
        _pillMenu = PillMenu.None;
    }

    /// <summary>The map of the piece, with the doubt of recognised notes still to check in its chords and keys (#352).</summary>
    private PieceDna BuildDna() => Doubt.On(PieceDna.Build(_notes!, _meter!, _keys, _score!.End, _selection, _selection is null ? null : _reading?.Structure, Words, _pieceStructure, SettingsStore.IsOn(MusicalIdeasSetting)));

    private async Task CloseDnaAsync()
    {
        _dna = null;
        await StopDnaAsync();
    }

    private async Task StopDnaAsync()
    {
        if (!_dnaPlaying)
        {
            return;
        }

        _dnaPlaying = false;
        _dnaCard = null;
        await StopLoopingAsync();
        await Player.PauseAsync();
    }

    /// <summary>A mark or a section: its passage loops until another is chosen, the map's Stop is pressed or the map is left.</summary>
    private Task HearDnaAsync(TickSpan span) => HearDnaAsync(span, once: false);

    /// <summary>A card's Hear: its passage plays once from its start, and the card's button reads Stop until it ends.</summary>
    private Task HearDnaCardAsync(TickSpan span) => HearDnaAsync(span, once: true);

    private async Task HearDnaAsync(TickSpan span, bool once)
    {
        _dnaCard = once ? span : null;
        _looping = false;
        _alternating = false;
        await Player.SelectSourcesAsync(_hear);
        await Player.SetLoopAsync(span, once);
        _answerLoop = span;
        _dnaPlaying = true;
        await Player.SeekAsync(span.Onset);
        if (Player.State == PlaybackState.Stopped)
        {
            await PlayAsync();
        }
    }

    /// <summary>
    /// Plays the piece from its start, the map following, as a listening session; pressed again, it stops. A card's passage
    /// has its own Stop, so while one sounds this starts the map in its place.
    /// </summary>
    private async Task PlayDnaMapAsync()
    {
        if (Player.State != PlaybackState.Stopped && DnaCardSounding is null)
        {
            _dnaPlaying = true;
            await StopDnaAsync();
            return;
        }

        await StopLoopingAsync();
        _dnaCard = null;
        _dnaPlaying = true;
        // The piece from its start: under the playback model a move out of the selection turns Цикл off (T12).
        await Player.MoveAsync(Tick.Zero);
        await FollowModelAsync();
        await PlayAsync();
    }

    /// <summary>In Insights on an idea's card: the map is left, the idea selected and the Insights stream open on it.</summary>
    private async Task ShowDnaInInsightsAsync(TickSpan span)
    {
        await ShowDnaInScoreAsync(span);
        _insightsOpen = true;
        _asking = false;
        _contentsOpen = false;
        _checkOpen = false;
        _performanceOpen = false;
    }

    /// <summary>Add Structure bookmarks on the map: a bookmark for each section of the form, in the Contents drawer.</summary>
    private async Task AddFormBookmarksAsync()
    {
        if (_dna is not { Form.Count: > 0 } dna)
        {
            return;
        }

        int before = Bookmarks.Count;
        await SaveMarksAsync(project => project.AddFormBookmarks(dna.Form, Words, DateTimeOffset.Now), "");
        _dnaStatus = Text.Count("bookmark.structureAdded", Bookmarks.Count - before);
    }

    /// <summary>Leaves the map for the score with the passage selected.</summary>
    private async Task ShowDnaInScoreAsync(TickSpan span)
    {
        await CloseDnaAsync();
        await SelectAsync(span, note: null);
    }
}