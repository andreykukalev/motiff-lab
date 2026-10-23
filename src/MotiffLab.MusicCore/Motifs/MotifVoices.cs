using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

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
/// <para>
/// When no two of the motif's notes sound at once, every note is the melody. Otherwise the melody is
/// read as the top line: the highest note at each onset, among right-hand notes when the motif has
/// any. An onset whose notes all lie below a melody note still sounding is accompaniment under a held
/// melody note, not a new melody note.
/// </para>
/// <para>
/// The split is pure: the same notes and motif give the same voices.
/// </para>
/// </remarks>
public static class MotifVoices
{
    /// <summary>The top line is usually the melody, but not always (an inner melody, a melody in the bass).</summary>
    public const double SplitConfidence = 0.8;

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

        Note[] chosen = [.. motif.NoteIds.Where(byId.ContainsKey).Select(id => byId[id])
            .OrderBy(static note => note.Span.Onset)
            .ThenBy(static note => note.Pitch.MidiNumber)
            .ThenBy(static note => note.Id, StringComparer.Ordinal)];

        if (!HasTwoAtOnce(chosen))
        {
            return new MotifVoiceSplit([.. chosen.Select(static note => note.Id)], [], Attribution.Imported);
        }

        Note[] candidates = chosen.Any(static note => note.Hand?.Value == Hand.Right)
            ? [.. chosen.Where(static note => note.Hand?.Value == Hand.Right)]
            : chosen;

        var melody = new List<Note>();
        foreach (IGrouping<Tick, Note> onset in candidates.GroupBy(static note => note.Span.Onset))
        {
            Note top = onset.MaxBy(static note => note.Pitch.MidiNumber)!;
            if (melody.Count > 0 && melody[^1].Span.End > onset.Key && top.Pitch < melody[^1].Pitch)
            {
                continue;
            }

            melody.Add(top);
        }

        var melodyIds = melody.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        return new MotifVoiceSplit(
            [.. melody.Select(static note => note.Id)],
            [.. chosen.Where(note => !melodyIds.Contains(note.Id)).Select(static note => note.Id)],
            Attribution.Inferred(SplitConfidence));
    }

    private static bool HasTwoAtOnce(Note[] sorted)
    {
        Tick soundingUntil = Tick.Zero;
        foreach (Note note in sorted)
        {
            if (note.Span.Onset < soundingUntil)
            {
                return true;
            }

            soundingUntil = note.Span.End > soundingUntil ? note.Span.End : soundingUntil;
        }

        return false;
    }
}