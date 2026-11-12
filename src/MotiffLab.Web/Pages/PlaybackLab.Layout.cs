using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;
using MotiffLab.Web.Roll;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The score page (decision record 0018, sections 1 to 3): the score fills the screen and everything else
/// floats over it. The bar with its page count and five actions, which fades while the player reads
/// (<c>Scripts/layout/bar-fade.ts</c>); the Phrases popover with the level (Short, Phrases, Sentences),
/// the brackets on or off and Reset to detected; the piano roll drawer and the Insights sheet, which leave
/// the selection as it is; and the transport pill, shown with a selection or while playing. The phrases
/// are read at three levels from one analysis (<see cref="PhraseLevels"/>); the level is kept with the
/// piece as its "phrases.granularity" setting, and the edits of every level with the project.
/// </summary>
public partial class PlaybackLab : IAsyncDisposable
{
    /// <summary>The device preference for showing the phrase brackets (the setting "guidance.brackets").</summary>
    private const string BracketsPreference = "guidance.brackets";

    /// <summary>The tempo steps the pill offers, as a share of the written tempo.</summary>
    private static readonly int[] s_tempoSteps = [50, 60, 75, 90, 100];

    /// <summary>The bar's small overlays; one at a time.</summary>
    private enum Popover
    {
        None,
        Phrases,
        More,
        Layers,

        /// <summary>The piano roll's own settings, from a long press on its action or its handle.</summary>
        Roll,
    }

    /// <summary>The pill's small overlays: the tempo and which notes play.</summary>
    private enum PillMenu
    {
        None,
        Tempo,
        Hands,

        /// <summary>The pill's own settings, from a long press on it.</summary>
        Settings,
    }

    /// <summary>
    /// The structure of the piece (#347), read once from music-core: the phrase levels the brackets show, the sections of
    /// the form the Contents and Piece DNA show, and the closes and motifs beside them.
    /// </summary>
    private PieceStructure? _pieceStructure;

    /// <summary>The piece at the three levels; its <see cref="PhraseLevels.Phrases"/> and <see cref="PhraseLevels.Sentences"/> are <see cref="_phrases"/> and <see cref="_sentences"/>.</summary>
    private PhraseLevels? Levels => _pieceStructure?.Levels;

    /// <summary>The level the brackets show and a tap selects at.</summary>
    private PhraseLevel _level = PhraseLevel.Phrase;

    private bool _showBrackets = true;
    private Popover _popover;
    private PillMenu _pillMenu;
    private bool _rollOpen;
    private bool _insightsOpen;
    private bool _penOn;
    private (int First, int Last, int Count) _pages;

    private ScoreView? _scoreView;
    private ElementReference _main;
    private ElementReference _bar;
    private IJSObjectReference? _chromeModule;
    private IJSObjectReference? _barFade;
    private IJSObjectReference? _phraseKeys;
    private IJSObjectReference? _drawerResize;
    private IJSObjectReference? _longPress;
    private DotNetObjectReference<PlaybackLab>? _self;
    private bool _attaching;

    private string WorkspaceClass => string.Join(' ', new[]
    {
        _rollOpen && !FocusShowsRoll ? "roll-open" : null,
        _assembling && _besideOpen ? "beside-open" : null,
        WaveShown ? "wave-open" : null,
        _insightsOpen || _checkOpen || _performanceOpen || _carry is not null || _journalOpen ? "insights-open" : null,
        _contentsOpen ? "contents-open" : null,
        _bookmarkDraft is { Id: null } || _noteDraft is not null ? "bookmarking" : null,
        Focused ? "focused" : null,
        _popover != Popover.None || _settingsOpen ? "popover-open" : null,
    }.Where(name => name is not null));

