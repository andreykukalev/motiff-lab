using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Repairs;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;
using MotiffLab.Web.Compare;
using MotiffLab.Web.Playback;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// «Починка по флажку» (an experiment, Settings · Experimental, version 0.6; the AI companion study, section 2.2; decision
/// record 0028, section 6): Repair on a flagged bar in the Check drawer offers the ways music-core works out from the
/// flag's reason, with no AI (<see cref="CheckRepairs"/>). The chosen way is a draft: the bar engraved with it as a plum
/// ossia, heard As read or as the Draft, or the two in turn, on the bar alone. Accept adds its notes to the corrections
/// (My edits) as ordinary corrections, and one Undo takes them all back; Dismiss drops the draft. Nothing is sent anywhere,
/// and the flag stays until the player says Looks right.
/// </summary>
public partial class PlaybackLab
{
    public const string RepairsSetting = "experimental.assistantRepairs";

    /// <summary>The piece with the chosen draft in place of the bar's notes, heard while Draft is chosen.</summary>
    private const string RepairSource = "repair";

    /// <summary>The flagged bar whose repairs are open; null when none is.</summary>
    private int? _repairBar;

    /// <summary>The way the player chose; null for the first.</summary>
    private string? _repairChosenId;

    private RepairHearing _repairHearing;

    /// <summary>The last repair accepted: its bar and the corrections before and after it, kept here only, so one Undo takes it back whole.</summary>
    private (int Bar, NoteEditLog Before, NoteEditLog After)? _repairUndo;

    private (IReadOnlyList<CheckFlag>? Check, IReadOnlyList<Note>? Notes, string Language, int Bar, IReadOnlyList<RepairOptions> Options)? _repairs;

    private (RepairCandidate Candidate, IReadOnlyList<Note>? Notes, string Language, PhraseCardSource Card)? _repairCard;

    private enum RepairHearing
    {
        None,
        AsRead,
        Draft,
        InTurn,
    }

    private bool RepairsOn => SettingsStore.IsOn(RepairsSetting);

    /// <summary>Whether Repair shows on the flagged bars: the experiment is on and the corrections are laid on the notes.</summary>
    private bool RepairsShown => RepairsOn && !EditsOff && CurrentPiece.Piece is not null && _notes is not null && _meter is not null;

    /// <summary>The repairs of the bar's open flags, worked out once for the flags, the notes and the language as they are.</summary>
    private IReadOnlyList<RepairOptions> RepairOptionsOf(int bar)
    {
        if (!RepairsShown || _project is not { } project)
        {
            return [];
        }

        if (_repairs is { } cached && ReferenceEquals(cached.Check, project.Check) && ReferenceEquals(cached.Notes, _notes) && cached.Language == Text.Language && cached.Bar == bar)
        {
            return cached.Options;
        }

        var piece = new AssistantPiece(CurrentPiece.Piece!, _notes!, project.Check ?? [], Words);
        IReadOnlyList<RepairOptions> options = [.. project.OpenChecks
            .Where(flag => flag.Bar == bar)
            .DistinctBy(flag => flag.Reason)
            .Select(flag => CheckRepairs.For(piece, flag))];
        _repairs = (project.Check, _notes, Text.Language, bar, options);
        return options;
    }

    /// <summary>The way chosen on the open bar: the player's, or the first; null when the bar has none.</summary>
    private RepairCandidate? RepairChosen
    {
        get
        {
            if (_repairBar is not { } bar)
            {
                return null;
            }

            RepairCandidate[] candidates = [.. RepairOptionsOf(bar).SelectMany(options => options.Candidates)];
            return candidates.FirstOrDefault(candidate => candidate.Id == _repairChosenId) ?? candidates.FirstOrDefault();
        }
    }

    /// <summary>The bars the open repairs read and change.</summary>
    private TickSpan? RepairMeasure => _repairBar is { } bar && RepairOptionsOf(bar) is [{ } first, ..] ? first.Measure : null;

    /// <summary>The draft shown now and its bars: the assistant's (#582 item 17.3) or the chosen repair; one at a time.</summary>
    private (AssistantDraft Draft, TickSpan Measure)? ShownDraft =>
        _aiDraft is { } ai ? (ai.Made, ai.Measure)
        : RepairChosen is { } chosen && RepairMeasure is { } measure ? (chosen.Draft, measure)
        : null;

