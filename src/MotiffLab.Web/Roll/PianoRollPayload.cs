using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.Web.Roll;

/// <summary>
/// Packs a piece into the flat arrays the piano roll reads (<c>RollPayload</c> in
/// <c>Scripts/roll/roll-model.ts</c>): per note its onset, duration, pitch, track, voice, hand and
/// what is inferred about it; per bar its onset, duration, beat length and number.
/// </summary>
internal static class PianoRollPayload
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

    /// <summary>Imported and player-confirmed values are facts; detected and inferred ones are marked (decision record 0004).</summary>
    public static bool IsCertain(Attribution attribution) =>
        attribution.Provenance is Provenance.Imported or Provenance.UserConfirmed or Provenance.UserEdited;
}

internal sealed record RollPayload(string[] Ids, long[] Notes, long[] Bars, long End);

/// <summary>A drag across the roll's bar numbers, from where it started to where it ended; a click when it barely moved.</summary>
public readonly record struct RollRange(Tick Anchor, Tick Current, bool IsClick);