using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>How a fact was reached, so the app never presents a guess as something it read.</summary>
public enum FactCertainty
{
    /// <summary>Read or counted directly from the notes or stated by the file.</summary>
    Detected,

    /// <summary>Worked out by a rule from the notes, for example the top line of chords or a key from the pitches.</summary>
    Inferred,

    /// <summary>A rule's best guess on thin or conflicting evidence.</summary>
    Uncertain,
}

/// <summary>The lens a fact belongs to on the Understand screen (N4).</summary>
public enum FactLens
{
    Rhythm,
    Melody,
    Key,

    /// <summary>Repetition and phrasing around the motif (S1-A4).</summary>
    Structure,

    /// <summary>The measurable side of musical character: tempo, density, register, loudness, articulation (S1-A5).</summary>
    Character,
}

/// <summary>
/// A place in the score that a fact names or counts, so choosing the fact can highlight it, for
/// example where the motif comes back or which notes are syncopated (#25).
/// </summary>
/// <remarks>
/// A fact that names places one by one, such as recurrences or syncopations, has a place for each;
/// a fact that counts a set of notes, such as the detached notes, has one place for the set. The
/// UI only shows places; it never works them out.
/// </remarks>
/// <param name="Label">The place in a few words, as the fact names it, for example "exactly in bar 2" or "highest, E5".</param>
/// <param name="Span">
/// For a place that is a stretch of time, such as a bar, a beat, a phrase, a silence or a tempo,
/// that stretch; for any other place, from the first onset of its notes to the end of the last one.
/// </param>
/// <param name="NoteIds">
/// The notes the place refers to, in time order, then by pitch: for a bar or a beat, the notes that
/// start in it. Empty for a stretch of time with no notes of its own, such as a phrase or a silence.
/// </param>
public sealed record FactPlace(string Label, TickSpan Span, IReadOnlyList<string> NoteIds)
{
    /// <summary>A place made of notes, spanning them from the first onset to the last end.</summary>
    internal static FactPlace Of(string label, IEnumerable<Note> notes)
    {
        Note[] ordered = [.. notes.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal)];
        return new FactPlace(label, TickSpan.FromBounds(ordered[0].Span.Onset, ordered.Max(note => note.Span.End)), [.. ordered.Select(note => note.Id)]);
    }

    public bool Equals(FactPlace? other) =>
        other is not null && Label == other.Label && Span == other.Span && NoteIds.SequenceEqual(other.NoteIds, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Label, Span, NoteIds.Count);
}

/// <summary>One plain fact about a motif, with how sure it is and why.</summary>
/// <param name="Id">Stable within a motif's analysis, for example "melody.intervals"; explanations cite it.</param>
/// <param name="Text">The fact in a sentence, stating only what was measured.</param>
/// <param name="Basis">Where the fact comes from, and why it is inferred or uncertain when it is.</param>
public sealed record MotifFact(string Id, FactLens Lens, FactCertainty Certainty, string Text, string Basis)
{
    /// <summary>
    /// The places in the score the fact names or counts, in the order the text names them and
    /// otherwise in time order; empty for a fact about the selection as a whole, such as the key or the tempo
    /// when it does not change. Places stay on the device and are not part of the digest.
    /// </summary>
    public IReadOnlyList<FactPlace> Places { get; init; } = [];

    public bool Equals(MotifFact? other) =>
        other is not null
        && Id == other.Id
        && Lens == other.Lens
        && Certainty == other.Certainty
        && Text == other.Text
        && Basis == other.Basis
        && Places.SequenceEqual(other.Places);

    public override int GetHashCode() => HashCode.Combine(Id, Lens, Certainty, Text, Places.Count);
}