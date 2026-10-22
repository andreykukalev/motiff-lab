using MotiffLab.MusicCore.Playback;

namespace MotiffLab.Web.Playback;

/// <summary>
/// Packs a <see cref="PlaybackScore"/> into the flat number arrays the TypeScript engine reads
/// (<c>TimelineMessage</c> and <c>SourceMessage</c> in <c>playback-processor.ts</c>). Flat arrays keep
/// a score of thousands of notes small on the way across and cheap to copy to the audio thread.
/// </summary>
internal static class PlaybackPayload
{
    public static TimelinePayload Timeline(PlaybackScore score)
    {
        long[] tempos = new long[score.Tempos.Count * 2];
        for (int i = 0; i < score.Tempos.Count; i++)
        {
            tempos[2 * i] = score.Tempos[i].At.Value;
            tempos[(2 * i) + 1] = score.Tempos[i].MicrosecondsPerQuarter;
        }

        long[] beats = new long[score.Beats.Count * 2];
        for (int i = 0; i < score.Beats.Count; i++)
        {
            beats[2 * i] = score.Beats[i].At.Value;
            beats[(2 * i) + 1] = score.Beats[i].IsDownbeat ? 1 : 0;
        }

        long[] bars = new long[score.Bars.Count * 4];
        for (int i = 0; i < score.Bars.Count; i++)
        {
            PlaybackBar bar = score.Bars[i];
            bars[4 * i] = bar.Span.Onset.Value;
            bars[(4 * i) + 1] = bar.Span.Duration;
            bars[(4 * i) + 2] = bar.BeatTicks;
            bars[(4 * i) + 3] = bar.BeatsPerBar;
        }

        return new TimelinePayload(tempos, beats, bars, score.End.Value);
    }

    /// <summary>Onset, duration, pitch, velocity and track of each note, in the score's order.</summary>
    public static long[] Notes(PlaybackScore score)
    {
        long[] notes = new long[score.Notes.Count * 5];
        for (int i = 0; i < score.Notes.Count; i++)
        {
            PlaybackNote note = score.Notes[i];
            notes[5 * i] = note.Span.Onset.Value;
            notes[(5 * i) + 1] = note.Span.Duration;
            notes[(5 * i) + 2] = note.Pitch;
            notes[(5 * i) + 3] = note.Velocity;
            notes[(5 * i) + 4] = note.Track;
        }

        return notes;
    }
}

internal sealed record TimelinePayload(long[] Tempos, long[] Beats, long[] Bars, long End);