    /// <summary>
    /// The pill shows with a selection or while playback runs, or only while it runs (the setting "guidance.transportPill", 0018 section 10).
    /// Once playback has run it stays after a pause or the end of the piece, so tempo, metronome and count-in can be set before playing
    /// again, until a tap on the page where there is no music puts it away.
    /// </summary>
    private bool PillShown => ((_selection is not null || _pillKept) && SettingsStore["guidance.transportPill"] != "playing") || Player.State != PlaybackState.Stopped || _pillMenu != PillMenu.None;

    /// <summary>Playback has run since the page was last tapped where there is no music: the pill stays.</summary>
    private bool _pillKept;

    /// <summary>"p. 2 of 4", or "pp. 1–2 of 4" with two pages to a row; null in the strip or before the score is engraved.</summary>
    private string? PageText => _pages switch
    {
        (0, _, _) or (_, _, 0) => null,
        var (first, last, count) when first == last => Text.Format("bar.page", first, count),
        var (first, last, count) => Text.Format("bar.pages", first, last, count),
    };

    /// <summary>Whether the score scrolls with playback (Settings · Score); the playhead shows either way.</summary>
    private const string FollowSetting = "playback.follow";

    /// <summary>Even bars (testing item 35): the bars take room by their length, so the playhead keeps one pace.</summary>
    private const string EvenBarsSetting = "score.evenBars";

    /// <summary>Where the bar the playhead is in starts while it is out of sight during playback; null while it is in sight.</summary>
    private Tick? _playheadAway;

    private void OnPlayheadAway(Tick? barStart) => _playheadAway = barStart;

    /// <summary>The chip "Back to playback": the playing system scrolls into view and the score follows it again.</summary>
    private async Task ReturnToPlayheadAsync()
    {
        _playheadAway = null;
        if (_scoreView is not null)
        {
            await _scoreView.ReturnToPlayheadAsync();
        }
    }

    /// <summary>Reads the player's settings on this device: whether the brackets show, and the rest as the page asks for them.</summary>
    private async Task ReadLayoutPreferencesAsync()
    {
        await SettingsStore.LoadAsync();
        _showBrackets = SettingsStore.IsOn(BracketsPreference);
    }

    /// <summary>A setting changed, in the Settings sheet over the score or by a control on the page: the score shows it at once.</summary>
    private void OnSettingsChanged() => _ = InvokeAsync(async () =>
    {
        _showBrackets = SettingsStore.IsOn(BracketsPreference);
        ReadingMarksChanged();
        // A piece without its own level opens at the setting's.
        if (Levels is not null && _project?.SettingOverrides.ContainsKey(GranularitySetting) != true)
        {
            _level = ProjectLevel;
        }

        if (_barFade is not null)
        {
            try
            {
                await _barFade.InvokeVoidAsync("setDelay", BarFadeMilliseconds);
            }
            catch (JSException)
            {
            }
        }

        StateHasChanged();
    });

    /// <summary>How long the bar stays after the last touch (the setting "guidance.barFade", in seconds).</summary>
    private int BarFadeMilliseconds => int.TryParse(SettingsStore["guidance.barFade"], out int seconds) ? seconds * 1000 : 2000;

    private bool _settingsOpen;

    /// <summary>The section the Settings sheet opens at: an element's popover leads to its own rows.</summary>
    private string? _settingsSection;

    private void OpenSettings() => OpenSettings(null);

    private void OpenSettings(string? section)
    {
        _settingsSection = section;
        _settingsOpen = true;
        _popover = Popover.None;
        _pillMenu = PillMenu.None;
    }

