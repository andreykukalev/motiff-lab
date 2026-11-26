using System.Text.Json;

using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The assistant's calls (version 0.6, 0028; within contract 12): the tool registry on piece 0, and on an open piece a
/// tool run, the facts digest and note addresses. Nothing here talks to a model or changes the piece: a tool answers what
/// it read or its drafts, and the app shows a draft for the player to accept.
/// </summary>
internal static class AssistantRequest
{
    /// <summary>assistant.tools: {"tools": [{name, kind, description, parameters}]}, the same for every request.</summary>
    public static string Tools() => Answer.Write(AssistantTools.WriteCatalogue);

    /// <summary>The assistant's calls on an open piece (<see cref="CallRegistry.OnPiece"/>), on what its tools read of it.</summary>
    public static readonly FacadeCall<AssistantPiece>[] Calls =
    [
        new("assistant.call", static (piece, request) => Run(request, piece)),
        new("assistant.digest", static (piece, request) => Digest(request, piece)),
        new("assistant.address", static (piece, request) => Address(request, piece)),
        new("assistant.context", static (piece, request) => Context(request, piece)),
    ];

    /// <summary>
    /// assistant.call {"tool", "parameters", "role"?}: the tool's answer, {"tool", "kind", "ok"} with "refusal" or "result" and
    /// "drafts". A refusal is an answer, not an error: it is what a model is told back. With "role" ("mentor" or "proposer",
    /// the iPad's turn), the call is first checked as the relay checks it (<see cref="AssistantRoles.CallRefusal"/>).
    /// </summary>
    internal static string Run(Request request, AssistantPiece piece)
    {
        string tool = request.Required("tool");
        ToolResult result = Result(request, tool, piece);
        return Answer.Write(json => AssistantTools.WriteResult(json, piece, tool, result));
    }

    /// <summary>The answer of the call "tool" with "parameters" on <paramref name="piece"/>, checked first for "role" when there is one.</summary>
    internal static ToolResult Result(Request request, string tool, AssistantPiece piece)
    {
        JsonElement parameters = request.Element("parameters") ?? EmptyObject();
        return RoleRefusal(request, tool, parameters, piece) is { } refusal ? ToolResult.Refused(refusal) : AssistantTools.Call(piece, tool, parameters);
    }

    private static string? RoleRefusal(Request request, string tool, JsonElement parameters, AssistantPiece piece)
    {
        if (request.Text("role") is not { } id)
        {
            return null;
        }

        AssistantRole role = AssistantRoles.Parse(id) ?? throw FacadeException.BadRequest($"There is no role \"{id}\".");
        return AssistantRoles.CallRefusal(role, new ToolCall("call", tool, parameters), piece.Words);
    }

    /// <summary>assistant.digest {"bars": {"from", "to"}, "hand"}: {"digest": …} of the bars, or of the whole piece without "bars".</summary>
    private static string Digest(Request request, AssistantPiece piece)
    {
        FactsDigest digest;
        if (request.Has("bars"))
        {
            Request bars = request.Object("bars");
            int from = bars.Int("from") ?? throw FacadeException.BadRequest("\"bars\" needs \"from\".");
            int to = bars.Int("to") ?? throw FacadeException.BadRequest("\"bars\" needs \"to\".");
            if (piece.RangeRefusal(from, to) is { } refusal)
            {
                throw FacadeException.BadRequest(refusal);
            }

            digest = FactsDigest.OfRange(piece, from, to, request.Enum<Hand>("hand"));
        }
        else
        {
            digest = FactsDigest.OfPiece(piece);
        }

        return Answer.Write(json =>
        {
            json.WritePropertyName("digest");
            digest.Write(json);
        });
    }

    /// <summary>
    /// assistant.context {"selection": {"onset", "end"} in ticks, or absent for nothing selected; "wholePiece": the player
    /// chose the whole piece for this selection}: {"context": …}, what a turn sends as its "context"
    /// (<see cref="Assistant.Conversation.AssistantContext.OfQuestion"/>), the bars the selection touches or the piece's
    /// digest; an error when the selection spans more bars than a range may hold.
    /// </summary>
    private static string Context(Request request, AssistantPiece piece)
    {
        Time.TickSpan? selection = null;
        if (request.Has("selection"))
        {
            Request span = request.Object("selection");
            long onset = span.Long("onset") ?? throw FacadeException.BadRequest("\"selection\" needs \"onset\".");
            long end = span.Long("end") ?? throw FacadeException.BadRequest("\"selection\" needs \"end\".");
            if (onset < 0 || end <= onset)
            {
                throw FacadeException.BadRequest("A selection ends after it starts.");
            }

            selection = Time.TickSpan.FromBounds(new Time.Tick(onset), new Time.Tick(end));
        }

        var scope = Assistant.Conversation.AskScope.Choose(request.Bool("wholePiece") ?? false, selection);
        JsonElement context = Assistant.Conversation.AssistantContext.OfQuestion(piece, selection, scope)
            ?? throw FacadeException.BadRequest(piece.Words.T("Select fewer bars: a question takes at most 32.", "Выделите меньше тактов: вопрос берёт не больше 32."));
        return Answer.Write(json =>
        {
            json.WritePropertyName("context");
            context.WriteTo(json);
        });
    }

    /// <summary>assistant.address {"address": "6.L.1"} or {"noteId"}: {"address", "noteId"}, so the app highlights the note an answer names.</summary>
    private static string Address(Request request, AssistantPiece piece)
    {
        if (request.Text("noteId") is { } id)
        {
            string address = piece.Addresses.TextOf(id) ?? throw FacadeException.BadRequest($"Note {id} is not in the piece.");
            return Answer.Write(json =>
            {
                json.WriteString("address", address);
                json.WriteString("noteId", id);
            });
        }

        string text = request.Required("address");
        Note note = piece.Addresses.Resolve(text, piece.Words, out string? reason) ?? throw FacadeException.BadRequest(reason!);
        return Answer.Write(json =>
        {
            json.WriteString("address", piece.Addresses.TextOf(note.Id));
            json.WriteString("noteId", note.Id);
        });
    }

    private static JsonElement EmptyObject()
    {
        using JsonDocument empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }
}