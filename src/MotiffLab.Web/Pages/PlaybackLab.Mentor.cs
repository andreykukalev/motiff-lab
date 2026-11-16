using System.Text.Json;

using Microsoft.AspNetCore.Components;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Import;
using MotiffLab.Web.Account;
using MotiffLab.Web.Insights;
using MotiffLab.Web.Understand;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The assistant's conversation (an experiment, Settings · Experimental, version 0.6; decision record 0028, section 4): with
/// it on, every Ask opens the mentor in the Insights sheet (<see cref="MentorPanel"/>). A question goes out only after the
/// player's consent and only when the API says a model is on for the account (GET /assistant/status); then the device sends
/// the passage's facts digest and notes (or the whole piece's digest) with the conversation, runs the model's read tools
/// here through music-core's registry and sends their results, until the answer, which is checked again here
/// (<see cref="AnswerCheck"/>). The device keeps the conversation (<see cref="AssistantSession"/>); the server keeps nothing.
/// Each cited fact is a chip that rings its places in the score.
/// </summary>
public partial class PlaybackLab
{
    public const string MentorSetting = "experimental.assistant";

    private AssistantSession _mentor = AssistantSession.Start(AssistantRole.Mentor);

    private readonly List<MentorExchange> _mentorExchanges = [];

    /// <summary>The piece the conversation is about; another piece starts a new one.</summary>
    private object? _mentorPiece;

    private bool _mentorBusy;

    /// <summary>How many drafts the conversation held before the question being answered.</summary>
    private int _mentorDraftsBefore;

    /// <summary>Whether the highlight on the score is a cited fact's, which the Insights stream does not show as its own.</summary>
    private bool _highlightCited;

    [Inject]
    private AssistantRelay Relay { get; set; } = default!;

    private bool MentorOn => SettingsStore.IsOn(MentorSetting);

    /// <summary>The conversation about the piece open now.</summary>
    private IReadOnlyList<MentorExchange> MentorExchanges => ReferenceEquals(_mentorPiece, MentorSource) ? _mentorExchanges : [];

    /// <summary>The piece as loaded (the demo too), whose notes, as corrected, the mentor reads.</summary>
    private ImportedPiece? MentorSource => _sectionSource ?? CurrentPiece.Piece;

    private async Task AskMentorAsync(string question)
    {
        if (_mentorBusy)
        {
            return;
        }

        await FollowPieceAsync();
        if (_mentor.Role != AskRole)
        {
            // A conversation keeps the tools of its role: another role starts a new one.
            StartConversation();
        }

        _mentorBusy = true;
        StateHasChanged();
        try
        {
            (MentorExchange exchange, AssistantSession? session) = await MentorAnswerAsync(_mentor, question);
            if (session is not null)
            {
                _mentor = session;
                await OfferAiDraftsAsync(MentorPiece()!, session, _mentorDraftsBefore, inConversation: true);
            }

            _mentorExchanges.Add(exchange);
            await KeepConversationAsync(exchange);
        }
        finally
        {
            _mentorBusy = false;
        }
    }

    /// <summary>The piece as the assistant's tools read it now, or null with no piece open.</summary>
    private AssistantPiece? MentorPiece() =>
        MentorSource is { } imported && _notes is not null && _meter is not null ? new AssistantPiece(imported, _notes, _project?.Check ?? [], Words) : null;

    /// <summary>
    /// The answer to <paramref name="question"/> asked in <paramref name="from"/>, with the session after it; or why there is
    /// none, and no session: nothing is sent without consent, a model on for the account and a passage the turn can carry.
    /// </summary>
    private async Task<(MentorExchange Exchange, AssistantSession? Session)> MentorAnswerAsync(AssistantSession from, string question)
    {
        if (!await MentorConsent.GivenAsync(Preferences))
        {
            return (MentorExchange.Unanswered(question, "mentor.noConsent"), null);
        }

        (AssistantStatus? status, RelayProblem problem) = await Relay.StatusAsync();
        if (StatusNote(status, problem) is { } note)
        {
            return (MentorExchange.Unanswered(question, note), null);
        }

        if (MentorPiece() is not { } piece)
        {
            return (MentorExchange.Unanswered(question, "mentor.noPiece"), null);
        }

        return MentorContext(piece) is { } context
            ? await ConverseAsync(from, piece, context, question)
            : (MentorExchange.Unanswered(question, "mentor.tooManyBars"), null);
    }

