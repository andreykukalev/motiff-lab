using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Transcription;

/// <summary>
/// Notes heard in a recording by automatic transcription (decision record 0014). The engine writes
/// MIDI that imports like any other, but its notes are a hearing of a recording, not facts from a file:
/// every one is marked <see cref="Provenance.Detected"/> at <see cref="Confidence"/>.
/// </summary>
/// <remarks>
/// The engine gives no score the player could check per note, and its accuracy on real recordings is
/// not yet measured, so every note gets the same low confidence, below the 0.6 that hints need
/// (05-domain-architecture). Hands worked out from the notes stay inferred, as for any MIDI file.
/// Corrections still go to the edit log and become user-edited. Pure and deterministic.
/// </remarks>
public static class Transcribed
{
    /// <summary>The confidence of a transcribed note: a draft for the player to check.</summary>
    public const double Confidence = 0.4;

    /// <summary>The notes marked as detected; notes another step already marked are marked the same.</summary>
    public static IReadOnlyList<Note> Mark(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        Attribution detected = Attribution.Detected(Confidence);
        return [.. notes.Select(note => note with { Attribution = detected })];
    }

    /// <summary>
    /// Whether the piece's notes were heard in a recording: MIDI whose notes are all marked detected,
    /// as <see cref="Mark(ImportedPiece)"/> leaves them. Scanned notes are detected too, but are MusicXML.
    /// </summary>
    public static bool IsHeard(ImportedPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        return piece.Midi is not null && piece.Notes.Count > 0 && piece.Notes.All(note => note.Attribution.Provenance == Provenance.Detected);
    }

    /// <summary>
    /// The key to write notes heard in a recording in, which the engine does not give: the major or
    /// minor key whose profile best fits how long each pitch class sounds (decision record 0016), with
    /// notes below middle C counting twice, since the bass carries the roots of the harmony.
    /// An inference for the score's key signature only; analysis still treats the piece as having none.
    /// Null without notes.
    /// </summary>
    public static MusicalKey? KeyOf(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        double[] weights = new double[12];
        foreach (Note note in notes)
        {
            int bass = note.Pitch.MidiNumber < 60 ? 2 : 1;
            weights[note.Pitch.MidiNumber % 12] += bass * Math.Min(note.Span.Duration, 4 * CanonicalTime.TicksPerQuarter);
        }

        return weights.All(weight => weight == 0) ? null : KeyFinder.Rank(weights)[0].Key;
    }

    /// <summary>The piece with its notes marked as transcribed (<see cref="Mark(IReadOnlyList{Note})"/>).</summary>
    /// <exception cref="ArgumentException">The piece was not imported from MIDI, which is what transcription writes.</exception>
    public static ImportedPiece Mark(ImportedPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (piece.Midi is null)
        {
            throw new ArgumentException("A transcription is read as MIDI.", nameof(piece));
        }

        return piece with { Notes = Mark(piece.Notes) };
    }
}