using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Assistant.Drafts;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The assistant's calls in a draft (decision record 0034, section 6; version 1.2, within contract 13): what a turn sends
/// about a place, a tool run on it, and the player's «Принять» of what a tool proposed. Each names the place by "draft",
/// "place" and, optionally, "variant" (the variant built from; else the place's first that is not its base). Nothing here
/// talks to a model; only <see cref="Accept"/> changes the draft, and only as the player asks.
/// </summary>
internal static class DraftAssistantRequest
{
    /// <summary>
    /// drafts.context: {"context": …}, what a turn in the draft sends as its "context" (<see cref="AssistantContext.OfDraft"/>):
    /// the place's digest, its base's notes and the variant's; a bad request for a place or a variant of more than 32 bars.
    /// </summary>
    public static string Context(OpenPiece owner, Request request)
    {
        DraftScope scope = ScopeOf(owner, request);
        return Answer.Write(json =>
        {
            json.WritePropertyName("context");
            AssistantContext.OfDraft(scope).WriteTo(json);
        });
    }

    /// <summary>
    /// drafts.call {"tool", "parameters", "role"?, "now"?}: the tool's answer as assistant.call gives it, run on the place (its
    /// base for a piece's reading tools); a draft's tool proposes as one entry of "drafts" (<see cref="DraftProposal.Write"/>).
    /// </summary>
    public static string Call(OpenPiece owner, Request request) => AssistantRequest.Run(request, ScopeOf(owner, request).Base);

    /// <summary>
    /// drafts.accept {"tool", "parameters", "role"?, "now"}: «Принять» — the same call made again on the draft as it is now
    /// and what it proposes kept: its steps, marked as the assistant's, or a bridge place and its base. Answers the sources
    /// with "draft", "place", "steps" (the numbers of the steps it added), "structure" and, for a bridge, "base"; a bad request
    /// when the call no longer proposes anything (the draft changed under it). Undone by drafts.undo, as one.
    /// </summary>
    public static string Accept(OpenPiece owner, Request request)
    {
        DraftProposal proposal = Proposal(owner, request);
        string answer = owner.Changed(request, piece => piece.ChangeDraft(proposal.After.Id, _ => proposal.After), json =>
        {
            json.WriteString("draft", proposal.After.Id);
            json.WriteString("place", proposal.Place);
            Answer.Numbers(json, "steps", [.. proposal.Steps.Select(static step => step.Number)]);
            json.WriteBoolean("structure", proposal.Structure);
            if (proposal.Structure)
            {
                json.WriteString("base", proposal.After.FindPlace(proposal.Place)!.Base);
            }
        });
        if (proposal.Structure)
        {
            owner.KeepDraftBefore(proposal.Before);
        }

        return answer;
    }

    /// <summary>What the call "tool" with "parameters" proposes on the place now; a bad request when it refuses or proposes nothing.</summary>
    public static DraftProposal Proposal(OpenPiece owner, Request request)
    {
        DraftScope scope = ScopeOf(owner, request);
        ToolResult result = AssistantRequest.Result(request, request.Required("tool"), scope.Base);
        return result.Proposal ?? throw FacadeException.BadRequest(result.Refusal ?? owner.Words.T("The call proposes nothing to accept.", "Этот вызов ничего не предлагает."));
    }

    private static DraftScope ScopeOf(OpenPiece owner, Request request)
    {
        try
        {
            return DraftScope.Of(owner.Project, request.Required("draft"), request.Required("place"), request.Text("variant"), SourceRequest.Now(request), owner.Words);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }
}