    /// <summary>Why the status forbids sending, as a UI text key; null when a question may go.</summary>
    private static string? StatusNote(AssistantStatus? status, RelayProblem problem) => status switch
    {
        null => ProblemNote(problem),
        { Configured: false } => "mentor.notConfigured",
        { Allowed: false } => "mentor.notAllowed",
        { Pro: false } => "mentor.pro",
        { Left: <= 0 } => "mentor.dailyCap",
        _ => null,
    };

    private static string ProblemNote(RelayProblem problem) => problem switch
    {
        RelayProblem.NotSignedIn => "mentor.signIn",
        RelayProblem.NotAllowed => "mentor.notAllowed",
        RelayProblem.Pro => "mentor.pro",
        RelayProblem.TooLarge => "mentor.tooLarge",
        RelayProblem.DailyCap => "mentor.dailyCap",
        RelayProblem.Refused => "mentor.refused",
        _ => "mentor.unreachable",
    };

    /// <summary>
    /// The context of the question (music-core's <see cref="AssistantContext.OfQuestion"/>): the selected bars' digest and
    /// notes, or the whole piece's digest when that is the scope; null when the selection is longer than a range may be.
    /// </summary>
    private JsonElement? MentorContext(AssistantPiece piece) => AssistantContext.OfQuestion(piece, _selection?.Span, _askScope);

    /// <summary>
    /// One question's turns in <paramref name="from"/>: the device sends the turn, runs the tool calls that come back and
    /// sends their results, until the answer. The session after it comes back only with the answer, so a turn that fails
    /// leaves the conversation as it was.
    /// </summary>
    private async Task<(MentorExchange Exchange, AssistantSession? Session)> ConverseAsync(AssistantSession from, AssistantPiece piece, JsonElement context, string question)
    {
        AssistantSession session = from.Ask(question);
        _mentorDraftsBefore = session.Drafts.Count;
        for (int step = 0; step <= AssistantRoles.MostToolCalls + 1; step++)
        {
            TurnRequest request = session.Request(Text.Language, context);
            if (request.ToJson().Length > TurnRequest.MaxBytes)
            {
                return (MentorExchange.Unanswered(question, "mentor.tooLarge"), null);
            }

            (TurnReply? reply, RelayProblem problem) = await Relay.TurnAsync(request);
            switch (reply?.Kind)
            {
                case null:
                    return (MentorExchange.Unanswered(question, ProblemNote(problem)), null);
                case TurnReplyKind.NotConfigured:
                    return (MentorExchange.Unanswered(question, "mentor.notConfigured"), null);
                case TurnReplyKind.ToolCalls:
                    int before = session.ToolCallsInQuestion;
                    session = session.Called(reply.Calls).Resulted(TurnRunner.Run(piece, session.Role, reply.Calls, before));
                    continue;
                default:
                    CheckedAnswer answer = AnswerCheck.Check(reply.Text, request.KnownIds, context, Words, request.Drafted);
                    AssistantSession answered = session.Answered(answer.Text);
                    var exchange = new MentorExchange(question, answer.Parts, reply.Fallback || answer.FellBack, CitedFacts.In(piece, answer.Parts, context, answered.ResultsInQuestion), null);
                    return (exchange, answered);
            }
        }

        return (MentorExchange.Unanswered(question, "mentor.unreachable"), null);
    }

    /// <summary>A cited fact tapped: its places are ringed in the score and the playhead goes to the first, as a fact of the stream does.</summary>
    private async Task ShowCitedAsync(CitedFact cited)
    {
        if (cited.Places.Count == 0)
        {
            return;
        }

        await ChooseFactAsync(new FactChoice(new MotifFact(cited.Id, FactLens.Structure, FactCertainty.Inferred, cited.Text, "") { Places = cited.Places }));
        _highlightCited = true;
    }
}