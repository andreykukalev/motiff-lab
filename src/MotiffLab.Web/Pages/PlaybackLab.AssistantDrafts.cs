using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The assistant as an action interface (an experiment, «Помощник правит ноты», Settings · Experimental, version 0.6.1;
/// #582 item 17.3, the lead's decision 3): with it on, the assistant is the proposer, which may call the tools that make
/// drafts (a repair by Check reason, a variation, a note edit, a joined tie). The device runs each call in music-core;
/// the drafts of the question's calls are made again from their calls (tools are pure) and the first is shown as the
/// repairs show theirs: the bars engraved with it as a plum ossia, heard As read, as the Draft or in turn, with what
/// changes written by the engine. Accept adds its edits to My corrections as ordinary corrections, one Undo takes them all
/// back; Cancel drops it. Nothing changes until Accept, and the model never writes a note.
/// </summary>
public partial class PlaybackLab
{
    public const string ActionsSetting = "experimental.assistantActions";

    /// <summary>A draft of the assistant on show: the call that made it, its notes as made now, its bars, and the question it answers.</summary>
    private sealed record AiDraft(SessionDraft Call, AssistantDraft Made, TickSpan Measure);

    /// <summary>The assistant's draft on show; null when none is.</summary>
    private AiDraft? _aiDraft;

    /// <summary>The drafts the last question's calls made, to choose among; the first is shown.</summary>
    private IReadOnlyList<AiDraft> _aiDrafts = [];

    private (AssistantDraft Draft, IReadOnlyList<Note>? Notes, string Language, PhraseCardSource? Card)? _aiDraftCard;

    /// <summary>Whether the assistant may act: the experiment is on, beside the conversation's.</summary>
    private bool ActionsOn => SettingsStore.IsOn(ActionsSetting);

    /// <summary>The role a new conversation takes: the proposer when the assistant may act, else the mentor.</summary>
    private AssistantRole AskRole => ActionsOn ? AssistantRole.Proposer : AssistantRole.Mentor;

    /// <summary>The draft's bars engraved with it above them.</summary>
    private PhraseCardSource? AiDraftCard
    {
        get
        {
            if (_aiDraft is not { } shown)
            {
                return null;
            }

            if (_aiDraftCard is { } cached && ReferenceEquals(cached.Draft, shown.Made) && ReferenceEquals(cached.Notes, _notes) && cached.Language == Text.Language)
            {
                return cached.Card;
            }

            PhraseCardSource? card = DraftCardOf(shown.Made, shown.Measure, Text["assistantDraft.ossia"]);
            _aiDraftCard = (shown.Made, _notes, Text.Language, card);
            return card;
        }
    }

    /// <summary>The titles of the drafts to choose among: the engine's, or "Way 1", "Way 2".</summary>
    private IReadOnlyList<string> AiDraftWays => [.. _aiDrafts.Select((draft, index) => draft.Made.Title ?? Text.Format("assistantDraft.way", index + 1))];

    private int AiDraftIndex => _aiDraft is { } shown ? Math.Max(0, _aiDrafts.ToList().IndexOf(shown)) : 0;

    /// <summary>"Bars 3–4": the bars the draft covers.</summary>
    private string AiDraftBars => _aiDraft is { } shown && _meter is not null ? BarsOf(shown.Measure) : "";

    /// <summary>
    /// The drafts the question's calls made (those after <paramref name="before"/> in the conversation), made again over the
    /// notes as they are; the first is shown, and the repair on show, if any, gives way to it.
    /// </summary>
    private async Task OfferAiDraftsAsync(AssistantPiece piece, int before)
    {
        if (_meter is null)
        {
            return;
        }

        MeterMap meter = _meter;
        _aiDrafts = [.. _mentor.Drafts.Skip(before)
            .Select(call => call.Make(piece) is { } made ? new AiDraft(call, made, made.Measure(meter)) : null)
            .OfType<AiDraft>()];
        if (_aiDrafts.Count == 0)
        {
            return;
        }

        await StopHearingRepairAsync();
        _repairBar = null;
        _aiDraft = _aiDrafts[0];
        await SetRepairSourceAsync();
    }

    private async Task ChooseAiDraftAsync(int index)
    {
        if (index < 0 || index >= _aiDrafts.Count)
        {
            return;
        }

        RepairHearing heard = RepairHeard;
        _aiDraft = _aiDrafts[index];
        await SetRepairSourceAsync();
        if (heard is RepairHearing.Draft or RepairHearing.InTurn)
        {
            _repairHearing = RepairHearing.None;
            await HearRepairAsync(heard);
        }
    }

    /// <summary>Accept: the draft's edits added to the corrections one by one, as a repair's are; one Undo takes them all back.</summary>
    private async Task AcceptAiDraftAsync()
    {
        if (_aiDraft is not { } shown || _meter is null)
        {
            return;
        }

        if (shown.Made.Edits.Any(edit => PerformanceTake.IsTaken(edit.NoteId)))
        {
            _marksStatus = Text["performance.takenNote"];
            return;
        }

        NoteEditLog before = _edits;
        NoteEditLog after;
        try
        {
            after = Corrections(before, shown.Made);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            _marksStatus = Text["assistantDraft.stale"];
            return;
        }

        string bars = AiDraftBars;
        await StopHearingRepairAsync();
        CloseAiDraft(DraftState.Accepted);
        _repairUndo = (BarRange.Of(_meter, shown.Measure).FirstBar, before, after);
        await ApplyEditsAsync(after);
        _marksStatus = Text.Format("assistantDraft.accepted", bars);
    }

    /// <summary>Cancel: the draft goes, and nothing changed.</summary>
    private async Task CancelAiDraftAsync()
    {
        await StopHearingRepairAsync();
        CloseAiDraft(DraftState.Dismissed);
    }

    private void CloseAiDraft(DraftState state)
    {
        foreach (AiDraft offered in _aiDrafts)
        {
            _mentor = _mentor.Decided(offered.Call.Id, offered == _aiDraft ? state : DraftState.Dismissed);
        }

        _aiDraft = null;
        _aiDrafts = [];
    }
}