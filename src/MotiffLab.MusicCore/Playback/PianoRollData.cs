using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Playback;

/// <summary>
/// Packs a piece into the flat arrays the piano roll reads (<c>RollPayload</c> in
/// <c>Scripts/roll/roll-model.ts</c> in the web app, the same in the iPad app through the facade's <c>roll</c>): per note its onset, duration, pitch, track, voice, hand and
/// what is inferred about it; per bar its onset, duration, beat length and number.
/// </summary>
public static class PianoRollData
{
    public const int HandNone = 0;
    public const int HandLeft = 1;
    public const int HandRight = 2;

    public const int FlagNoteInferred = 1;
    public const int FlagHandInferred = 2;

    public static RollPayload Create(IReadOnlyList<Note> notes, PlaybackScore score)
    {
        string[] ids = new string[notes.Count];
        long[] packed = new long[notes.Count * 7];
        for (int i = 0; i < notes.Count; i++)
        {
            Note note = notes[i];
            ids[i] = note.Id;
            packed[7 * i] = note.Span.Onset.Value;
            packed[(7 * i) + 1] = note.Span.Duration;
            packed[(7 * i) + 2] = note.Pitch.MidiNumber;
            packed[(7 * i) + 3] = note.Track;
            packed[(7 * i) + 4] = note.Voice ?? 0;
            packed[(7 * i) + 5] = note.Hand?.Value switch
            {
                Hand.Left => HandLeft,
                Hand.Right => HandRight,
                _ => HandNone,
            };
            packed[(7 * i) + 6] = (IsCertain(note.Attribution) ? 0 : FlagNoteInferred)
                | (note.Hand is null || IsCertain(note.Hand.Attribution) ? 0 : FlagHandInferred);
        }

        long[] bars = new long[score.Bars.Count * 4];
        for (int i = 0; i < score.Bars.Count; i++)
        {
            PlaybackBar bar = score.Bars[i];
            bars[4 * i] = bar.Span.Onset.Value;
            bars[(4 * i) + 1] = bar.Span.Duration;
            bars[(4 * i) + 2] = bar.BeatTicks;
            bars[(4 * i) + 3] = bar.Number;
        }

        return new RollPayload(ids, packed, bars, score.End.Value);
    }

    /// <summary>
    /// The names the roll writes (0018 section 4), spelled as the score spells them: each note's for the key signature in
    /// force where it starts, and every row's for each key signature, so the rows read in the key of the place in view.
    /// Before the first key signature, and in a piece without any, notes are spelled for the first one or C major.
    /// </summary>
    public static RollNames Names(IReadOnlyList<Note> notes, IReadOnlyList<KeyChange> keys, NoteNaming naming)
    {
        KeyChange[] ordered = [.. keys.OrderBy(key => key.At)];
        int FifthsAt(Tick at)
        {
            int fifths = ordered.Length == 0 ? 0 : ordered[0].Fifths;
            foreach (KeyChange key in ordered)
            {
                if (key.At > at)
                {
                    break;
                }

                fifths = key.Fifths;
            }

            return fifths;
        }

        string[] noteNames = [.. notes.Select(note => NoteNames.Name(note.Pitch.MidiNumber, FifthsAt(note.Span.Onset), naming))];
        KeyChange[] rows = ordered.Length == 0 ? [new KeyChange(Tick.Zero, 0)] : ordered;
        return new RollNames(
            noteNames,
            [.. rows.Select(key => key.At.Value)],
            [.. rows.Select(key => Enumerable.Range(0, Pitch.MaxMidiNumber + 1).Select(pitch => NoteNames.Name(pitch, key.Fifths, naming)).ToArray())]);
    }

    /// <summary>Imported and player-confirmed values are facts; detected and inferred ones are marked (decision record 0004).</summary>
    public static bool IsCertain(Attribution attribution) =>
        attribution.Provenance is Provenance.Imported or Provenance.UserConfirmed or Provenance.UserEdited;
}

public sealed record RollPayload(string[] Ids, long[] Notes, long[] Bars, long End);

/// <summary>The names the roll writes: one per note, and per key signature (from its tick) one per row, MIDI 0 to 127.</summary>
public sealed record RollNames(string[] Notes, long[] KeyTicks, string[][] Rows);