    /// <summary>
    /// A long press on an element (Scripts/layout/long-press.ts, 0018 section 10) opens its own settings: the pill's,
    /// the Phrases popover from its action, the piano roll's from its action or its handle.
    /// </summary>
    [JSInvokable]
    public Task OnLongPress(string name)
    {
        switch (name)
        {
            case "pill":
                _popover = Popover.None;
                _pillMenu = PillMenu.Settings;
                break;
            case "phrases":
                _pillMenu = PillMenu.None;
                _popover = Popover.Phrases;
                break;
            case "roll":
                _pillMenu = PillMenu.None;
                _popover = Popover.Roll;
                break;
            default:
                return Task.CompletedTask;
        }

        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>Whether the piece keeps its own value for any of these settings, so the popover's scope reads This piece.</summary>
    private bool PieceScoped(IEnumerable<string> ids) => _project is { } project && ids.Any(project.SettingOverrides.ContainsKey);

    /// <summary>
    /// The scope of a popover's settings (0018 section 10): This piece keeps the values shown with the piece; Everywhere
    /// lets the piece follow the Settings sheet again, and later changes made here go there.
    /// </summary>
    private async Task SetScopeAsync(IReadOnlyList<string> ids, bool piece)
    {
        if (_project is not { } project || PieceScoped(ids) == piece)
        {
            return;
        }

        foreach (string id in ids)
        {
            project = project.OverrideSetting(id, piece ? project.SettingOverrides.GetValueOrDefault(id) ?? SettingsStore[id] : null);
        }

        _project = project;
        CurrentPiece.Saved(project);
        await Projects.SaveAsync(project);
        if (Levels is not null)
        {
            _level = ProjectLevel;
        }
    }

    /// <summary>The piece's own choices go (0018 section 10): the settings of the sheet hold for it too.</summary>
    private async Task ClearPieceSettingsAsync()
    {
        if (_project is not { SettingOverrides.Count: > 0 } project)
        {
            return;
        }

        foreach (string id in project.SettingOverrides.Keys.ToList())
        {
            project = project.OverrideSetting(id, null);
        }

        _project = project;
        CurrentPiece.Saved(project);
        await Projects.SaveAsync(project);
        if (Levels is not null)
        {
            _level = ProjectLevel;
        }
    }

    /// <summary>
    /// Reads the piece at the three levels with the player's edits, and the level the piece is shown at: the
    /// project's own choice, or the setting's default.
    /// </summary>
    private void SuggestPhrases(LevelEdits? edits = null)
    {
        LevelEdits? with = edits ?? _levelsToRead ?? Levels?.Edits;
        _levelsToRead = null;
        PhraseLevels levels = PhraseLevels.Suggest(_notes!, _meter!, _phraseMarks, _score!.End, KeySignature.InForce(_keys, Tick.Zero), Words, with);
        _pieceStructure = PieceStructure.Of(_notes!, _meter!, levels, Words);
        _phrases = levels.Phrases;
        _sentences = levels.Sentences;
    }

    /// <summary>The level the project keeps for the piece, or the setting's default.</summary>
    private PhraseLevel ProjectLevel => PhraseLevels.Parse(_project?.SettingOverrides.GetValueOrDefault(GranularitySetting)) ?? DefaultLevel;

    /// <summary>The level a piece opens at until the player chooses one for it: the setting, Phrases unless changed.</summary>
    private PhraseLevel DefaultLevel => PhraseLevels.Parse(SettingsStore[GranularitySetting]) ?? PhraseLevel.Phrase;

    private const string GranularitySetting = "phrases.granularity";

    private string LevelName(PhraseLevel level) => Text[level switch
    {
        PhraseLevel.Short => "phrases.levelShort",
        PhraseLevel.Sentence => "phrases.levelSentences",
        _ => "phrases.levelPhrases",
    }];

    /// <summary>"Short phrase", "Phrase" or "Sentence" in the player's language, as a bracket is named.</summary>
    private string UnitName(PhraseLevel level)
    {
        string name = PhraseLevels.Describe(level, Words);
        return name.Length == 0 ? name : char.ToUpper(name[0], Text.Culture) + name[1..];
    }

    /// <summary>How many units the piece has at the level shown, as the popover says it.</summary>
    private string LevelCount => Levels is null ? "" : Text.Count(_level switch
    {
        PhraseLevel.Short => "phrases.countShort",
        PhraseLevel.Sentence => "phrases.countSentences",
        _ => "phrases.countPhrases",
    }, Levels.Units(_level).Count);

    /// <summary>The unit of the level shown that the selection is (<see cref="SelectedUnit"/>): its bracket is drawn in the accent.</summary>
    private PhraseUnit? CurrentUnit => SelectedUnit;

    /// <summary>The brackets of the level shown, the selected one in the accent and a dot on those the player kept ideas from.</summary>
    private IReadOnlyList<ScorePhrase>? ScorePhrases
    {
        get
        {
            if (Levels is null || !_showBrackets)
            {
                return null;
            }

            PhraseUnit? current = CurrentUnit;
            IReadOnlyList<Observation> kept = Kept;
            if (!ReferenceEquals(Levels, _scorePhrasesFor) || _level != _scorePhrasesLevel || current != _scorePhrasesCurrent || !ReferenceEquals(kept, _scorePhrasesKept))
            {
                (_scorePhrasesFor, _scorePhrasesLevel, _scorePhrasesCurrent, _scorePhrasesKept) = (Levels, _level, current, kept);
                _scorePhrases = [.. Levels.Units(_level).Select(unit =>
                {
                    int ideas = kept.Count(observation => observation.IsAbout(unit.Span));
                    string label = Text.Format("phrases.bracketLabel", UnitName(unit.Level), unit.Number, Bars(unit));
                    return new ScorePhrase(
                        unit.Span.Onset.Value,
                        unit.Span.End.Value,
                        unit.Number,
                        KindClass(unit),
                        unit == current,
                        ideas > 0,
                        ideas == 0 ? label : label + ", " + Text.Format("phrases.keptShort", ideas));
                })];
            }

            return _scorePhrases;
        }
    }

    private PhraseLevels? _scorePhrasesFor;
    private PhraseLevel _scorePhrasesLevel;
    private PhraseUnit? _scorePhrasesCurrent;
    private IReadOnlyList<Observation>? _scorePhrasesKept;
    private IReadOnlyList<ScorePhrase>? _scorePhrases;

    /// <summary>How a unit's start was found, as the bracket's class: the start of the piece, the player's own, inferred, or imported from the file.</summary>
    private static string KindClass(PhraseUnit unit) => unit.Number == 1 ? "start" : unit.Start.Provenance switch
    {
        Provenance.UserEdited or Provenance.UserConfirmed => "yours",
        Provenance.Inferred or Provenance.Detected => "inferred",
        _ => "imported",
    };

    private string Bars(PhraseUnit unit) =>
        unit.FirstBar == unit.LastBar ? Text.Format("bars.one", unit.FirstBar) : Text.Format("bars.range", unit.FirstBar, unit.LastBar);

    /// <summary>A tap on a bracket selects its unit; a tap on the selected one lifts it into focus (0018 section 4).</summary>
    private Task OnBracketPickedAsync(int number)
    {
        _popover = Popover.None;
        if (Levels is null || number < 1 || number > Levels.Units(_level).Count)
        {
            return Task.CompletedTask;
        }

        PhraseUnit unit = Levels.Units(_level)[number - 1];
        if (unit == CurrentUnit)
        {
            // The second tap lifts the phrase into focus.
            EnterFocus();
            return Task.CompletedTask;
        }

        TickSpan span = Levels.Selectable(unit);
        string? note = span == unit.Span ? null : Text.Format("selection.unitCapped", UnitName(unit.Level), unit.Number, unit.BarCount, MotifSelection.MaxStructureBars, _meter!.BarAt(new Tick(span.End.Value - 1)).Number);
        return SelectAsync(span, note);
    }

    /// <summary>
    /// Shows the piece at another level: the brackets are drawn again and renumbered, and the selection moves to
    /// the unit that is it, encloses it or holds its start (0018 section 3). The level is kept with the piece.
    /// </summary>
    private async Task SetLevelAsync(PhraseLevel level)
    {
        if (level == _level || Levels is null)
        {
            return;
        }

        _level = level;
        if (_project is not null && PieceScoped([GranularitySetting]))
        {
            _project = _project.OverrideSetting(GranularitySetting, PhraseLevels.SettingValue(level));
        }
        else if (_project is not null)
        {
            // Everywhere: the Settings sheet's level changes, and every piece without its own follows it.
            await SettingsStore.SetAsync(GranularitySetting, PhraseLevels.SettingValue(level));
        }

        if (_selection is { } selection)
        {
            PhraseUnit unit = Levels.Map(selection.Span, level);
            await SelectAsync(Levels.Selectable(unit), note: null);
        }
        else
        {
            await SaveProjectAsync();
        }
    }

    /// <summary>The level's own edits go; the other levels keep theirs.</summary>
    private Task ResetLevelAsync() =>
        Levels is { } levels ? ChangePhrasesAsync(levels.Reset(_level), around: null) : Task.CompletedTask;

    private async Task ToggleBracketsAsync()
    {
        _showBrackets = !_showBrackets;
        await SettingsStore.SetAsync(BracketsPreference, _showBrackets);
    }

    private void TogglePopover(Popover popover)
    {
        _popover = _popover == popover ? Popover.None : popover;
        _pillMenu = PillMenu.None;
    }

    private void ClosePopover()
    {
        _popover = Popover.None;
        _pillMenu = PillMenu.None;
    }

    private void TogglePillMenu(PillMenu menu)
    {
        _pillMenu = _pillMenu == menu ? PillMenu.None : menu;
        _popover = Popover.None;
    }

    /// <summary>The roll drawer dragged nearly away by its handle (Scripts/layout/drawer-resize.ts).</summary>
    [JSInvokable]
    public void OnDrawerClosed()
    {
        _rollOpen = false;
        _besideOpen = false;
        _pointed = null;
        StateHasChanged();
    }

    /// <summary>What heads the roll drawer: the bars it shows the selection of, or just its name.</summary>
    private string RollHeading => _selection is { } selection ? Text.Format("roll.headingBars", BarsOf(selection.Span)) : Text["roll.heading"];

    private void ToggleRoll()
    {
        _rollOpen = !_rollOpen;
        _pointed = null;
        _popover = Popover.None;
    }

    private void ToggleInsights()
    {
        _insightsOpen = !_insightsOpen;
        _asking = false;
        _cameFrom = null;
        if (_dnaAside)
        {
            // Piece DNA put aside under the assistant goes with it; its playing stopped when it was put aside.
            _dnaAside = false;
            _dna = null;
        }
        _contentsOpen &= !_insightsOpen;
        _checkOpen &= !_insightsOpen;
        _performanceOpen &= !_insightsOpen;
        _popover = Popover.None;
    }

    private async Task TogglePenAsync()
    {
        _popover = Popover.None;
        if (_scoreView is not null)
        {
            await _scoreView.TogglePencilAsync();
        }
    }

    private void OnPenChanged(ScoreView.PencilState state) => _penOn = state.On;

    private async Task OnPagesShown((int First, int Last, int Count) pages)
    {
        _pages = pages;
        if (At is { } at && at != _shownAt && pages.Count > 0 && _scoreView is not null)
        {
            _shownAt = at;
            await _scoreView.ShowTickAsync(at);
        }

        await SaveCoverAsync();
    }

    /// <summary>Where in the piece to show on arrival, in ticks, as the "PDF" view's page leads here (0023).</summary>
    [SupplyParameterFromQuery(Name = "at")]
    public long? At { get; set; }

    /// <summary>The place last shown for <see cref="At"/>, so it is shown once.</summary>
    private long? _shownAt;

    /// <summary>
    /// The "PDF" view of the PDF these notes were read from (0023), at the page the scan read the bar at the top of this page from: the link
    /// is rough, by the pages the scan read.
    /// </summary>
    private async Task ShowPdfAsync()
    {
        if (_project is not { } project || (project.Document is null && project.ReadFrom is null))
        {
            return;
        }

        // Notes read from a PDF since 2026-11-05 are a piece of their own, linked to the PDF piece; before, the PDF was in this project.
        long? tick = _scoreView is null || _pages.First < 1 ? null : await _scoreView.FirstTickOnAsync(_pages.First);
        int page = (project.ReadFrom?.Pages ?? project.DocumentPages)?.PageOf(new Tick(tick ?? 0)) ?? 1;
        string pdf = project.ReadFrom?.ProjectId ?? project.Id;
        Navigation.NavigateTo($"pdf?project={Uri.EscapeDataString(pdf)}&page={page}");
    }

    /// <summary>The project, phrases and pages the library's cover was last saved for.</summary>
    private string? _coverKey;

    /// <summary>
    /// Keeps the piece's cover for its card in the library (0018 section 9) once the whole score shows as pages: the first
    /// page, with where the pages, the phrases and the bars start, so the card can place its resume point without opening it.
    /// </summary>
    private async Task SaveCoverAsync()
    {
        if (_project is not { } project || _scoreView is null || Levels is null || _meter is null || _score is null || Focused || _pages.Count == 0)
        {
            return;
        }

        IReadOnlyList<PhraseUnit> phrases = Levels.Units(PhraseLevel.Phrase);
        string key = $"{project.Id}:{_pages.Count}:{string.Join(',', phrases.Select(unit => unit.Span.Onset.Value))}";
        if (key == _coverKey)
        {
            return;
        }

        _coverKey = key;
        bool saved = await _scoreView.SaveCoverAsync(
            project.Id,
            phrases.Select(unit => unit.Span.Onset.Value),
            _meter.BarsBefore(_score.End).Select(bar => bar.Span.Onset.Value),
            _meter.FirstBarNumber);
        if (!saved)
        {
            // Not engraved yet: the next render tries again.
            _coverKey = null;
        }
    }

    /// <summary>The title the score's first page is headed with, when it has one.</summary>
    private string? _engravedTitle;

    /// <summary>The piece's title in the bar: the one the player gave it in the library (version 0.2), else as engraved, else from the file's name.</summary>
    private string BarTitle => _project?.Metadata.Title ?? _engravedTitle ?? ScoreTitle.Of(_title) ?? Text["workspace.title"];

    private void OnTitleRead(string? title) => _engravedTitle = string.IsNullOrWhiteSpace(title) ? null : title;

    private async Task SetPagedAsync(bool paged)
    {
        if (_scoreView is not null)
        {
            await _scoreView.SetPagedAsync(paged);
        }
    }

    private async Task ExportPdfAsync()
    {
        if (_scoreView is not null)
        {
            await _scoreView.ExportPdfAsync();
        }
    }

    /// <summary>Limits what plays and lights up to the chosen notes (S1-M2), from the pill.</summary>
    private Task ChooseFilterAsync(int index)
    {
        _pillMenu = PillMenu.None;
        return SetFilterAsync(new ChangeEventArgs { Value = index.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    /// <summary>
    /// Page Up and Page Down, and the page-turner pedals that send them (#126): the next or previous phrase at the
    /// level shown; with nothing selected, Page Down starts at the first. The arrow keys move by page (score-view.ts).
    /// </summary>
    [JSInvokable]
    public async Task StepAsync(int direction)
    {
        EnsureLevels();
        await StepUnitAsync(direction);
        StateHasChanged();
    }

    /// <summary>
    /// The unit before or after the selection at the level shown; the first when nothing is selected. In focus, the one
    /// before or after the phrase in focus, and the card moves to it.
    /// </summary>
    private async Task StepUnitAsync(int direction)
    {
        if (Levels is null)
        {
            return;
        }

        IReadOnlyList<PhraseUnit> units = Levels.Units(_level);
        PhraseUnit? from = Focused ? FocusUnit : _selection is { } selection ? CurrentUnit ?? Levels.At(_level, selection.Span.Onset) : null;
        int index = from is not null ? from.Number - 1 + Math.Sign(direction) : direction > 0 ? 0 : -1;
        if (index < 0 || index >= units.Count)
        {
            return;
        }

        await SelectAsync(Levels.Selectable(units[index]), note: null);
        if (_focus)
        {
            _focusSpan = _selection?.Span;
        }
    }

    /// <summary>A click on a bar in the score selects it, and a Shift-click extends the selection to it, as on the roll (S1-M1).</summary>
    private Task OnScoreBarPickedAsync((Tick Start, bool Extend) bar) =>
        OnRangePickedAsync(new RollRange(bar.Start, bar.Start, true, bar.Extend));

    /// <summary>A tap on the page where there is no music puts the selection away (0018 section 3), or with none the kept pill; the sheets stay as they are.</summary>
    private Task OnPaperTappedAsync()
    {
        if (_penOn || Focused)
        {
            return Task.CompletedTask;
        }

        if (_selection is null)
        {
            // With nothing selected the tap puts the kept pill away, and its popover with it.
            if (Player.State == PlaybackState.Stopped)
            {
                _pillKept = false;
                _pillMenu = PillMenu.None;
            }

            return Task.CompletedTask;
        }

        return ClearSelectionAsync();
    }

    /// <summary>Starts the bar's fade and the page-turner keys once the score page is showing; stops them when it goes.</summary>
    private async Task SyncLayoutAsync()
    {
        if (_score is null)
        {
            await DetachLayoutAsync();
            return;
        }

        // The bar is on the page from the render after the piece arrives. Renders can come while this awaits, so it starts once.
        if (_attaching || _barFade is not null || string.IsNullOrEmpty(_bar.Id))
        {
            return;
        }

        _attaching = true;
        try
        {
            _self ??= DotNetObjectReference.Create(this);
            _chromeModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/layout/bar-fade.js");
            _barFade = await _chromeModule.InvokeAsync<IJSObjectReference>("attach", _main, _bar, BarFadeMilliseconds);
            IJSObjectReference keys = await JS.InvokeAsync<IJSObjectReference>("import", "./js/phrases/phrase-keys.js");
            _phraseKeys = await keys.InvokeAsync<IJSObjectReference>("listen", _self);
            await keys.DisposeAsync();
            IJSObjectReference modes = await JS.InvokeAsync<IJSObjectReference>("import", "./js/layout/mode-keys.js");
            _modeKeys = await modes.InvokeAsync<IJSObjectReference>("listen", _self);
            await modes.DisposeAsync();
            IJSObjectReference drawer = await JS.InvokeAsync<IJSObjectReference>("import", "./js/layout/drawer-resize.js");
            _drawerResize = await drawer.InvokeAsync<IJSObjectReference>("attach", _main, _self);
            await drawer.DisposeAsync();
            IJSObjectReference press = await JS.InvokeAsync<IJSObjectReference>("import", "./js/layout/long-press.js");
            _longPress = await press.InvokeAsync<IJSObjectReference>("attach", _main, _self);
            await press.DisposeAsync();
        }
        catch (JSException)
        {
            // Without the script the bar stays as it is; the page still works.
        }
        finally
        {
            _attaching = false;
        }
    }

    private async Task DetachLayoutAsync()
    {
        foreach (IJSObjectReference? part in new[] { _barFade, _phraseKeys, _drawerResize, _longPress, _modeKeys })
        {
            if (part is null)
            {
                continue;
            }

            try
            {
                await part.InvokeVoidAsync("dispose");
                await part.DisposeAsync();
            }
            catch (JSException)
            {
            }
            catch (JSDisconnectedException)
            {
            }
        }

        (_barFade, _phraseKeys, _drawerResize, _longPress, _modeKeys, _modeKeysOn) = (null, null, null, null, null, null);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DetachLayoutAsync();
            if (_chromeModule is not null)
            {
                await _chromeModule.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The page is going away with the layout.
        }

        _self?.Dispose();
    }
}