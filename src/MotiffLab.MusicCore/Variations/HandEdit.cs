using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>What one hand edit does to a variation's notes.</summary>
public enum HandEditKind
{
    /// <summary>Changes a note's pitch, timing or velocity.</summary>
    Change,

    /// <summary>Takes a note out.</summary>
    Remove,

    /// <summary>Adds a note.</summary>
    Add,
}

/// <summary>One edit of one note (S1-E7).</summary>
/// <param name="NoteId">The note changed or removed, or the id of the note added.</param>
/// <param name="Values">The note's new values; null for a removal.</param>
public sealed record HandEditParameters(HandEditKind Kind, string NoteId, NoteValues? Values) : OperationParameters
{
    public static HandEditParameters Change(string noteId, NoteValues values) => new(HandEditKind.Change, noteId, values);

    public static HandEditParameters Remove(string noteId) => new(HandEditKind.Remove, noteId, null);

    public static HandEditParameters Add(string noteId, NoteValues values) => new(HandEditKind.Add, noteId, values);
}

/// <summary>
/// A note edited by hand in a variation (S1-E7): a step like any other in the chain, so it can be
/// undone, redone and saved, and the Keep chips are checked again after it (S1-E9). A variation made
/// only of hand edits is a hand-made variation, a first-class result.
/// </summary>
/// <remarks>
/// Keep choices never block a hand edit: a kept property it breaks shows "no longer kept" on its
/// chip. The edit must leave a playable note: inside the piano's range, with a velocity of 1–127,
/// starting no earlier than the selection when it moves. Edited and added notes
/// are marked user-edited; an added note takes the track, hand and voice of the note sounding
/// before it, or of the first note. Pure: the same notes and edit give the same result.
/// </remarks>
public sealed class HandEdit : IVariationOperation
{
    /// <summary>The step's id in a chain; hand edits are not a catalogue operation, so it is the requirement's.</summary>
    public const string OperationId = "E7";

    public static HandEdit Instance { get; } = new();

    public string Id => OperationId;

    public string Name => "Edit by hand";

    public string NameIn(Words words) => words.T(Name, "Правка вручную");

    public string? DisabledReason(IReadOnlySet<KeepProperty> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        return null;
    }

    public string? DisabledReason(IReadOnlySet<KeepProperty> keep, Words words) => DisabledReason(keep);

    /// <summary>A hand edit names its note, so it has no scope.</summary>
    public bool Supports(OperationScope scope) => scope == OperationScope.All;

    /// <summary>The first free id for an added note, "h1", "h2" and so on, not used by any of <paramref name="notes"/>.</summary>
    public static string NewId(IEnumerable<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var used = notes.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        int n = 1;
        while (used.Contains($"h{n}"))
        {
            n++;
        }

        return $"h{n}";
    }

    public string? UnavailableReason(IReadOnlyList<Note> notes, VariationContext context, OperationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(context);
        Words w = context.Words;
        HandEditParameters edit = Cast(parameters);
        if (edit.Scope != OperationScope.All)
        {
            return w.T(
                $"{Name} names its note, so it can't be limited to {OperationScopes.Describe(edit.Scope)}.",
                $"«{NameIn(w)}» относится к одной ноте: ограничить её слоем «{OperationScopes.Describe(edit.Scope, w)}» нельзя.");
        }

        Note? note = notes.FirstOrDefault(note => note.Id == edit.NoteId);
        string missing = w.T($"Note {edit.NoteId} is not in the variation.", $"Ноты {edit.NoteId} нет в вариации.");
        switch (edit.Kind)
        {
            case HandEditKind.Remove:
                return note is null ? missing
                    : notes.Count == 1 ? w.T("A variation keeps at least one note.", "В вариации должна остаться хотя бы одна нота.")
                    : null;
            case HandEditKind.Add when note is not null:
                return w.T($"The variation already has a note {edit.NoteId}.", $"В вариации уже есть нота {edit.NoteId}.");
            case HandEditKind.Change when note is null:
                return missing;
            case HandEditKind.Change when NoteValues.Of(note) == edit.Values:
                return w.T("The note already has these values.", "У ноты уже эти значения.");
        }

        if (edit.Values is not { } values)
        {
            return w.T("The edit has no values for the note.", "В правке нет значений для ноты.");
        }

        bool moved = note is null || note.Span.Onset != values.Span.Onset;
        return values.Pitch.MidiNumber is < OperationRules.LowestPitch or > OperationRules.HighestPitch
                ? w.T("The note would be outside the piano's range (A0–C8).", "Нота вышла бы за пределы диапазона фортепиано (A0–C8).")
            : values.Velocity is < Note.MinVelocity or > Note.MaxVelocity
                ? w.T($"Velocity runs from {Note.MinVelocity} to {Note.MaxVelocity}.", $"Сила нажатия (velocity) — от {Note.MinVelocity} до {Note.MaxVelocity}.")
            : moved && values.Span.Onset < context.Span.Onset ? w.T("A note cannot start before the selection.", "Нота не может начинаться раньше выделения.")
            : null;
    }

    /// <exception cref="InvalidOperationException">The edit cannot apply to these notes.</exception>
    public Variation Apply(IReadOnlyList<Note> notes, VariationContext context, OperationParameters parameters)
    {
        if (UnavailableReason(notes, context, parameters) is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        HandEditParameters edit = Cast(parameters);
        Note[] after = edit.Kind switch
        {
            HandEditKind.Remove => [.. notes.Where(note => note.Id != edit.NoteId)],
            HandEditKind.Change => [.. notes.Select(note => note.Id == edit.NoteId ? With(note, edit.Values!.Value) : note)],
            _ => [.. notes, Added(notes, edit.NoteId, edit.Values!.Value)],
        };
        return VariationFacts.Build(OperationId, context.Span, notes, after, context.Keep, OperationRules.Check(notes, after, context.Words).Flags, context.Key, context.Meter, context.Words);
    }

    private static Note With(Note note, NoteValues values) =>
        note with { Pitch = values.Pitch, Span = values.Span, Velocity = values.Velocity, Attribution = Attribution.UserEdited };

    private static Note Added(IReadOnlyList<Note> notes, string id, NoteValues values)
    {
        Tick onset = values.Span.Onset;
        Note model = notes.Where(note => note.Span.Onset <= onset).OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Id, StringComparer.Ordinal).LastOrDefault()
            ?? notes.OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Id, StringComparer.Ordinal).First();
        return With(model with { Id = id, Source = null }, values);
    }

    private static HandEditParameters Cast(OperationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return parameters as HandEditParameters ?? throw new ArgumentException($"Expected {nameof(HandEditParameters)}.", nameof(parameters));
    }
}