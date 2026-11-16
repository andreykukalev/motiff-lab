using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>The notes of a performance read from MIDI: a transcription engine's file or a MIDI file played in by hand (decision record 0022, section 1).</summary>
public static class PlayedNotes
{
    /// <summary>
    /// Every note of the file at its time, through the file's tempo changes, in microseconds from the file's start
    /// plus <paramref name="startMicroseconds"/> (where the stretch that was transcribed starts in the recording).
    /// Ids are the importer's, so they stay the same for the same file.
    /// </summary>
    public static IReadOnlyList<PlayedNote> FromMidi(ReadOnlySpan<byte> midi, long startMicroseconds = 0)
    {
        MidiImport import = MidiImporter.Import(midi);
        MidiTempoChange[] tempos = [.. import.Tempos.OrderBy(tempo => tempo.At)];
        return [.. import.Notes.Select(note =>
        {
            long onset = Microseconds(tempos, note.Span.Onset.Value);
            return new PlayedNote(note.Id, note.Pitch, startMicroseconds + onset, note.Velocity, Microseconds(tempos, note.Span.End.Value) - onset);
        })];
    }

    /// <summary>The time of a tick through the tempo changes; a quarter at 120 a minute until the first.</summary>
    private static long Microseconds(MidiTempoChange[] tempos, long tick)
    {
        long at = 0, elapsed = 0, perQuarter = MidiTempoChange.DefaultMicrosecondsPerQuarter;
        foreach (MidiTempoChange tempo in tempos)
        {
            if (tempo.At.Value >= tick)
            {
                break;
            }

            elapsed += (tempo.At.Value - at) * perQuarter / CanonicalTime.TicksPerQuarter;
            (at, perQuarter) = (tempo.At.Value, tempo.MicrosecondsPerQuarter);
        }

        return elapsed + ((tick - at) * perQuarter / CanonicalTime.TicksPerQuarter);
    }
}