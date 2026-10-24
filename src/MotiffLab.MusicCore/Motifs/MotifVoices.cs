using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Motifs;

/// <summary>A motif's notes split into its melody voice and its other voices.</summary>
/// <param name="Melody">Ids of the melody notes, in onset order: what melodic views and operations use.</param>
/// <param name="Others">Ids of the other notes, ordered by onset, then pitch: what accompaniment and voicing operations use.</param>
/// <param name="Attribution">
/// Imported when the motif is a single line, so every note is the melody. Inferred, with
/// <see cref="MotifVoices.SplitConfidence"/>, when the melody had to be picked out of notes that sound together.
/// </param>
public sealed record MotifVoiceSplit(IReadOnlyList<string> Melody, IReadOnlyList<string> Others, Attribution Attribution);

/// <summary>Derives the melody voice and the other voices of a motif (Stage 1 spec, motif definition).</summary>
/// <remarks>
/// The melody is <see cref="MotifTexture.Melody"/>, so this split, the melody facts, the Keep
/// checks and the melody operations all read the same line. Pure: the same notes and motif give
/// the same voices.
/// </remarks>
public static class MotifVoices
{
    /// <summary>The top line is usually the melody, but not always (an inner melody, a melody in the bass).</summary>
    public const double SplitConfidence = MotifTexture.Confidence;

    /// <param name="notes">The notes of the piece; the motif's notes are picked by id, and ids not found are skipped.</param>
    public static MotifVoiceSplit Split(IEnumerable<Note> notes, Motif motif)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(motif);

        var byId = new Dictionary<string, Note>(StringComparer.Ordinal);
        foreach (Note note in notes)
        {
            byId.TryAdd(note.Id, note);
        }

        MotifTexture texture = MotifTexture.Read(motif.NoteIds.Where(byId.ContainsKey).Select(id => byId[id]));
        var melodyIds = texture.Melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        return new MotifVoiceSplit(
            [.. texture.Melody.Select(static note => note.Id)],
            [.. texture.Notes.Where(note => !melodyIds.Contains(note.Id)).Select(static note => note.Id)],
            texture.Attribution);
    }
}