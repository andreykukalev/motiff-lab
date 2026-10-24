using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;

namespace MotiffLab.MusicCore.Variations;

/// <summary>
/// The shared part of the melody operations V9 and V10: they change the pitches of the motif's
/// melody (<see cref="MotifTexture.Melody"/>) and leave every other note, and every onset and
/// length, as it was (Stage 1 spec, motif definition: melodic operations use the melody voice).
/// </summary>
/// <remarks>
/// A melody operation is not available when the melody has fewer than two notes, when it counts in
/// scale steps and the motif has no key, or when a new melody note would sound at or below a note
/// of the accompaniment sounding with it: the melody would stop being the top line, so the result
/// would no longer be a variation of the same melody.
/// </remarks>
public abstract class MelodyOperation<TParameters> : VariationOperation<TParameters>
    where TParameters : OperationParameters
{
    /// <summary>Whether these parameters count in scale steps, and so need a key.</summary>
    protected abstract bool IsDiatonic(TParameters parameters);

    /// <summary>The new melody pitches, one per melody note, in order. Pure.</summary>
    /// <param name="key">The context's key; never null when <see cref="IsDiatonic"/> is true.</param>
    protected abstract int[] NewMelody(int[] melody, MusicalKey? key, TParameters parameters);

    /// <summary>Why the operation has nothing to do on this melody, or null. Runs after the shared checks.</summary>
    protected virtual string? MelodyPrecondition(int[] melody, MusicalKey? key, TParameters parameters) => null;

    protected sealed override string? Precondition(IReadOnlyList<Note> notes, VariationContext context, TParameters parameters)
    {
        MotifTexture texture = MotifTexture.Read(notes);
        if (texture.Melody.Count < 2)
        {
            return $"{Name} needs at least two melody notes, so it is not available for this selection.";
        }

        if (IsDiatonic(parameters) && context.Key is null)
        {
            return $"{Name} counts in steps of the key's scale, and this selection has no key, so it is not available.";
        }

        int[] melody = [.. texture.Melody.Select(static note => note.Pitch.MidiNumber)];
        if (MelodyPrecondition(melody, context.Key, parameters) is { } reason)
        {
            return reason;
        }

        int[] after = NewMelody(melody, context.Key, parameters);
        for (int i = 0; i < after.Length; i++)
        {
            Note note = texture.Melody[i];
            if (after[i] != melody[i] && texture.SoundingWith(note).Any(other => !IsMelody(texture, other) && other.Pitch.MidiNumber >= after[i]))
            {
                return $"{Name} would put a melody note at or under its accompaniment, so it is not available for this selection.";
            }
        }

        return null;
    }

    protected sealed override IEnumerable<Note> Transform(IReadOnlyList<Note> notes, VariationContext context, TParameters parameters)
    {
        MotifTexture texture = MotifTexture.Read(notes);
        int[] after = NewMelody([.. texture.Melody.Select(static note => note.Pitch.MidiNumber)], context.Key, parameters);
        var pitches = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < after.Length; i++)
        {
            pitches[texture.Melody[i].Id] = after[i];
        }

        return notes.Select(note => pitches.TryGetValue(note.Id, out int pitch) && pitch != note.Pitch.MidiNumber
            ? note with { Pitch = new Pitch(Math.Clamp(pitch, 0, Pitch.MaxMidiNumber)), Attribution = Attribution.UserEdited }
            : note);
    }

    private static bool IsMelody(MotifTexture texture, Note note) => texture.Melody.Any(melody => melody.Id == note.Id);
}