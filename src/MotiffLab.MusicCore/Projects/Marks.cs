using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>The inks the player writes with (0018, section 5): four pencil colours and the amber highlighter.</summary>
public enum InkColour
{
    Graphite,
    Carmine,
    Forest,
    Ochre,
    Amber,
}

/// <summary>What an ink stroke was made with.</summary>
public enum InkTool
{
    Pen,
    Highlighter,
}

/// <summary>
/// One stroke the player wrote on the score (0015, 0018): kept against the bar it starts in, not
/// against the page, so it goes back to its bar at the size of the staves in any layout.
/// </summary>
/// <remarks>
/// The shape is the one the web app's pencil layer draws: where in the bar the stroke starts, as a
/// share of the bar's width and in staff spaces below the top staff line, then its points relative to
/// the first in staff spaces with their pressure. Strokes are the player's own writing and are never
/// read as music.
/// </remarks>
/// <param name="Size">The stroke's thickness in staff spaces.</param>
/// <param name="Bar">Where the bar the stroke starts in begins.</param>
/// <param name="U">Where the stroke starts across the bar: 0 at its left edge, 1 at its right.</param>
/// <param name="V">Where the stroke starts: staff spaces below the bar's top staff line, negative above it.</param>
/// <param name="Points">The points after the first as dx, dy (staff spaces from the first point) and pressure, three numbers each.</param>
/// <param name="Pressure">The first point's pressure, 0 to 1.</param>
public sealed record InkStroke(string Id, InkTool Tool, InkColour Colour, double Size, Tick Bar, double U, double V, IReadOnlyList<double> Points, double Pressure)
{
    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A stroke needs an id.", nameof(Id));

    public double Size { get; } = Size > 0 && double.IsFinite(Size) ? Size : throw new ArgumentOutOfRangeException(nameof(Size), Size, "A stroke's size is positive.");

    public IReadOnlyList<double> Points { get; } = (Points ?? throw new ArgumentNullException(nameof(Points))).Count % 3 == 0 && Points.All(double.IsFinite)
        ? Points
        : throw new ArgumentException("A stroke's points come three numbers at a time.", nameof(Points));

    public bool Equals(InkStroke? other) =>
        other is not null && Id == other.Id && Tool == other.Tool && Colour == other.Colour && Size == other.Size && Bar == other.Bar
        && U == other.U && V == other.V && Pressure == other.Pressure && Points.SequenceEqual(other.Points);

    public override int GetHashCode() => HashCode.Combine(Id, Bar, Points.Count);
}

/// <summary>
/// A note in the margin (0018, section 5): the player's words about a bar or a phrase, shown as a
/// numbered marker beside it and found again in the Contents drawer's Notes tab.
/// </summary>
/// <param name="Span">The bar or bars the note is about.</param>
/// <param name="Text">The note, trimmed and never blank.</param>
public sealed record MarginNote(string Id, TickSpan Span, string Text, DateTimeOffset CreatedAt)
{
    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A note needs an id.", nameof(Id));

    public string Text { get; } = !string.IsNullOrWhiteSpace(Text) ? Text.Trim() : throw new ArgumentException("A margin note needs some words.", nameof(Text));

    /// <summary>The same note with other words.</summary>
    public MarginNote With(string text) => new(Id, Span, text, CreatedAt);
}

/// <summary>
/// A highlight (0018, section 5): bars, or notes within them, marked in an ink. The notes are kept by id
/// so the highlight stays on them when the score reflows or the piece is imported again.
/// </summary>
/// <param name="Span">The bars the highlight lies in.</param>
/// <param name="NoteIds">The notes highlighted; empty highlights the whole span.</param>
public sealed record Highlight(string Id, TickSpan Span, IReadOnlyList<string> NoteIds, InkColour Colour)
{
    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A highlight needs an id.", nameof(Id));

    public IReadOnlyList<string> NoteIds { get; } = (NoteIds ?? throw new ArgumentNullException(nameof(NoteIds))).All(id => !string.IsNullOrWhiteSpace(id))
        ? NoteIds
        : throw new ArgumentException("A highlighted note needs an id.", nameof(NoteIds));

    public bool Equals(Highlight? other) => other is not null && Id == other.Id && Span == other.Span && Colour == other.Colour && NoteIds.SequenceEqual(other.NoteIds, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Id, Span, Colour);
}

/// <summary>A fingering the player wrote over a note (0018, section 5): a digit, anchored to the note's id.</summary>
public sealed record Fingering(string NoteId, int Finger)
{
    public string NoteId { get; } = !string.IsNullOrWhiteSpace(NoteId) ? NoteId : throw new ArgumentException("A fingering needs its note.", nameof(NoteId));

    public int Finger { get; } = Finger is >= 1 and <= 5 ? Finger : throw new ArgumentOutOfRangeException(nameof(Finger), Finger, "A finger is 1 to 5.");
}

