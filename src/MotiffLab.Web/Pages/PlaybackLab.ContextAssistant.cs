using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.Web.Insights;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The contextual assistant (an experiment, «Помощник у выделения и беседы», Settings · Experimental, version 0.6.1; #582
/// item 17.2, the lead's decisions 2 and 5): with it on, Ask on a selection opens a compact popup there
/// (<see cref="ContextAssistant"/>) rather than the full assistant. Each question in it is a conversation of its own, with
/// the selection as its context and the tools of the role the assistant asks in, so a request to change the bars comes
/// back as a draft over them (#582 item 17.3); nothing of it is kept. Add to context moves the question and answer, with
/// the facts it cites, into the full assistant's conversation about the piece, which keeps them (#582 item 17.1).
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The popup's last question and what came of it; null before the first.</summary>
    private MentorExchange? _contextExchange;

    private bool _contextBusy;

    /// <summary>Whether the popup's exchange went into the full assistant's conversation.</summary>
    private bool _contextAdded;

    /// <summary>Whether Ask at the place it was pressed opens the popup: the experiment is on and the question is about a selection.</summary>
    private bool OpensContext(bool wholePiece) => ContextOn && !wholePiece && _selection is not null;

    private async Task AskInContextAsync(string question)
    {
        if (_contextBusy)
        {
            return;
        }

        await DropContextDraftAsync();
        _contextBusy = true;
        _contextAdded = false;
        _contextExchange = null;
        StateHasChanged();
        try
        {
            (MentorExchange exchange, AssistantSession? session) = await MentorAnswerAsync(AssistantSession.Start(AskRole), question);
            _contextExchange = exchange;
            if (session is not null && MentorPiece() is { } piece)
            {
                await OfferAiDraftsAsync(piece, session, 0, inConversation: false);
            }
        }
        finally
        {
            _contextBusy = false;
        }
    }

    /// <summary>
    /// Add to context: the question and its answer, as checked and with its cited facts, join the full assistant's
    /// conversation about the piece as one of its exchanges, and are kept with it; the next question there goes on from them.
    /// </summary>
    private async Task AddToContextAsync()
    {
        if (_contextExchange is not { Note: null } exchange || _contextAdded || _mentorBusy)
        {
            return;
        }

        await FollowPieceAsync();
        if (_mentor.Role != AskRole)
        {
            StartConversation();
        }

        _mentor = _mentor.Ask(exchange.Question).Answered(new CheckedAnswer(exchange.Parts, exchange.Fallback, null).Text);
        _mentorExchanges.Add(exchange);
        await KeepConversationAsync(exchange);
        _contextAdded = true;
    }

    /// <summary>The popup's draft, if one is on show, goes with the popup: nothing changed.</summary>
    private async Task DropContextDraftAsync()
    {
        if (_aiDraft is { InConversation: false })
        {
            await CancelAiDraftAsync();
        }
    }

    private void ForgetContext()
    {
        _contextExchange = null;
        _contextAdded = false;
    }
}