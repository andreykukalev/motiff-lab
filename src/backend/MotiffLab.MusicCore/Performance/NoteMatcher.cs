using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>
/// Printed notes paired with played notes over the whole piece (<see cref="PerformanceComparison"/>): printed notes in
/// order of onset, each takes the nearest unmatched played note of the same pitch within half a beat of it in score time.
/// </summary>
internal static class NoteMatcher
{
    /// <summary>The match of each printed note heard, by its id, and which played notes are taken.</summary>
    public static (Dictionary<string, NoteMatch> MatchOf, bool[] Used) Match(List<Note> printed, MeterMap meter, List<PlacedNote> placed)
    {
        Dictionary<int, List<int>> byPitch = [];
        for (int k = 0; k < placed.Count; k++)
        {
            if (!byPitch.TryGetValue(placed[k].Pitch.MidiNumber, out List<int>? list))
            {
                byPitch[placed[k].Pitch.MidiNumber] = list = [];
            }

            list.Add(k);
        }

        bool[] used = new bool[placed.Count];
        Dictionary<string, NoteMatch> matchOf = new(StringComparer.Ordinal);
        foreach (Note note in printed)
        {
            if (!byPitch.TryGetValue(note.Pitch.MidiNumber, out List<int>? candidates))
            {
                continue;
            }

            long reach = meter.SignatureAt(note.Span.Onset).BeatTicks / 2;
            int best = -1;
            long bestDistance = long.MaxValue;
            foreach (int k in candidates)
            {
                long distance = Math.Abs(placed[k].At.Value - note.Span.Onset.Value);
                if (!used[k] && distance <= reach && distance < bestDistance)
                {
                    best = k;
                    bestDistance = distance;
                }
            }

            if (best >= 0)
            {
                used[best] = true;
                matchOf[note.Id] = new NoteMatch(note.Id, placed[best].Id, placed[best].At.Value - note.Span.Onset.Value);
            }
        }

        return (matchOf, used);
    }
}