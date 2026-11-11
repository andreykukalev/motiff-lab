using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Transcription;

/// <summary>Why the length of a note heard in a recording is uncertain (#352).</summary>
public enum LengthDoubt
{
    /// <summary>The sustain pedal is down when the note ends, so its release cannot be heard.</summary>
    Pedal,

    /// <summary>The note sounds longer than a half note, while the piano tone dies away, so its end is a guess.</summary>
    Long,
}

/// <summary>The notes of one bar whose length is uncertain, and why.</summary>
/// <param name="Bar">The bar number the notes start in, as the workspace shows it.</param>
/// <param name="NoteIds">The notes, in time order, then by pitch.</param>
/// <param name="Reasons">Each reason that holds for at least one of them, in the order of <see cref="LengthDoubt"/>.</param>
public sealed record UncertainLengths(int Bar, IReadOnlyList<string> NoteIds, IReadOnlyList<LengthDoubt> Reasons)
{
    public bool Equals(UncertainLengths? other) =>
        other is not null && Bar == other.Bar && NoteIds.SequenceEqual(other.NoteIds, StringComparer.Ordinal) && Reasons.SequenceEqual(other.Reasons);

    public override int GetHashCode() => HashCode.Combine(Bar, NoteIds.Count, Reasons.Count);
}

/// <summary>
/// The notes heard in a recording whose length is uncertain (decision record 0027, section 12; #352), apart from any
/// doubt about their pitch or onset: on a synthetic Für Elise the engine finds where notes start almost exactly, but their
/// ends far less well (onsets 0.999, with ends 0.64–0.73). Worked out when the check is shown and never saved, so the
/// project file does not change (Andrei, 2026-11-11).
/// </summary>
/// <remarks>
/// A heard note's length is uncertain when the sustain pedal is down at the moment it ends, or when it sounds longer
/// than a half note. Notes the player fixed or confirmed are certain, and so are notes not heard in a recording. These
/// marks do not make chords uncertain: the Check flags do that (<see cref="Analysis.NoteDoubt"/>). Pure and
/// deterministic: the same notes and pedal give the same bars.
/// </remarks>
public static class HeardLengths
{
    /// <summary>A heard note longer than this, a half note, has an uncertain end.</summary>
    public const long LongestSure = 2 * CanonicalTime.TicksPerQuarter;

    /// <summary>The bars with notes of uncertain length, in bar order; empty for a piece not heard in a recording.</summary>
    /// <param name="piece">The piece as opened, which tells whether it was heard and holds its pedal.</param>
    /// <param name="notes">The notes as the player has them now, corrections applied.</param>
    public static IReadOnlyList<UncertainLengths> Of(ImportedPiece piece, IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(notes);
        if (!Transcribed.IsHeard(piece))
        {
            return [];
        }

        IReadOnlyList<MidiPedal> pedal = piece.Midi?.SustainPedal ?? [];
        var found = new List<(Note Note, LengthDoubt Reason)>();
        foreach (Note note in notes)
        {
            if (note.Attribution.Provenance != Provenance.Detected)
            {
                continue;
            }

            Tick end = note.Span.End;
            if (pedal.Any(held => held.Span.Onset < end && end <= held.Span.End))
            {
                found.Add((note, LengthDoubt.Pedal));
            }

            if (note.Span.Duration > LongestSure)
            {
                found.Add((note, LengthDoubt.Long));
            }
        }

        return [.. found
            .GroupBy(item => piece.Meter.BarAt(item.Note.Span.Onset).Number)
            .OrderBy(bar => bar.Key)
            .Select(bar => new UncertainLengths(
                bar.Key,
                [.. bar.Select(item => item.Note).Distinct()
                    .OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal)
                    .Select(note => note.Id)],
                [.. bar.Select(item => item.Reason).Distinct().Order()]))];
    }

    /// <summary>The reason in a few words, for the Check drawer: "Pedal held", «Педаль нажата».</summary>
    public static string NameOf(LengthDoubt reason, Words words) => reason switch
    {
        LengthDoubt.Pedal => words.T("Pedal held at the end", "Педаль нажата в конце"),
        LengthDoubt.Long => words.T("Long note", "Долгая нота"),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown length doubt."),
    };

    /// <summary>What the reason means for the notes, in plain words for the player.</summary>
    public static string Explain(LengthDoubt reason, Words words) => reason switch
    {
        LengthDoubt.Pedal => words.T(
            "The pedal is down when these notes end, so their release cannot be heard and their length may be written wrong.",
            "Когда эти ноты заканчиваются, педаль нажата: снятия не слышно, и длительность могла записаться неверно."),
        LengthDoubt.Long => words.T(
            "These notes sound longer than a half note while the piano tone dies away, so where they end is a guess.",
            "Эти ноты звучат дольше половинной, а звук фортепиано затухает, поэтому их конец угадан."),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown length doubt."),
    };
}