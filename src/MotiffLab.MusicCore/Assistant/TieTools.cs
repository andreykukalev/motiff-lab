using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

using static MotiffLab.MusicCore.Assistant.ToolArguments;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>
/// Joining a tie the reading lost (#582 item 17.3; 0028 section 2, "join notes"): a note and the note of the same pitch
/// that starts exactly where it ends, in the same hand and track, become one note held through both, as a tie writes
/// it. The first note keeps its place and velocity; the second goes. Pure: the same notes and address give the same draft.
/// </summary>
internal static class TieTools
{
    public const string JoinTool = "notes.join";

    internal static IEnumerable<AssistantTool> All()
    {
        yield return new AssistantTool(
            JoinTool,
            ToolKind.ProposeDraft,
            "Joins a note with the note of the same pitch that starts exactly where it ends (same hand), as a tie: one note held through both. Use it for a tie the reading lost or a note repeated where it should be held. Nothing is applied.",
            ToolSchema.Object(
                ("bars", BarsSchema, "The bars the draft covers, at most 32; both notes lie in them."),
                ("address", ToolSchema.Text(NoteAddress.Pattern), "The first of the two notes (bar.hand.index).")),
            static (piece, parameters) => Joined(piece, parameters));
    }

    private static ToolResult Joined(AssistantPiece piece, JsonElement parameters)
    {
        Words w = piece.Words;
        if (RangeRefusal(piece, parameters) is { } range)
        {
            return ToolResult.Refused(range);
        }

        (int from, int to) = BarsOf(parameters);
        string? text = parameters.GetProperty("address").GetString();
        if (piece.Addresses.Resolve(text, w, out string? reason) is not { } first)
        {
            return ToolResult.Refused(reason ?? w.T("Joining needs the first note's address.", "Чтобы связать ноты, нужен адрес первой."));
        }

        RangeReading reading = piece.Read(from, to);
        IReadOnlyList<Note> before = reading.NotesOf(piece.Notes);
        if (!before.Any(note => note.Id == first.Id))
        {
            return ToolResult.Refused(w.T($"Note {text} is outside the bars of the draft.", $"Нота {text} вне тактов черновика."));
        }

        if (Next(before, first) is not { } second)
        {
            return ToolResult.Refused(w.T(
                $"No note of the same pitch starts where {text} ends, in the same hand and within the bars of the draft.",
                $"В тактах черновика нет ноты той же высоты в той же руке, которая начинается там, где кончается {text}."));
        }

        Note held = first with { Span = TickSpan.FromBounds(first.Span.Onset, second.Span.End), Attribution = Attribution.UserEdited };
        IReadOnlyList<Note> after = [.. before.Where(note => note.Id != second.Id).Select(note => note.Id == first.Id ? held : note)];
        (AssistantDraft? draft, string? refusal) = NoteTools.Made(piece, JoinTool, reading, before, after, new HashSet<KeepProperty>());
        return draft is not null ? ToolResult.Proposed([draft]) : ToolResult.Refused(refusal!);
    }

    /// <summary>The note a tie from <paramref name="first"/> would hold on to: the same pitch, track and hand, starting at its end.</summary>
    private static Note? Next(IReadOnlyList<Note> notes, Note first) => notes.FirstOrDefault(note =>
        note.Id != first.Id
        && note.Pitch == first.Pitch
        && note.Track == first.Track
        && note.Hand?.Value == first.Hand?.Value
        && note.Span.Onset == first.Span.End);
}