/// <summary>
/// The marks the player made on the piece (0018, section 5), kept in the project file so they travel with
/// it: ink strokes, margin notes, highlights and fingering. Bookmarks and Check flags are kept beside them,
/// on the project itself. Every mark is anchored to a bar or a note id, never to a page.
/// </summary>
public sealed record Marks(
    IReadOnlyList<InkStroke> Ink,
    IReadOnlyList<MarginNote> Notes,
    IReadOnlyList<Highlight> Highlights,
    IReadOnlyList<Fingering> Fingering)
{
    public static Marks Empty { get; } = new([], [], [], []);

    public bool IsEmpty => Ink.Count == 0 && Notes.Count == 0 && Highlights.Count == 0 && Fingering.Count == 0;

    public int Count => Ink.Count + Notes.Count + Highlights.Count + Fingering.Count;

    /// <summary>The marks with these strokes in place of the ink, as the pencil layer hands them back after a change.</summary>
    public Marks WithInk(IEnumerable<InkStroke> ink)
    {
        List<InkStroke> strokes = [.. ink ?? throw new ArgumentNullException(nameof(ink))];
        return strokes.Select(static stroke => stroke.Id).Distinct(StringComparer.Ordinal).Count() == strokes.Count
            ? this with { Ink = strokes }
            : throw new ArgumentException("Two strokes have the same id.", nameof(ink));
    }

    /// <summary>The marks with the strokes added after the others.</summary>
    public Marks AddInk(IEnumerable<InkStroke> strokes) => WithInk([.. Ink, .. strokes ?? throw new ArgumentNullException(nameof(strokes))]);

    /// <summary>The marks without the strokes named; ids not there are ignored.</summary>
    public Marks RemoveInk(IEnumerable<string> ids)
    {
        var gone = (ids ?? throw new ArgumentNullException(nameof(ids))).ToHashSet(StringComparer.Ordinal);
        return this with { Ink = [.. Ink.Where(stroke => !gone.Contains(stroke.Id))] };
    }

    /// <exception cref="ArgumentException">A note with the same id is already there.</exception>
    public Marks AddNote(MarginNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return Notes.Any(kept => kept.Id == note.Id)
            ? throw new ArgumentException($"There is already a note {note.Id}.", nameof(note))
            : this with { Notes = [.. Notes, note] };
    }

    /// <exception cref="KeyNotFoundException">No note has the id.</exception>
    public Marks ChangeNote(string id, string text)
    {
        MarginNote changed = (Notes.FirstOrDefault(note => note.Id == id) ?? throw new KeyNotFoundException($"There is no note {id}.")).With(text);
        return this with { Notes = [.. Notes.Select(note => note.Id == id ? changed : note)] };
    }

    /// <exception cref="KeyNotFoundException">No note has the id.</exception>
    public Marks RemoveNote(string id)
    {
        _ = Notes.FirstOrDefault(note => note.Id == id) ?? throw new KeyNotFoundException($"There is no note {id}.");
        return this with { Notes = [.. Notes.Where(note => note.Id != id)] };
    }

    /// <exception cref="ArgumentException">A highlight with the same id is already there.</exception>
    public Marks AddHighlight(Highlight highlight)
    {
        ArgumentNullException.ThrowIfNull(highlight);
        return Highlights.Any(kept => kept.Id == highlight.Id)
            ? throw new ArgumentException($"There is already a highlight {highlight.Id}.", nameof(highlight))
            : this with { Highlights = [.. Highlights, highlight] };
    }

    /// <exception cref="KeyNotFoundException">No highlight has the id.</exception>
    public Marks RemoveHighlight(string id)
    {
        _ = Highlights.FirstOrDefault(highlight => highlight.Id == id) ?? throw new KeyNotFoundException($"There is no highlight {id}.");
        return this with { Highlights = [.. Highlights.Where(highlight => highlight.Id != id)] };
    }

    /// <summary>The marks with the finger on the note, in place of any finger written there before.</summary>
    public Marks SetFingering(Fingering fingering)
    {
        ArgumentNullException.ThrowIfNull(fingering);
        return this with { Fingering = [.. Fingering.Where(kept => kept.NoteId != fingering.NoteId), fingering] };
    }

    /// <summary>The marks without a finger on the note; nothing changes when there was none.</summary>
    public Marks RemoveFingering(string noteId) => this with { Fingering = [.. Fingering.Where(kept => kept.NoteId != noteId)] };

    /// <summary>"n_0004": one more than the highest note number, so ids stay stable for the life of the project.</summary>
    public string NextNoteId() => Project.NextId("n_", Notes.Select(note => note.Id));

    /// <summary>"h_0004": one more than the highest highlight number.</summary>
    public string NextHighlightId() => Project.NextId("h_", Highlights.Select(highlight => highlight.Id));

    public bool Equals(Marks? other) =>
        other is not null && Ink.SequenceEqual(other.Ink) && Notes.SequenceEqual(other.Notes) && Highlights.SequenceEqual(other.Highlights) && Fingering.SequenceEqual(other.Fingering);

    public override int GetHashCode() => HashCode.Combine(Ink.Count, Notes.Count, Highlights.Count, Fingering.Count);
}