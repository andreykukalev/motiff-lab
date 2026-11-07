using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;
using MotiffLab.Web.Roll;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Merging another version into the player's notes (testing items 32 and 34; Andrei, 2026-11-07): the Sources drawer has a
/// list and a page for each version; the page offers Keep my notes, Take the sure bars and Take the version wherever it
/// differs, each of which opens a draft. In the draft the player takes a bar or a phrase from either side or writes it in
/// the roll, steps through the changes and hears and sees three views (mine, the version, the result) before Apply.
/// Everything can be undone: each choice, the draft, the merge once applied, and the version's removal. The full-screen
/// Compare versions window works on the same draft.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>"Применять сразу, без черновика": a choice on the version page applies at once (with Undo in its toast).</summary>
    private const string MergeDirectSetting = "merge.direct";

    private const string MergeMineSource = "merge-mine";
    private const string MergeVersionSource = "merge-version";
    private const string MergeResultSource = "merge-result";

    /// <summary>The version whose page the drawer shows; null shows the list of sources.</summary>
    private string? _versionPage;

    /// <summary>The merge draft open; null when none is.</summary>
    private MergeDraft? _draft;

    /// <summary>The bar of the change the draft shows.</summary>
    private int? _draftAt;

    /// <summary>Whether a choice takes the whole phrase («Фраза») rather than the bar («Такт»).</summary>
    private bool _draftPhrase;

    /// <summary>What the score shows while a draft is open: "mine", "version" or "result" (the default).</summary>
    private string _mergeShow = "result";

    /// <summary>What the three views loop: "mine", "version", "result" or "alternate"; null while none plays.</summary>
    private string? _mergeHearing;

    private bool _compareOpen;

    /// <summary>The window's list: "all", "sure", "doubtful" or "decided".</summary>
    private string _compareFilter = "all";

    /// <summary>How much the views show round the change, for reading only: "bar" (± 1 bar), "phrase" or "sentence".</summary>
    private string _compareSee = "bar";

    /// <summary>The size of the notes in the window's views, in per cent.</summary>
    private int _compareZoom = 100;

    /// <summary>What the last merge action did, with the step that undoes it: a toast over the score.</summary>
    private (string Text, Func<Task>? Undo)? _mergeToast;

    /// <summary>The version removed last, where it was and the Check flags performances had, to put back while the piece is open.</summary>
    private (SavedPerformance Performance, int Index, IReadOnlyList<CheckFlag> Flags)? _removedVersion;

    /// <summary>The bar being written in the roll for the draft: its notes as they stand, the steps before each edit, and the notes it started from.</summary>
    private (int Bar, IReadOnlyList<Note> Notes, Stack<IReadOnlyList<Note>> Steps, IReadOnlyList<Note> From)? _ownEdit;

    private string? _ownPicked;

    /// <summary>Whether the bar is written from the window, which opens again once it is done.</summary>
    private bool _ownFromWindow;

    private (MergeDraft Draft, NoteEditLog Edits, IReadOnlyList<Note> Imported, IReadOnlyList<TakenBar> Taken, IReadOnlyList<Note> Result)? _draftResult;

    private (object Key, PhraseCardSource Card)? _mergeMineCard, _mergeVersionCard, _mergeResultCard;

    private (object Key, ScoreSource Score, IReadOnlyList<string> Version, IReadOnlyList<string> Own)? _mergeScore;

    private bool MergeDirect => SettingsStore.IsOn(MergeDirectSetting);

    /// <summary>The version the draft is for, when it is still in the piece.</summary>
    private SavedPerformance? DraftVersion => _draft is { } draft ? _project?.FindPerformance(draft.PerformanceId) : null;

    /// <summary>The version whose page is open.</summary>
    private SavedPerformance? PageVersion => _versionPage is { } id ? _project?.FindPerformance(id) : null;

    /// <summary>The draft's version compared with the print: the version shown is always the draft's while it is open.</summary>
    private PerformanceComparison? DraftComparison => DraftVersion is not null && ShownPerformance?.Id == DraftVersion.Id ? ShownComparison : null;

    /// <summary>The bars the draft takes and the notes it gives, worked out again only when the draft or the print change.</summary>
    private (IReadOnlyList<TakenBar> Taken, IReadOnlyList<Note> Result)? DraftOutcome
    {
        get
        {
            if (_draft is not { } draft || DraftVersion is not { } version || DraftComparison is not { } comparison || _meter is null)
            {
                return null;
            }

            if (_draftResult is not { } cached || !ReferenceEquals(cached.Draft, draft) || !ReferenceEquals(cached.Edits, _edits) || !ReferenceEquals(cached.Imported, _imported))
            {
                IReadOnlyList<Note> printed = PrintedNotes;
                IReadOnlyList<TakenBar> taken = PerformanceMerge.Taken(draft, version, comparison, _meter, printed);
                cached = (draft, _edits, _imported, taken, PerformanceTake.Apply(printed, taken));
                _draftResult = cached;
            }

            return (cached.Taken, cached.Result);
        }
    }

    /// <summary>The draft's bars, each with its comparison, in bar order.</summary>
    private IReadOnlyList<BarComparison> DraftRows => _draft is { } draft && DraftComparison is { } comparison
        ? [.. draft.Bars.Select(bar => comparison.Bars.First(compared => compared.Bar == bar))]
        : [];

    /// <summary>The window's list after its filter.</summary>
    private IReadOnlyList<BarComparison> CompareRows => _compareFilter switch
    {
        "sure" => [.. DraftRows.Where(bar => bar.Confidence >= PerformanceTake.AutomaticConfidence)],
        "doubtful" => [.. DraftRows.Where(bar => bar.Confidence < PerformanceTake.AutomaticConfidence)],
        "decided" => [.. DraftRows.Where(bar => _draft?.DecisionFor(bar.Bar) is not null)],
        _ => DraftRows,
    };

    /// <summary>The bars in the version's comparison it is sure enough of, for «Взять уверенные (N)».</summary>
    private int PageSureCount => PageVersion is not null && ShownPerformance?.Id == PageVersion.Id && ShownComparison is { } comparison
        ? PerformanceMerge.SureBars(comparison).Count(bar => !PageVersion.Dismissed.Contains(bar))
        : 0;

    private int PageDifferingCount => PageVersion is not null && ShownPerformance?.Id == PageVersion.Id && ShownComparison is { } comparison
        ? PerformanceMerge.DraftBars(comparison).Count(bar => !PageVersion.Dismissed.Contains(bar))
        : 0;

    /// <summary>The choice for a bar in the draft, as the row shows it: «не решено», «мой», «из версии», «свой», «не отличие».</summary>
    private string ChoiceName(int bar) => _draft?.DecisionFor(bar)?.Choice switch
    {
        MergeChoice.Mine => Text["merge.state.mine"],
        MergeChoice.Version => Text["merge.state.version"],
        MergeChoice.Own => Text["merge.state.own"],
        MergeChoice.NotADifference => Text["merge.state.notDifference"],
        _ => Text["merge.state.open"],
    };

    private string ChoiceClass(int bar) => _draft?.DecisionFor(bar)?.Choice switch
    {
        MergeChoice.Mine => "mine",
        MergeChoice.Version => "version",
        MergeChoice.Own => "own",
        MergeChoice.NotADifference => "same",
        _ => "open",
    };

    private static string GroupKey(MergeGroup group) => group switch
    {
        MergeGroup.AddedNotes => "merge.group.added",
        MergeGroup.Rhythm => "merge.group.rhythm",
        _ => "merge.group.other",
    };

    /// <summary>The verdict sentence over the version's strip of bars.</summary>
    private string PageVerdict(SavedPerformance version) => PageDifferingCount == 0
        ? Text.Format("merge.verdictSame", version.Name)
        : Text.Format("merge.verdict", version.Name, Text.Count("merge.bars", PageDifferingCount), Text.Count("merge.sure", PageSureCount));

    private void OpenVersionPage(string id)
    {
        ShowPerformanceOf(id);
        _versionPage = id;
        _addingSource = false;
        _readingDocument = false;
    }

    private void CloseVersionPage()
    {
        _versionPage = null;
    }

    /// <summary>
    /// Keep my notes, Take the sure bars or Take the version everywhere: a draft opens with that choice made, or with
    /// «Применять сразу» on it is applied at once, with Undo in the toast.
    /// </summary>
    private async Task StartDraftAsync(MergeStart start)
    {
        if (PageVersion is not { } version || ShownComparison is not { } comparison)
        {
            return;
        }

        MergeDraft draft = PerformanceMerge.Open(version, comparison, start);
        if (start != MergeStart.Saved && MergeDirect)
        {
            _draft = draft;
            await ApplyDraftAsync();
            return;
        }

        OpenDraft(draft);
    }

    private void OpenDraft(MergeDraft draft)
    {
        _draft = draft;
        _draftResult = null;
        _mergeShow = "result";
        _draftAt = draft.Bars.FirstOrDefault(bar => draft.DecisionFor(bar)?.Choice is MergeChoice.Version or MergeChoice.Own) is var first and > 0
            ? first
            : draft.Bars.Count > 0 ? draft.Bars[0] : null;
    }

    /// <summary>«Открыть черновик снова»: the draft with every choice as last applied, to adjust and apply again.</summary>
    private Task ReopenDraftAsync() => StartDraftAsync(MergeStart.Saved);

    private async Task ChangeDraftAsync(Func<MergeDraft, MergeDraft> change)
    {
        if (_draft is not { } draft)
        {
            return;
        }

        _draft = change(draft);
        await RefreshMergeSoundAsync();
    }

    /// <summary>Take mine or the version for the bar shown, or for every differing bar of its phrase.</summary>
    private Task ChooseAsync(int bar, MergeChoice choice) => ChangeDraftAsync(draft => draft.Choose(ChoiceBars(bar), choice));

    /// <summary>The bars a choice for this bar takes: the bar, or with «Фраза» every differing bar of the phrase it is in.</summary>
    private IReadOnlyList<int> ChoiceBars(int bar)
    {
        if (!_draftPhrase || _meter is null || _phrases is null || _draft is not { } draft)
        {
            return [bar];
        }

        BarRange phrase = BarRange.Of(_meter, _phrases.Covering(_meter.GetBar(bar).Span).Span);
        return [.. draft.Bars.Where(phrase.Contains)];
    }

    /// <summary>«Взять группу»: the same choice for every bar of one kind.</summary>
    private Task ChooseGroupAsync(MergeGroup group, MergeChoice choice) =>
        ChangeDraftAsync(draft => draft.Choose(DraftRows.Where(bar => PerformanceMerge.GroupOf(bar.Difference) == group).Select(bar => bar.Bar), choice));

    private Task UndoDraftAsync() => ChangeDraftAsync(draft => draft.Undo());

    private Task RedoDraftAsync() => ChangeDraftAsync(draft => draft.Redo());

    private Task ResetDraftAsync() => ChangeDraftAsync(draft => draft.Reset());

    private Task UndecideAsync(int bar) => ChangeDraftAsync(draft => draft.Undecide([bar]));

    /// <summary>Cancel: the draft goes and nothing in the score changed.</summary>
    private async Task CancelDraftAsync()
    {
        _draft = null;
        _draftResult = null;
        _compareOpen = false;
        _ownEdit = null;
        await StopMergeHearingAsync();
    }

    /// <summary>
    /// Apply: the bars the draft takes come into the score in place of those taken from the version before, its choices are
    /// kept with the piece to open again, and the toast offers Undo, back to the piece as it was before Apply.
    /// </summary>
    private async Task ApplyDraftAsync()
    {
        if (_project is not { } project || _draft is not { } draft || DraftVersion is not { } version || DraftComparison is not { } comparison || _meter is null)
        {
            return;
        }

        SavedPerformance before = version;
        SavedPerformance merged = PerformanceMerge.Apply(draft, version, comparison, _meter, PrintedNotes, DateTimeOffset.Now);
        _project = project.ChangePerformance(version.Id, _ => merged);
        _draft = null;
        _draftResult = null;
        _compareOpen = false;
        _ownEdit = null;
        await StopMergeHearingAsync();
        _mergeToast = (Text.Count("merge.applied", merged.Taken.Count), () => PutPerformanceBackAsync(before, Text["merge.applyUndone"]));
        // The version's page says what was merged, with Open the draft again and Undo the merge.
        _performanceOpen = _versionPage == version.Id;
        await ApplyEditsAsync(_edits);
    }

    /// <summary>«Отменить слияние»: the player's notes back in every bar taken from the version; the choices stay, to open again.</summary>
    private async Task UndoMergeAsync()
    {
        if (_project is not { } project || PageVersion is not { } version)
        {
            return;
        }

        _project = project.ChangePerformance(version.Id, PerformanceMerge.Undo);
        _mergeToast = (Text["merge.undone"], () => PutPerformanceBackAsync(version, Text["merge.redone"]));
        await ApplyEditsAsync(_edits);
    }

    /// <summary>A version back as it was before the last step: the toast's Undo.</summary>
    private async Task PutPerformanceBackAsync(SavedPerformance before, string said)
    {
        if (_project is not { } project || project.FindPerformance(before.Id) is null)
        {
            return;
        }

        _project = project.ChangePerformance(before.Id, _ => before);
        _mergeToast = (said, null);
        await ApplyEditsAsync(_edits);
    }

    /// <summary>
    /// «Убрать версию из пьесы»: the version, its taken bars and its Check flags go at once, and the toast's Return puts it
    /// back while the piece is open.
    /// </summary>
    private async Task RemoveVersionAsync()
    {
        if (_project is not { } project || PageVersion is not { } version)
        {
            return;
        }

        _removedVersion = (version, project.Performances.ToList().FindIndex(kept => kept.Id == version.Id),
            [.. (project.Check ?? []).Where(flag => flag.Source == CheckSource.Performance)]);
        if (_draft?.PerformanceId == version.Id)
        {
            await CancelDraftAsync();
        }

        _project = project.RemovePerformance(version.Id);
        _versionPage = null;
        _performanceId = null;
        _scoreChecks = null;
        _mergeToast = (Text.Format("merge.removed", version.Name), PutBackVersionAsync);
        await ApplyEditsAsync(_edits);
    }

    private async Task PutBackVersionAsync()
    {
        if (_project is not { } project || _removedVersion is not { } removed || project.FindPerformance(removed.Performance.Id) is not null)
        {
            return;
        }

        _project = project.PutBackPerformance(removed.Performance, removed.Index, removed.Flags);
        _removedVersion = null;
        _scoreChecks = null;
        _performanceId = removed.Performance.Id;
        _mergeToast = (Text.Format("merge.putBack", removed.Performance.Name), null);
        await ApplyEditsAsync(_edits);
    }

    private async Task UndoToastAsync()
    {
        if (_mergeToast is { Undo: { } undo })
        {
            _mergeToast = null;
            await undo();
        }
    }

    private void CloseToast() => _mergeToast = null;

    /// <summary>‹ ›: the change before or after the one shown, among the window's list when it is open.</summary>
    private Task StepChangeAsync(int by)
    {
        IReadOnlyList<int> bars = [.. (_compareOpen ? CompareRows : DraftRows).Select(bar => bar.Bar)];
        if (bars.Count == 0)
        {
            return Task.CompletedTask;
        }

        int index = _draftAt is { } at ? bars.ToList().IndexOf(at) : -1;
        int next = index < 0 ? 0 : Math.Clamp(index + by, 0, bars.Count - 1);
        return GoToBarAsync(bars[next]);
    }

    /// <summary>A bar picked anywhere in the draft: the views and the score go to it.</summary>
    private async Task GoToBarAsync(int bar)
    {
        _draftAt = bar;
        _ownEdit = null;
        await ShowPerformanceBarAsync(bar);
        await RefreshMergeSoundAsync();
    }

    /// <summary>The bars the views show round the change: ± 1 bar, its phrase or its sentence. Reading only; what is taken does not change.</summary>
    private BarRange? MergeBars
    {
        get
        {
            if (_draftAt is not { } at || _meter is null || _score is null)
            {
                return null;
            }

            int last = MotifSelector.LastBar(_meter, _score.End);
            TickSpan span = _meter.GetBar(at).Span;
            return _compareSee switch
            {
                "phrase" when _phrases is not null => BarRange.Of(_meter, _phrases.Covering(span).Span),
                "sentence" when _sentences is { Sentences.Count: > 0 } sentences => BarRange.Of(_meter, sentences.At(span.Onset).Span),
                _ => new BarRange(Math.Max(_meter.FirstBarNumber, at - 1), Math.Min(last, at + 1)),
            };
        }
    }

    /// <summary>The notes of one view: mine, the version in the score's time, or the result.</summary>
    private IReadOnlyList<Note>? MergeViewNotes(string view) => view switch
    {
        "mine" => PrintedNotes,
        "version" => VersionNotes,
        _ => DraftOutcome?.Result,
    };

    /// <summary>The ids a view tints: what comes from the version plum, what the player wrote amber.</summary>
    private (IReadOnlyList<string> Version, IReadOnlyList<string> Own) MergeTints(string view, IReadOnlyList<Note> notes)
    {
        if (view == "version")
        {
            return (_versionNotes?.Layer.Added ?? [], []);
        }

        if (view != "result" || DraftVersion is not { } version)
        {
            return ([], []);
        }

        string own = PerformanceMerge.OwnPrefix(version.Id);
        string taken = $"{PerformanceTake.IdPrefix}{version.Id}-";
        return ([.. notes.Select(note => note.Id).Where(id => id.StartsWith(taken, StringComparison.Ordinal) && !id.StartsWith(own, StringComparison.Ordinal))],
            [.. notes.Select(note => note.Id).Where(id => id.StartsWith(own, StringComparison.Ordinal))]);
    }

    /// <summary>One of the three views engraved over the bars shown, its notes tinted; engraved again only when they change.</summary>
    private PhraseCardSource? MergeCard(string view)
    {
        if (MergeBars is not { } bars || MergeViewNotes(view) is not { } notes || _score is null || _meter is null)
        {
            return null;
        }

        ref (object Key, PhraseCardSource Card)? cache = ref view == "mine" ? ref _mergeMineCard : ref view == "version" ? ref _mergeVersionCard : ref _mergeResultCard;
        var key = (bars, notes, _readable, Text.Culture.Name);
        if (cache is { } cached && Equals(cached.Key, key))
        {
            return cached.Card;
        }

        Tick end = new(Math.Max(_score.End.Value, notes.Count == 0 ? 0 : notes.Max(note => note.Span.End.Value)));
        GeneratedNotation notation = _heard
            ? NotationGenerator.Generate(notes, _meter, NotationKeys(), end, asHeard: true, bars: bars)
            : NotationGenerator.Generate(notes, _meter, NotationKeys(), end, bars: bars);
        (IReadOnlyList<string> version, IReadOnlyList<string> own) = MergeTints(view, notes);
        Dictionary<string, string> tints = [];
        foreach (string id in version)
        {
            tints[id] = "version-added";
        }

        foreach (string id in own)
        {
            tints[id] = "own-note";
        }

        PhraseCardSource card = PhraseCardSource.From(notation, _meter.GetBar(bars.FirstBar).Span.Onset, _ => "") with { Tints = tints };
        cache = (key, card);
        return card;
    }

    /// <summary>
    /// The score while a draft is open and «Показать» is on the version or the result: those notes in place of the player's,
    /// tinted as in the views. Null shows the player's notes.
    /// </summary>
    private ScoreSource? MergeScore
    {
        get
        {
            if (_draft is null || _mergeShow == "mine" || MergeViewNotes(_mergeShow) is not { } notes || _score is null)
            {
                return null;
            }

            var key = (_mergeShow, notes, _readable);
            if (_mergeScore is { } cached && Equals(cached.Key, key))
            {
                return cached.Score;
            }

            Tick end = new(Math.Max(_score.End.Value, notes.Count == 0 ? 0 : notes.Max(note => note.Span.End.Value)));
            (IReadOnlyList<string> version, IReadOnlyList<string> own) = MergeTints(_mergeShow, notes);
            ScoreSource score = ScoreSource.FromNotes(Notation(notes, end));
            _mergeScore = (key, score, version, own);
            return score;
        }
    }

    private IReadOnlyList<string>? MergeScoreVersion => MergeScore is null ? null : _mergeScore?.Version;

    private IReadOnlyList<string>? MergeScoreOwn => MergeScore is null ? null : _mergeScore?.Own;

    private void ShowMergeView(string view) => _mergeShow = view;

    /// <summary>
    /// ▶ for a view, or Мои · Версия · Итог · Поочерёдно: the bars shown play once as that view has them, or the three one
    /// after another, and playback stops back at their start. Pressed while playing, it stops; pressed again, it plays again.
    /// </summary>
    private async Task HearMergeAsync(string view)
    {
        if (_mergeHearing == view && Player.State != PlaybackState.Stopped)
        {
            await StopMergeHearingAsync();
            return;
        }

        _mergeHearing = view;
        await RefreshMergeSoundAsync(start: true);
    }

    /// <summary>The views' sound kept to the draft and the bars shown, as the choices or the bar change.</summary>
    private async Task RefreshMergeSoundAsync(bool start = false)
    {
        if (_mergeHearing is not { } view || MergeBars is not { } bars || _meter is null || _score is null)
        {
            return;
        }

        TickSpan range = TickSpan.FromBounds(_meter.GetBar(bars.FirstBar).Span.Onset, _meter.GetBar(bars.LastBar).Span.End);
        await Player.SetSourceAsync(MergeMineSource, _score.WithNotes(Voiced(PrintedNotes)));
        await Player.SetSourceAsync(MergeVersionSource, _score.WithNotes(Voiced(VersionNotes ?? [])));
        await Player.SetSourceAsync(MergeResultSource, _score.WithNotes(Voiced(DraftOutcome?.Result ?? PrintedNotes)));
        _looping = true;
        _alternating = false;
        if (view == "alternate")
        {
            await Player.AlternateAsync(
            [
                new LoopPass([MergeMineSource], range),
                new LoopPass([MergeVersionSource], range),
                new LoopPass([MergeResultSource], range),
            ], once: true);
        }
        else
        {
            await Player.SelectSourcesAsync(view == "mine" ? MergeMineSource : view == "version" ? MergeVersionSource : MergeResultSource);
            await Player.SetLoopAsync(range, once: true);
        }

        if (start || !range.Contains(Player.Position))
        {
            await Player.SeekAsync(range.Onset);
        }

        if (start && Player.State == PlaybackState.Stopped)
        {
            await PlayAsync();
        }
    }

    private async Task StopMergeHearingAsync()
    {
        if (_mergeHearing is null)
        {
            return;
        }

        _mergeHearing = null;
        _looping = false;
        await Player.PauseAsync();
        await Player.SetLoopAsync(null);
        await Player.SelectSourcesAsync(Player.OriginalSource);
    }

    /// <summary>«Подробное сравнение ⤢»: the window opens on the draft, which opens as last applied when none is open.</summary>
    private async Task OpenCompareWindowAsync()
    {
        if (_draft is null)
        {
            await StartDraftAsync(MergeStart.Saved);
        }

        if (_draft is null)
        {
            return;
        }

        _compareOpen = true;
        _performanceOpen = false;
        _popover = Popover.None;
    }

    /// <summary>A bar in the overview's strip: with a draft open the draft goes to it; otherwise the score shows it.</summary>
    private Task StripBarAsync(int bar) => _draft?.Bars.Contains(bar) == true ? GoToBarAsync(bar) : ShowPerformanceBarAsync(bar);

    private Task ToggleMergeDirectAsync() => SettingsStore.SetAsync(MergeDirectSetting, !MergeDirect);

    private void CloseCompare()
    {
        _compareOpen = false;
        _ownEdit = null;
        _performanceOpen = _draft is not null;
    }

    private void ZoomCompare(int by) => _compareZoom = Math.Clamp(_compareZoom + by, 60, 200);

    /// <summary>The window's keys: M mine, V the version, E the roll, ← → the changes; ⌘Z / Ctrl+Z undo, ⇧⌘Z / Ctrl+Y redo.</summary>
    private async Task CompareKeyAsync(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs args)
    {
        bool command = args.CtrlKey || args.MetaKey;
        switch (args.Key)
        {
            case "z" or "Z" when command && args.ShiftKey:
            case "y" or "Y" when command:
                await RedoDraftAsync();
                break;
            case "z" or "Z" when command:
                await UndoDraftAsync();
                break;
            case "m" or "M" or "ь" or "Ь" when !command && _draftAt is { } mine:
                await ChooseAsync(mine, MergeChoice.Mine);
                break;
            case "v" or "V" or "м" or "М" when !command && _draftAt is { } version:
                await ChooseAsync(version, MergeChoice.Version);
                break;
            case "e" or "E" or "у" or "У" when !command && _draftAt is { } own:
                BeginOwnEdit(own);
                break;
            case "ArrowLeft":
                await StepChangeAsync(-1);
                break;
            case "ArrowRight":
                await StepChangeAsync(1);
                break;
            case "Escape":
                CloseCompare();
                break;
        }
    }

    /// <summary>
    /// «✎ Править в ролле»: the roll opens on the bar, with the notes the draft gives there to edit and the version's notes
    /// as dashed plum outlines to drag in. «✓ Готово» makes the bar «свой»; it is still part of the draft.
    /// </summary>
    private void BeginOwnEdit(int bar)
    {
        if (_meter is null || DraftOutcome is not { } outcome)
        {
            return;
        }

        TickSpan span = _meter.GetBar(bar).Span;
        IReadOnlyList<Note> notes = [.. outcome.Result.Where(note => span.Contains(note.Span.Onset))];
        _draftAt = bar;
        _ownEdit = (bar, notes, new Stack<IReadOnlyList<Note>>(), notes);
        _ownPicked = null;
        _ownFromWindow = _compareOpen;
        _compareOpen = false;
        _rollOpen = true;
    }

    /// <summary>The roll's notes while a bar is written for the draft: the result with the bar as the player left it.</summary>
    private IReadOnlyList<Note>? OwnEditNotes
    {
        get
        {
            if (_ownEdit is not { } edit || _meter is null || DraftOutcome is not { } outcome)
            {
                return null;
            }

            TickSpan span = _meter.GetBar(edit.Bar).Span;
            return [.. outcome.Result.Where(note => !span.Contains(note.Span.Onset)), .. edit.Notes];
        }
    }

    /// <summary>The version's notes in the bar being written, as outlines in the roll.</summary>
    private IReadOnlyList<Note>? OwnEditGhosts => _ownEdit is { } edit && _meter is not null
        ? [.. (VersionNotes ?? []).Where(note => _meter.GetBar(edit.Bar).Span.Contains(note.Span.Onset))]
        : null;

    /// <summary>The roll's editor while a bar is written for the draft: ✓ Готово in place of Save.</summary>
    private NoteEditor? OwnEditor => _ownEdit is not { } edit ? null : new NoteEditor(
        true,
        _editTool,
        CanUndo: edit.Steps.Count > 0,
        Changed: !ReferenceEquals(edit.Notes, edit.From),
        CanReset: !ReferenceEquals(edit.Notes, edit.From),
        Picked: _ownPicked,
        Begin: () => Task.CompletedTask,
        ChooseTool: Rendered<string>(tool =>
        {
            _editTool = tool;
            return Task.CompletedTask;
        }),
        Change: Rendered<RollNoteEdit>(ChangeOwnNoteAsync),
        Add: Rendered<RollNoteEdit>(AddOwnNoteAsync),
        Remove: Rendered<string>(RemoveOwnNoteAsync),
        Undo: Rendered(() =>
        {
            if (_ownEdit is { } now && now.Steps.TryPop(out IReadOnlyList<Note>? before))
            {
                _ownEdit = now with { Notes = before };
            }

            return Task.CompletedTask;
        }),
        Reset: Rendered(() => OwnStepAsync(edit.From)),
        Cancel: Rendered(() =>
        {
            _ownEdit = null;
            _compareOpen = _ownFromWindow;
            return Task.CompletedTask;
        }),
        Save: Rendered(DoneOwnEditAsync))
    {
        SaveKey = "edit.done",
    };

    private Task OwnStepAsync(IReadOnlyList<Note> next)
    {
        if (_ownEdit is { } edit && !ReferenceEquals(next, edit.Notes))
        {
            edit.Steps.Push(edit.Notes);
            _ownEdit = edit with { Notes = next };
        }

        return Task.CompletedTask;
    }

    /// <summary>A note moved or lengthened, kept inside the bar.</summary>
    private Task ChangeOwnNoteAsync(RollNoteEdit change)
    {
        if (_ownEdit is not { } edit || _meter is null || edit.Notes.FirstOrDefault(note => note.Id == change.Id) is not { } note)
        {
            return Task.CompletedTask;
        }

        TickSpan bar = _meter.GetBar(edit.Bar).Span;
        long onset = Math.Clamp(change.Onset, bar.Onset.Value, bar.End.Value - 1);
        long end = Math.Min(bar.End.Value, onset + Math.Max(1, change.Length));
        Note moved = note with { Pitch = new Pitch(Math.Clamp(change.Pitch, 0, Pitch.MaxMidiNumber)), Span = TickSpan.FromBounds(new Tick(onset), new Tick(end)) };
        _ownPicked = note.Id;
        return OwnStepAsync([.. edit.Notes.Select(kept => kept.Id == note.Id ? moved : kept)]);
    }

    /// <summary>A note added in the bar, with the loudness, track and hand of the note nearest to it; it never crosses the bar line.</summary>
    private Task AddOwnNoteAsync(RollNoteEdit added)
    {
        if (_ownEdit is not { } edit || _meter is null || added.Pitch is < 0 or > Pitch.MaxMidiNumber)
        {
            return Task.CompletedTask;
        }

        TickSpan bar = _meter.GetBar(edit.Bar).Span;
        if (!bar.Contains(new Tick(added.Onset)))
        {
            return Task.CompletedTask;
        }

        Note? near = edit.Notes.OrderBy(note => Math.Abs(note.Pitch.MidiNumber - added.Pitch)).FirstOrDefault();
        string id = $"own-{edit.Bar}-{edit.Steps.Count + 1}-{added.Onset}-{added.Pitch}";
        Note note = new()
        {
            Id = id,
            Pitch = new Pitch(added.Pitch),
            Span = TickSpan.FromBounds(new Tick(added.Onset), new Tick(Math.Min(bar.End.Value, added.Onset + Math.Max(1, added.Length)))),
            Velocity = near?.Velocity ?? 80,
            Track = near?.Track ?? 0,
            Hand = new Attributed<Hand>(added.Pitch < 60 ? Hand.Left : Hand.Right, Attribution.UserEdited),
            Attribution = Attribution.UserEdited,
        };
        _ownPicked = id;
        return OwnStepAsync([.. edit.Notes, note]);
    }

    private Task RemoveOwnNoteAsync(string id)
    {
        if (_ownEdit is not { } edit || edit.Notes.All(note => note.Id != id))
        {
            return Task.CompletedTask;
        }

        _ownPicked = null;
        return OwnStepAsync([.. edit.Notes.Where(note => note.Id != id)]);
    }

    /// <summary>«✓ Готово»: the bar as written becomes the draft's «свой» bar.</summary>
    private async Task DoneOwnEditAsync()
    {
        if (_ownEdit is not { } edit || _draft is not { } draft || _meter is null)
        {
            return;
        }

        _ownEdit = null;
        _compareOpen = _ownFromWindow;
        if (ReferenceEquals(edit.Notes, edit.From))
        {
            return;
        }

        TakenBar own = PerformanceMerge.OwnBar(draft.PerformanceId, edit.Bar, _meter, PrintedNotes, edit.Notes);
        await ChangeDraftAsync(kept => kept.WriteOwn(own));
    }

    /// <summary>When the merge was applied, for «Слияние применено HH:MM · N тактов».</summary>
    private string? MergedLine(SavedPerformance version) => version.Merge is { AppliedAt: { } at }
        ? Text.Format("merge.appliedAt", at.ToLocalTime().ToString("HH:mm", Text.Culture), Text.Count("merge.bars", version.Taken.Count))
        : null;

    /// <summary>The bars taken from versions, for the thin plum line under them, with extra information on.</summary>
    private IReadOnlyList<long>? _takenBarTicks;

    private IReadOnlyList<long>? ScoreTakenBars
    {
        get
        {
            if (!ExtraShown("taken") || _meter is null || _project is not { } project)
            {
                return null;
            }

            long[] ticks = [.. project.TakenBars.Select(bar => _meter.GetBar(bar.Bar).Span.Onset.Value).Distinct().Order()];
            if (_takenBarTicks is null || !_takenBarTicks.SequenceEqual(ticks))
            {
                _takenBarTicks = ticks;
            }

            return _takenBarTicks;
        }
    }
}