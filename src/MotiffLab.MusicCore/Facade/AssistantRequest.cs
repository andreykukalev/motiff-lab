using System.Text.Json;

using MotiffLab.MusicCore.Assistant;
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

    public static string Call(string name, Request request, AssistantPiece piece) => name switch
    {
        "assistant.call" => Run(request, piece),
        "assistant.digest" => Digest(request, piece),
        "assistant.address" => Address(request, piece),
        _ => throw new FacadeException("unknownCall", $"There is no call \"{name}\"."),
    };

    /// <summary>
    /// assistant.call {"tool", "parameters"}: the tool's answer, {"tool", "kind", "ok"} with "refusal" or "result" and
    /// "drafts". A refusal is an answer, not an error: it is what a model is told back.
    /// </summary>
    private static string Run(Request request, AssistantPiece piece)
    {
        string tool = request.Required("tool");
        JsonElement parameters = request.Element("parameters") ?? EmptyObject();
        ToolResult result = AssistantTools.Call(piece, tool, parameters);
        return Answer.Write(json => AssistantTools.WriteResult(json, piece, tool, result));
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