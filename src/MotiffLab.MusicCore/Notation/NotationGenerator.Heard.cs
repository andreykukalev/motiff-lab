using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Notation;

public static partial class NotationGenerator
{
    /// <summary>
    /// Where each note heard in a recording starts for writing (issue #456), worked out over every staff at once, so that
    /// a chord shared between the hands stays one chord: notes within a 64th of the first are one chord, as before; the
    /// onsets after it within a 32nd, while every note before them still sounds, make one spread chord with them when it
    /// holds three notes or more; and a single note in front of such a chord that stops within a 16th of it is played
    /// into the chord, a grace note in front of it. Each note of a chord starts where its first note does.
    /// </summary>
    private sealed class HeardOnsets
    {
        private readonly Dictionary<string, long> _onsets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _leadIns = new(StringComparer.Ordinal);

        public static HeardOnsets Of(IEnumerable<Note> notes)
        {
            var found = new HeardOnsets();
            Note[] sorted = [.. notes.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal)];
            int start = 0;
            while (start < sorted.Length)
            {
                int end = Together(sorted, start);
                int spread = end;
                while (spread < sorted.Length && Spreads(sorted, start, spread))
                {
                    spread = Together(sorted, spread);
                }

                int from = start;
                if (spread - start >= 3)
                {
                    // A single note that stops within a 16th of the chord after it leads into it.
                    if (end - start == 1 && spread - end >= 2 && sorted[start].Span.End.Value < sorted[end].Span.Onset.Value + HeardGrid)
                    {
                        found._leadIns[sorted[start].Id] = sorted[end].Span.Onset.Value;
                        found._onsets[sorted[start].Id] = sorted[start].Span.Onset.Value;
                        from = end;
                    }

                    end = spread;
                }

                foreach (Note note in sorted[from..end])
                {
                    found._onsets[note.Id] = sorted[from].Span.Onset.Value;
                }

                start = end;
            }

            return found;
        }

        /// <summary>Where the note starts for writing: its chord's first onset.</summary>
        public long Onset(Note note) => _onsets.TryGetValue(note.Id, out long onset) ? onset : note.Span.Onset.Value;

        /// <summary>Whether the note leads into the chord that starts at <paramref name="chord"/> for writing.</summary>
        public bool LeadsInto(Note note, long chord) => _leadIns.TryGetValue(note.Id, out long into) && into == chord;

        /// <summary>The index after the notes within a 64th of the one at <paramref name="start"/>.</summary>
        private static int Together(Note[] sorted, int start)
        {
            int end = start + 1;
            while (end < sorted.Length && sorted[end].Span.Onset.Value - sorted[start].Span.Onset.Value < HeardChord)
            {
                end++;
            }

            return end;
        }

        /// <summary>Whether the note at <paramref name="next"/> starts within a 32nd of the one at <paramref name="start"/> with every note between still sounding.</summary>
        private static bool Spreads(Note[] sorted, int start, int next)
        {
            long at = sorted[next].Span.Onset.Value;
            return at - sorted[start].Span.Onset.Value < Grid && sorted[start..next].All(note => note.Span.End.Value > at);
        }
    }
}