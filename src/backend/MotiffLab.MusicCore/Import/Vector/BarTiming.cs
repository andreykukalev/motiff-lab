using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>
/// Places the events of one bar in time and in voices. Engraving puts everything that starts at one moment in one column, on
/// every staff, so the columns are taken left to right and the next column starts when the earliest of the notes still
/// sounding ends. A voice is a line of events each starting where the one before ends, a stem direction kept where it can be.
/// Nothing is stretched to make a bar fit: a bar that does not add up to its meter is reported.
/// </summary>
internal static class BarTiming
{
    /// <summary>
    /// Times the bar and numbers its voices, four a staff (staff 1 has voices 1 to 4, staff 2 from 5). A whole rest that
    /// fills the bar lasts as long as the bar is read on the other staves, or its meter when it is alone.
    /// </summary>
    /// <returns>Where each staff's longest voice ends: how long the bar is on that staff, 0 for an empty one.</returns>
    public static int[] Place(VectorBar bar, double space)
    {
        List<VectorEvent> all = [.. bar.Staves.SelectMany(events => events)];
        foreach (VectorEvent item in all)
        {
            item.Ticks = item.Grace || item.WholeBar ? 0 : NoteValue.Dotted(item.Written, item.Dots) * item.Tuplet.Normal / item.Tuplet.Actual;
            item.Onset = 0;
        }

        int read = Sweep(Columns(all, space));
        foreach (VectorEvent rest in all.Where(item => item.WholeBar))
        {
            rest.Ticks = read > 0 ? read : bar.InForce?.Ticks ?? 1920;
        }

        return [.. bar.Staves.Select((events, staff) => Voices(events, staff))];
    }

    /// <summary>The events that start together, column by column from the left; grace notes and whole-bar rests stand apart.</summary>
    private static List<List<VectorEvent>> Columns(List<VectorEvent> all, double space)
    {
        var columns = new List<List<VectorEvent>>();
        foreach (VectorEvent item in all.Where(item => !item.Grace && !item.WholeBar).OrderBy(item => item.Anchor))
        {
            if (columns.Count > 0 && (item.Anchor - columns[^1][0].Anchor < 1.2 * space || columns[^1].Any(other => Shifted(other, item, space))))
            {
                columns[^1].Add(item);
            }
            else
            {
                columns.Add([item]);
            }
        }

        return columns;
    }

    /// <summary>Gives each column its onset: the next starts when the earliest of the notes still sounding ends.</summary>
    /// <returns>Where the last note ends.</returns>
    private static int Sweep(List<List<VectorEvent>> columns)
    {
        int time = 0, read = 0;
        var sounding = new List<int>();
        foreach (List<VectorEvent> column in columns)
        {
            foreach (VectorEvent item in column)
            {
                item.Onset = time;
                sounding.Add(time + item.Ticks);
                read = Math.Max(read, time + item.Ticks);
            }

            sounding.RemoveAll(end => end <= time);
            time = sounding.Count > 0 ? sounding.Min() : time;
        }

        return read;
    }

    /// <summary>
    /// Whether a note stands beside another of the same staff only because the two voices would collide: the engraver moves
    /// one voice a notehead aside when its stem points the other way, so the two start together though their x differ.
    /// </summary>
    private static bool Shifted(VectorEvent other, VectorEvent item, double space) =>
        other.Staff == item.Staff && other.StemUp is bool first && item.StemUp is bool second && first != second
        && item.Anchor - other.Anchor < 2 * space && (other.Beam < 0 || other.Beam != item.Beam);

    /// <summary>
    /// The voice an event goes on: one ending where it starts with its stem direction, else one ending there with no
    /// direction to keep, else one that ended earlier (a voice resting); -1 for a new voice.
    /// </summary>
    private static int VoiceFor(List<(int End, bool? Up, int Number)> voices, VectorEvent item)
    {
        int found = voices.FindIndex(voice => voice.End == item.Onset && voice.Up == item.StemUp);
        if (found < 0)
        {
            found = voices.FindIndex(voice => voice.End == item.Onset && (voice.Up is null || item.StemUp is null));
        }

        return found >= 0 ? found : voices.FindIndex(voice => voice.End <= item.Onset && (voice.Up == item.StemUp || item.StemUp is null));
    }


    /// <summary>Gives each event of a staff its voice; a grace note joins the voice of the note it leads to.</summary>
    /// <returns>Where the staff's longest voice ends.</returns>
    private static int Voices(List<VectorEvent> events, int staff)
    {
        var voices = new List<(int End, bool? Up, int Number)>();
        foreach (VectorEvent item in events.Where(item => !item.Grace).OrderBy(item => item.Onset).ThenBy(item => item.X))
        {
            int found = VoiceFor(voices, item);
            if (found < 0)
            {
                voices.Add((0, item.StemUp, (staff * 4) + voices.Count + 1));
                found = voices.Count - 1;
            }

            item.Voice = voices[found].Number;
            voices[found] = (item.Onset + item.Ticks, item.StemUp ?? voices[found].Up, voices[found].Number);
        }

        foreach (VectorEvent grace in events.Where(item => item.Grace))
        {
            VectorEvent? next = events.Where(item => !item.Grace && item.X > grace.X).MinBy(item => item.X);
            grace.Onset = next?.Onset ?? voices.Select(voice => voice.End).DefaultIfEmpty(0).Max();
            grace.Voice = next?.Voice ?? (staff * 4) + 1;
        }

        return voices.Select(voice => voice.End).DefaultIfEmpty(0).Max();
    }
}