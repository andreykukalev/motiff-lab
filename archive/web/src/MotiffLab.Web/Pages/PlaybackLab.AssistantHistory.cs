using Microsoft.AspNetCore.Components;

using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.Web.Insights;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The full assistant's conversations kept on this device (an experiment, «Помощник у выделения и беседы», Settings ·
/// Experimental, version 0.6.1; #582 item 17.1, the lead's decision 4): with it on, each answered question is kept in the
/// piece's conversation (<see cref="ConversationRecord"/>, in IndexedDB beside the piece, never in the project file and
/// never synced), the assistant opens on the piece's latest conversation, and its list returns to an earlier one or starts
/// a new one. With it off, the conversation lasts as long as the page, as in 0.6.
/// </summary>
public partial class PlaybackLab
{
    public const string ContextSetting = "experimental.assistantContext";

    [Inject]
    private AssistantHistory History { get; set; } = default!;

    /// <summary>The conversation as kept; null until its first answer is kept.</summary>
    private ConversationRecord? _record;

    /// <summary>The piece's kept conversations, newest first.</summary>
    private IReadOnlyList<ConversationEntry> _conversations = [];

    /// <summary>The key the conversations of the piece the assistant follows are kept under.</summary>
    private string? _historyKey;

    /// <summary>Whether the popup at the selection and the kept conversations are on: the experiment, beside the conversation's.</summary>
    private bool ContextOn => MentorOn && SettingsStore.IsOn(ContextSetting);

    /// <summary>The key of the piece open now, as pencil marks key it ("project:id", or "piece:title"); null with the experiment off.</summary>
    private string? HistoryKey => ContextOn ? PencilMarks.KeyFor(_project?.Id, _title) : null;

    /// <summary>The kept conversations the full assistant lists; none with the experiment off.</summary>
    private IReadOnlyList<ConversationEntry> MentorConversations => ContextOn ? _conversations : [];

    /// <summary>
    /// The conversation follows the piece open now: another piece starts on its own latest conversation, or a new one. The
    /// same piece saved under a project for the first time keeps its conversation, kept under the project from then on.
    /// </summary>
    private async Task FollowPieceAsync()
    {
        string? key = HistoryKey;
        bool samePiece = ReferenceEquals(_mentorPiece, MentorSource);
        if (samePiece && _historyKey == key)
        {
            return;
        }

        _historyKey = key;
        if (samePiece)
        {
            _conversations = key is null ? [] : await History.ListAsync(key);
            return;
        }

        _mentorPiece = MentorSource;
        StartConversation();
        _conversations = key is null ? [] : await History.ListAsync(key);
        if (_conversations.Count > 0)
        {
            await OpenConversationAsync(_conversations[0].Id);
        }
    }

    /// <summary>A new, empty conversation in the role the assistant asks in now; the draft on show, if any, goes.</summary>
    private void StartConversation()
    {
        _mentor = AssistantSession.Start(AskRole);
        _mentorExchanges.Clear();
        _record = null;
        _aiDraft = null;
        _aiDrafts = [];
    }

    /// <summary>The full assistant opened: it follows the piece, so its list and latest conversation are there.</summary>
    private async Task MentorOpenedAsync()
    {
        await FollowPieceAsync();
        StateHasChanged();
    }

    /// <summary>An earlier conversation chosen in the list: it goes on from where it was left.</summary>
    private async Task OpenConversationAsync(string id)
    {
        if (_mentorBusy || await History.LoadAsync(id) is not { } record)
        {
            return;
        }

        StartConversation();
        _record = record;
        _mentor = record.Session;
        _mentorExchanges.AddRange(record.Exchanges.Select(static saved => saved.Note is { } note
            ? MentorExchange.Unanswered(saved.Question, note)
            : new MentorExchange(saved.Question, AnswerCheck.Parts(saved.Answer ?? ""), saved.Fallback, saved.Cited, null)));
    }

    /// <summary>New conversation in the list: the one on show stays kept, and the next question starts another.</summary>
    private void NewConversation()
    {
        if (!_mentorBusy)
        {
            StartConversation();
        }
    }

    /// <summary>The exchange added to the conversation as kept, with the session after it; one that went unanswered is not kept.</summary>
    private async Task KeepConversationAsync(MentorExchange exchange)
    {
        if (exchange.Note is not null || _historyKey is not { } key)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.Now;
        var saved = new SavedExchange(exchange.Question, new CheckedAnswer(exchange.Parts, exchange.Fallback, null).Text, exchange.Fallback, null, exchange.Cited);
        _record = (_record ?? ConversationRecord.Start($"c_{Guid.NewGuid():N}", _mentor.Role, now)).With(saved, _mentor, now);
        if (await History.SaveAsync(key, _record))
        {
            _conversations = await History.ListAsync(key);
        }
    }
}