    /// <summary>What is heard of the draft now: nothing once its loop gave way to another.</summary>
    private RepairHearing RepairHeard => _answerLoop is { } loop && loop == ShownDraft?.Measure ? _repairHearing : RepairHearing.None;

    /// <summary>What the switch shows for the draft.</summary>
    private CompareView RepairCompare => new(
        RepairHeard == RepairHearing.AsRead,
        RepairHeard == RepairHearing.Draft,
        RepairHeard == RepairHearing.InTurn,
        RepairHeard == RepairHearing.InTurn && Player.Passes is not null ? Player.Pass == 1 : null,
        Text[_aiDraft is null ? "repair.asRead" : "assistantDraft.asNow"],
        Text[_aiDraft is null ? "repair.draft" : "assistantDraft.draft"]);

    /// <summary>What the card lights up as it sounds.</summary>
    private string RepairSounding => RepairHeard switch
    {
        RepairHearing.Draft => "variation",
        RepairHearing.InTurn when Player.Pass == 1 => "variation",
        _ => "original",
    };

    /// <summary>The flagged bars engraved alone with the chosen draft as an ossia above.</summary>
    private PhraseCardSource? RepairCard
    {
        get
        {
            if (RepairChosen is not { } chosen || RepairMeasure is not { } measure)
            {
                return null;
            }

            if (_repairCard is { } cached && ReferenceEquals(cached.Candidate, chosen) && ReferenceEquals(cached.Notes, _notes) && cached.Language == Text.Language)
            {
                return cached.Card;
            }

            PhraseCardSource? card = DraftCardOf(chosen.Draft, measure, Text["repair.ossia"]);
            _repairCard = card is null ? null : (chosen, _notes, Text.Language, card);
            return card;
        }
    }

    /// <summary>The bars engraved alone with a draft as an ossia above: what it changes plum, what it keeps grey, what it takes out plum below.</summary>
    private PhraseCardSource? DraftCardOf(AssistantDraft draft, TickSpan measure, string ossia)
    {
        if (_notes is null || _meter is null || _score is null)
        {
            return null;
        }

        Variation variation = draft.Variation;
        Tick end = variation.Notes.Count == 0 ? _score.End : new Tick(Math.Max(_score.End.Value, variation.Notes.Max(note => note.Span.End.Value)));
        BarRange bars = BarRange.Of(_meter, measure);
        GeneratedNotation notation = NotationGenerator.Generate(_notes, _meter, NotationKeys(), end, bars: bars, ossias: [new NotationOssia("", variation.Notes)]);
        string prefix = NotationGenerator.OssiaIdPrefix(0);
        var changed = variation.Changes.Where(change => NoteValues.Of(change.Before) != NoteValues.Of(change.After)).Select(change => change.After.Id).Concat(variation.Added.Select(note => note.Id)).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> tints = variation.Notes.ToDictionary(note => prefix + note.Id, note => changed.Contains(note.Id) ? "version-added" : "repair-kept", StringComparer.Ordinal);
        foreach (Note removed in variation.Removed)
        {
            tints[removed.Id] = "version-added";
        }

        return PhraseCardSource.From(notation, _meter.GetBar(bars.FirstBar).Span.Onset, _ => ossia) with { Tints = tints };
    }

    /// <summary>Repair on a flagged bar: its ways open under its entry, the bar selected; again, they close.</summary>
    private async Task ToggleRepairAsync(int bar)
    {
        await StopHearingRepairAsync();
        _aiDraft = null;
        if (_repairBar == bar)
        {
            _repairBar = null;
            return;
        }

        await ShowCheckBarAsync(bar);
        _checkFixing = null;
        _repairBar = bar;
        _repairChosenId = null;
        await SetRepairSourceAsync();
    }

    private async Task ChooseRepairAsync(RepairCandidate candidate)
    {
        _repairChosenId = candidate.Id;
        RepairHearing heard = RepairHeard;
        await SetRepairSourceAsync();
        if (heard is RepairHearing.Draft or RepairHearing.InTurn)
        {
            _repairHearing = RepairHearing.None;
            await HearRepairAsync(heard);
        }
    }

