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

        if (!ReferenceEquals(_mentorPiece, MentorSource))
        {
            _mentorPiece = MentorSource;
            _mentor = AssistantSession.Start(AssistantRole.Mentor);
            _mentorExchanges.Clear();
        }

        _mentorBusy = true;
        StateHasChanged();
        try
        {
            _mentorExchanges.Add(await MentorAnswerAsync(question));
        }
        finally
        {
            _mentorBusy = false;
        }
    }

    /// <summary>The answer, or why there is none; nothing is sent without consent, a model on for the account and a passage the turn can carry.</summary>
    private async Task<MentorExchange> MentorAnswerAsync(string question)
    {
        if (await Preferences.GetAsync(MentorPanel.ConsentKey) != MentorPanel.Consented)
        {
            return MentorExchange.Unanswered(question, "mentor.noConsent");
        }

        (AssistantStatus? status, RelayProblem problem) = await Relay.StatusAsync();
        if (StatusNote(status, problem) is { } note)
        {
            return MentorExchange.Unanswered(question, note);
        }

        if (MentorSource is not { } imported || _notes is null || _meter is null)
        {
            return MentorExchange.Unanswered(question, "mentor.noPiece");
        }

        var piece = new AssistantPiece(imported, _notes, _project?.Check ?? [], Words);
        return MentorContext(piece) is { } context
            ? await ConverseAsync(piece, context, question)
            : MentorExchange.Unanswered(question, "mentor.tooManyBars");
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

    /// <summary>The context of the question: the selection's digest and notes, or the whole piece's digest; null when the selection is longer than a range may be.</summary>
    private JsonElement? MentorContext(AssistantPiece piece)
    {
        if (_askWholePiece || _selection is not { } selection)
        {
            return AssistantContext.OfPiece(piece);
        }

        int from = _meter!.BarAt(selection.Span.Onset).Number;
        int to = _meter.BarAt(new MusicCore.Time.Tick(Math.Max(selection.Span.Onset.Value, selection.Span.End.Value - 1))).Number;
        return piece.RangeRefusal(from, to) is null ? AssistantContext.OfRange(piece, from, to) : null;
    }

    /// <summary>
    /// One question's turns: the device sends the turn, runs the tool calls that come back and sends their results, until the
    /// answer. The conversation keeps the question only once it is answered, so a turn that fails leaves it as it was.
    /// </summary>
    private async Task<MentorExchange> ConverseAsync(AssistantPiece piece, JsonElement context, string question)
    {
        AssistantSession session = _mentor.Ask(question);
        for (int step = 0; step <= AssistantRoles.MostToolCalls + 1; step++)
        {
            TurnRequest request = session.Request(Text.Language, context);
            if (request.ToJson().Length > TurnRequest.MaxBytes)
            {
                return MentorExchange.Unanswered(question, "mentor.tooLarge");
            }

            (TurnReply? reply, RelayProblem problem) = await Relay.TurnAsync(request);
            switch (reply?.Kind)
            {
                case null:
                    return MentorExchange.Unanswered(question, ProblemNote(problem));
                case TurnReplyKind.NotConfigured:
                    return MentorExchange.Unanswered(question, "mentor.notConfigured");
                case TurnReplyKind.ToolCalls:
                    int before = session.ToolCallsInQuestion;
                    session = session.Called(reply.Calls).Resulted(TurnRunner.Run(piece, session.Role, reply.Calls, before));
                    continue;
                default:
                    CheckedAnswer answer = AnswerCheck.Check(reply.Text, request.KnownIds, context, Words);
                    _mentor = session.Answered(answer.Text);
                    return new MentorExchange(question, answer.Parts, reply.Fallback || answer.FellBack, CitedFacts.In(piece, answer.Parts, context, _mentor.ResultsInQuestion), null);
            }
        }

        return MentorExchange.Unanswered(question, "mentor.unreachable");
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