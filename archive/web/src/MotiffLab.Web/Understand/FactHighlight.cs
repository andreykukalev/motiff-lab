using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;

namespace MotiffLab.Web.Understand;

/// <summary>A fact chosen on the Understand tab (#25), and the one place of it the player chose, or null for all of them.</summary>
public sealed record FactChoice(MotifFact Fact, FactPlace? Place = null)
{
    /// <summary>The places to highlight: the chosen one, or all the fact names.</summary>
    public IReadOnlyList<FactPlace> Places => Place is null ? Fact.Places : [Place];
}

/// <summary>A note of a highlighted place, as the score matches it to its engraved notes: its id, when it sounds and its key.</summary>
public sealed record HighlightNote(string Id, long Onset, long End, int Pitch);

/// <summary>
/// A place of a chosen fact as the score and the roll draw it (Scripts/score, Scripts/roll): its
/// stretch of time in ticks and its notes. Empty notes stand for a stretch of time, such as a phrase or a silence.
/// </summary>
public sealed record HighlightPlace(long Start, long End, IReadOnlyList<HighlightNote> Notes);

/// <summary>
/// What the score and the roll highlight for a chosen fact. The places come from music-core
/// (<see cref="MotifFact.Places"/>); this only looks up their notes by id, so the views can find them.
/// </summary>
public sealed record FactHighlight(FactChoice Choice, IReadOnlyList<HighlightPlace> Places)
{
    public static FactHighlight Create(FactChoice choice, IReadOnlyList<Note> notes)
    {
        var byId = new Dictionary<string, Note>(StringComparer.Ordinal);
        foreach (Note note in notes)
        {
            byId.TryAdd(note.Id, note);
        }

        HighlightPlace[] places = [.. choice.Places.Select(place => new HighlightPlace(
            place.Span.Onset.Value,
            place.Span.End.Value,
            [.. place.NoteIds.Select(id => byId.GetValueOrDefault(id)).OfType<Note>()
                .Select(note => new HighlightNote(note.Id, note.Span.Onset.Value, note.Span.End.Value, note.Pitch.MidiNumber))]))];
        return new FactHighlight(choice, places);
    }
}