using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>A metronome mark written over the score: "♩ = 92", or "♩ ≈ 92" for a tempo worked out from a recording.</summary>
/// <param name="PerMinute">Beats a minute, counted in <paramref name="Beat"/>.</param>
/// <param name="Approximate">True for a tempo worked out from the playing (<see cref="TempoSource.Inferred"/>).</param>
/// <param name="Beat">The note of the mark: a dotted quarter in 6/8 and the other compound meters ("♩. = 60").</param>
public sealed record TempoMark(Tick At, int PerMinute, bool Approximate, TempoBeat Beat = TempoBeat.Quarter)
{
    /// <summary>The words after the note: "= 92" or "≈ 92".</summary>
    public string Text => $"{(Approximate ? "≈" : "=")} {PerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Which tempos a score is marked with (version 0.5.4, #511 section 17): the tempo at the start when the notes state it or
/// the player set it, "≈" when it was worked out from a recording, and nothing when it is only the default; after the
/// start, only the changes the notes state. A tempo assumed is never written, so a reader does not take it for the printed one.
/// A mark counts the beat of the meter where it stands: dotted quarters in 6/8, quarters in 4/4.
/// </summary>
public static class TempoMarks
{
    /// <param name="tempos">The tempo changes as played, in order, the first at tick 0.</param>
    /// <param name="meter">The bars the marks stand in, for their beat; null counts quarter notes throughout.</param>
    public static IReadOnlyList<TempoMark> Of(IReadOnlyList<PlaybackTempo> tempos, MeterMap? meter = null)
    {
        ArgumentNullException.ThrowIfNull(tempos);
        var marks = new List<TempoMark>();
        for (int i = 0; i < tempos.Count; i++)
        {
            PlaybackTempo tempo = tempos[i];
            TempoBeat beat = meter is null ? TempoBeat.Quarter : TempoBeats.At(meter, tempo.At);
            int perMinute = beat.PerMinute(tempo.MicrosecondsPerQuarter);
            bool written = i == 0 ? tempo.Source != TempoSource.Assumed : tempo.Source == TempoSource.Notated;
            if (!written || (marks.Count > 0 && marks[^1].PerMinute == perMinute && marks[^1].Beat == beat))
            {
                continue;
            }

            marks.Add(new TempoMark(tempo.At, perMinute, tempo.Source == TempoSource.Inferred, beat));
        }

        return marks;
    }
}