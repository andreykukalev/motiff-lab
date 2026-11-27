using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Transcription;

internal static partial class RecordingBars
{
    /// <summary>Onsets this close together are one chord: 30 ms.</summary>
    private const long ChordMicroseconds = 30_000;

    /// <summary>The notes' onsets with chords merged, each with its lowest pitch.</summary>
    private static Onset[] Cluster(IReadOnlyList<Note> notes, Clock clock)
    {
        var onsets = new List<Onset>();
        foreach (Note note in notes.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber))
        {
            long units = clock.Units(note.Span.Onset);
            long microseconds = units / Clock.UnitsPerMicrosecond;
            if (onsets.Count > 0 && microseconds - onsets[^1].Microseconds < ChordMicroseconds)
            {
                onsets[^1] = onsets[^1] with { Lowest = Math.Min(onsets[^1].Lowest, note.Pitch.MidiNumber) };
                continue;
            }

            onsets.Add(new Onset(note.Span.Onset, units, microseconds, note.Pitch.MidiNumber));
        }

        return [.. onsets];
    }

    /// <param name="Units">The onset's time in the recording, in <see cref="Clock"/> units.</param>
    private readonly record struct Onset(Tick Tick, long Units, long Microseconds, int Lowest);

    /// <summary>
    /// Time in the recording from canonical ticks and the tempo map, exactly: a tick at a tempo of
    /// m microseconds a quarter lasts m / 480 microseconds, so time is counted in 480ths of a microsecond.
    /// </summary>
    private sealed class Clock
    {
        public const long UnitsPerMicrosecond = CanonicalTime.TicksPerQuarter;

        private readonly (long At, long Units, long PerTick)[] _segments;

        public Clock(IReadOnlyList<MidiTempoChange> tempos)
        {
            var segments = new List<(long, long, long)>();
            long at = 0, units = 0, perTick = MidiTempoChange.DefaultMicrosecondsPerQuarter;
            foreach (MidiTempoChange tempo in tempos.OrderBy(tempo => tempo.At))
            {
                units += (tempo.At.Value - at) * perTick;
                at = tempo.At.Value;
                perTick = tempo.MicrosecondsPerQuarter;
                if (segments.Count > 0 && segments[^1].Item1 == at)
                {
                    segments[^1] = (at, units, perTick);
                    continue;
                }

                segments.Add((at, units, perTick));
            }

            if (segments.Count == 0 || segments[0].Item1 != 0)
            {
                segments.Insert(0, (0, 0, MidiTempoChange.DefaultMicrosecondsPerQuarter));
            }

            _segments = [.. segments];
        }

        public long Units(Tick tick)
        {
            (long at, long units, long perTick) = _segments[SegmentAt(segment => segment.At <= tick.Value)];
            return units + ((tick.Value - at) * perTick);
        }

        private int SegmentAt(Func<(long At, long Units, long PerTick), bool> starts)
        {
            int found = 0;
            for (int i = 1; i < _segments.Length && starts(_segments[i]); i++)
            {
                found = i;
            }

            return found;
        }
    }
}