    /// <summary>Gives the engine the piece with the chosen draft in place of the bar's notes.</summary>
    private async Task SetRepairSourceAsync()
    {
        if (ShownDraft is { } shown && _score is not null && _notes is not null)
        {
            var replaced = shown.Draft.Before.Select(note => note.Id).ToHashSet(StringComparer.Ordinal);
            await Player.SetSourceAsync(RepairSource, _score.WithNotes([.. _notes.Where(note => !replaced.Contains(note.Id)), .. shown.Draft.Variation.Notes]));
        }
    }

    private Task HearRepairChoiceAsync(bool draft) => HearRepairAsync(draft ? RepairHearing.Draft : RepairHearing.AsRead);

    private Task HearRepairInTurnAsync() => HearRepairAsync(RepairHearing.InTurn);

    /// <summary>Loops the flagged bars as read, as the draft has them, or the two in turn; the same choice again stops.</summary>
    private async Task HearRepairAsync(RepairHearing choice)
    {
        if (ShownDraft is not { Measure: var range })
        {
            return;
        }

        if (RepairHeard == choice)
        {
            await StopHearingRepairAsync();
            return;
        }

        _looping = false;
        _alternating = false;
        if (choice == RepairHearing.InTurn)
        {
            await Player.AlternateAsync([new LoopPass([Player.OriginalSource], range), new LoopPass([RepairSource], range)]);
        }
        else
        {
            await Player.SelectSourcesAsync(choice == RepairHearing.Draft ? RepairSource : Player.OriginalSource);
            await Player.SetLoopAsync(range);
            await Player.SeekAsync(range.Onset);
        }

        _answerLoop = range;
        _repairHearing = choice;
        if (Player.State == PlaybackState.Stopped)
        {
            await PlayAsync();
        }
    }

    private async Task StopHearingRepairAsync()
    {
        if (RepairHeard != RepairHearing.None)
        {
            await StopLoopingAsync();
        }

        _repairHearing = RepairHearing.None;
    }

    /// <summary>Dismiss: the draft goes, and nothing changed.</summary>
    private async Task DismissRepairAsync()
    {
        await StopHearingRepairAsync();
        _repairBar = null;
    }

    /// <summary>
    /// Accept: the draft's edits added to the corrections one by one, as the player's own (removed, then changed, then added),
    /// so they show in My edits and can be reverted note by note; one Undo takes the whole repair back.
    /// </summary>
    private async Task AcceptRepairAsync()
    {
        if (_repairBar is not { } bar || RepairChosen is not { } chosen)
        {
            return;
        }

        if (chosen.Draft.Edits.Any(edit => PerformanceTake.IsTaken(edit.NoteId)))
        {
            _marksStatus = Text["performance.takenNote"];
            return;
        }

        NoteEditLog before = _edits;
        NoteEditLog after;
        try
        {
            after = Corrections(before, chosen.Draft);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            _marksStatus = Text["repair.stale"];
            return;
        }

        await StopHearingRepairAsync();
        _repairBar = null;
        _repairUndo = (bar, before, after);
        await ApplyEditsAsync(after);
        _marksStatus = Text.Format("repair.accepted", bar);
    }

    /// <summary>The corrections with a draft's edits added, each an ordinary correction of the imported notes.</summary>
    private NoteEditLog Corrections(NoteEditLog log, AssistantDraft draft)
    {
        foreach (DraftEdit edit in draft.Edits)
        {
            log = edit.Kind switch
            {
                DraftEditKind.Remove => log.Remove(_imported, edit.NoteId),
                DraftEditKind.Change => log.Change(_imported, edit.NoteId, edit.After!.Value),
                _ => log.Add(_imported, edit.After!.Value, edit.Track, edit.Hand, out _),
            };
        }

        return log;
    }

    /// <summary>The bar of the last repair accepted, while no other correction came after it, so Undo takes it back whole.</summary>
    private int? RepairUndoable => _repairUndo is { } undo && undo.After.Equals(_edits) ? undo.Bar : null;

    /// <summary>Undo after Accept: every correction the repair added goes at once.</summary>
    private async Task UndoRepairAsync()
    {
        if (_repairUndo is not { } undo || !undo.After.Equals(_edits))
        {
            return;
        }

        _repairUndo = null;
        await ApplyEditsAsync(undo.Before);
        _marksStatus = Text.Format("repair.undone", undo.Bar);
    }
}