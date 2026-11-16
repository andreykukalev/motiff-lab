using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>What a draft edit does to one note.</summary>
public enum DraftEditKind
{
    /// <summary>The note's pitch, timing or velocity change.</summary>
    Change,

    /// <summary>The note goes.</summary>
    Remove,

    /// <summary>A note is added.</summary>
    Add,
}

/// <summary>
/// One edit of a draft as a correction the player can accept (0028 section 3): the same shape as the corrections log's
/// edits (<see cref="NoteEdit"/>), so an accepted draft becomes ordinary corrections, undone one by one like any other.
/// </summary>
/// <param name="NoteId">The note changed or removed; for an added note, the draft's own id for it.</param>
/// <param name="Before">The note's values now; null for an added note.</param>
/// <param name="After">The values the draft gives it; null for a removed note.</param>
public sealed record DraftEdit(DraftEditKind Kind, string NoteId, NoteValues? Before, NoteValues? After)
{
    /// <summary>The track an added note goes in.</summary>
    public int Track { get; init; }

    /// <summary>The hand an added note is played with, when the piece has hands.</summary>
    public Hand? Hand { get; init; }
}

/// <summary>
/// A proposal of notes (the AI companion study, section 4.1): what music-core made when a tool was called, never applied
/// to the piece. It holds the notes before and after, the diff, the Keep checks, the rules it raised, the explanation
/// written only from the diff (<see cref="VariationFacts"/>), and the same change as corrections (<see cref="Edits"/>) for
/// when the player accepts it. Only the player accepts a draft; no tool does.
/// </summary>
/// <param name="Tool">The tool that made it, for example "variation.simplify" or "repair.candidates".</param>
/// <param name="Before">The notes the draft was made from: the range's notes as they are.</param>
public sealed record AssistantDraft(string Tool, IReadOnlyList<Note> Before, Variation Variation)
{
    /// <summary>The draft as corrections: removed notes first, then changed notes, then added ones, each in time order.</summary>
    public IReadOnlyList<DraftEdit> Edits { get; } = EditsOf(Variation);

    /// <summary>A title for the draft's button, such as «Убрать повторную E5»; null when the tool gives none.</summary>
    public string? Title { get; init; }

    /// <summary>True when the draft changes nothing.</summary>
    public bool IsEmpty => Edits.Count == 0;

    /// <summary>The whole bars the draft covers (its range's notes before, and its notes after): the app engraves and loops them.</summary>
    /// <exception cref="InvalidOperationException">The draft has no note before or after.</exception>
    public TickSpan Measure(MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        Note[] notes = [.. Before, .. Variation.Notes];
        if (notes.Length == 0)
        {
            throw new InvalidOperationException("A draft with no notes has no bars.");
        }

        BarRange bars = BarRange.Of(meter, TickSpan.FromBounds(notes.Min(static note => note.Span.Onset), notes.Max(static note => note.Span.End)));
        return TickSpan.FromBounds(meter.GetBar(bars.FirstBar).Span.Onset, meter.GetBar(bars.LastBar).Span.End);
    }

    private static DraftEdit[] EditsOf(Variation variation) =>
    [
        .. variation.Removed.Select(static note => new DraftEdit(DraftEditKind.Remove, note.Id, NoteValues.Of(note), null)),
        .. variation.Changes
            .Where(static change => NoteValues.Of(change.Before) != NoteValues.Of(change.After))
            .Select(static change => new DraftEdit(DraftEditKind.Change, change.Before.Id, NoteValues.Of(change.Before), NoteValues.Of(change.After))),
        .. variation.Added.Select(static note => new DraftEdit(DraftEditKind.Add, note.Id, null, NoteValues.Of(note)) { Track = note.Track, Hand = note.Hand?.Value }),
    ];

    /// <summary>Writes the draft: its edits by note address, the explanation, the Keep checks and the rules' flags. Notes are named by address, never by id.</summary>
    public void Write(Utf8JsonWriter json, AssistantPiece piece)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(piece);
        json.WriteStartObject();
        json.WriteString("tool", Tool);
        json.WriteBoolean("applied", false);
        if (Title is not null)
        {
            json.WriteString("title", Title);
        }

        json.WriteString("explanation", Variation.Explanation);
        json.WriteStartArray("edits");
        foreach (DraftEdit edit in Edits)
        {
            json.WriteStartObject();
            json.WriteString("kind", ToolSchema.Camel(edit.Kind.ToString()));
            if (piece.Addresses.TextOf(edit.NoteId) is { } address)
            {
                json.WriteString("address", address);
            }

            if (edit.Before is { } before)
            {
                WriteValues(json, "before", before, piece);
            }

            if (edit.After is { } after)
            {
                WriteValues(json, "after", after, piece);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartObject("keep");
        foreach (KeepCheck check in Variation.Checks)
        {
            json.WriteString(ToolSchema.Camel(check.Property.ToString()), ToolSchema.Camel(check.Status.ToString()));
        }

        json.WriteEndObject();
        json.WriteStartArray("flags");
        foreach (string flag in Variation.Flags)
        {
            json.WriteStringValue(flag);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteValues(Utf8JsonWriter json, string name, NoteValues values, AssistantPiece piece)
    {
        json.WriteStartObject(name);
        json.WriteString("pitch", values.Pitch.ToString());
        json.WriteNumber("bar", piece.Meter.BarAt(values.Span.Onset).Number);
        json.WriteString("at", NoteAddressBook.Fraction(piece.Addresses.InBar(values.Span.Onset)));
        json.WriteString("length", NoteAddressBook.Fraction(values.Span.Duration));
        json.WriteNumber("velocity", values.Velocity);
        json.WriteEndObject();
    }
}