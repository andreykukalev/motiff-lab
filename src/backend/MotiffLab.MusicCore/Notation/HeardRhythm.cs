using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// The rhythm notes heard in a recording are written in for reading (<see cref="NotationGenerator.Generate"/>'s
/// <c>asHeard</c>, decision record 0016): onsets together as one chord, grace notes, onsets on the heard grid, and each
/// chord held to the next onset on its staff or cut at it.
/// </summary>
internal static class HeardRhythm
{
    /// <summary>Onsets this close are one chord when written as heard: a 64th.</summary>
    public const long HeardChord = NotationGenerator.Grid / 2;

    private const long Grid = NotationGenerator.Grid;

    /// <summary>A staff's notes written as heard (see <see cref="NotationGenerator.Generate"/>'s <c>asHeard</c>).</summary>
    /// <param name="counts">Counts the notes held, cut and written as grace notes.</param>
    /// <param name="together">Where each note starts for writing, worked out over every staff (<see cref="HeardOnsets"/>).</param>
    public static ShownNote[] AsHeard(ShownNote[] notes, HeardCounts counts, TupletSpan[] tuplets, HeardOnsets together)
    {
        List<List<ShownNote>> chords = Chords(notes, together);
        bool[] grace = Graces(chords, together);
        long[] onsets = Onsets(chords, grace, tuplets, together);

        var written = new List<ShownNote>(notes.Length);
        for (int i = 0; i < chords.Count; i++)
        {
            int next = Array.FindIndex(grace, i + 1, flag => !flag);
            if (grace[i])
            {
                // Played just before the next chord, written in front of it.
                counts.Graces++;
                written.Add(new ShownNote(chords[i][0].Note, onsets[next], onsets[next]) { IsGrace = true, Target = chords[next][0].Note.Id });
                continue;
            }

            long end = End(chords[i], onsets, i, next, tuplets, counts);
            written.AddRange(chords[i].Select(note => new ShownNote(note.Note, onsets[i], end)));
        }

        return [.. written];
    }

    /// <summary>Each grace note in front of the chord that holds the note it leads to.</summary>
    public static void AttachGraces(StaffPlan staff, ShownNote[] graces)
    {
        foreach (ShownNote grace in graces)
        {
            Chord chord = staff.Voices.SelectMany(voice => voice).First(chord => chord.Notes.Any(note => note.Note.Id == grace.Target));
            chord.Graces.Add(new Chord(chord.Onset, chord.Onset, [grace]));
        }
    }

    /// <summary>The notes in the order written, onsets that start together as one chord.</summary>
    private static List<List<ShownNote>> Chords(ShownNote[] notes, HeardOnsets together)
    {
        var chords = new List<List<ShownNote>>();
        foreach (ShownNote note in notes.OrderBy(note => together.Onset(note.Note)).ThenBy(note => note.Note.Pitch.MidiNumber).ThenBy(note => note.Note.Id, StringComparer.Ordinal))
        {
            if (chords.Count > 0 && together.Onset(note.Note) - together.Onset(chords[^1][0].Note) < HeardChord)
            {
                chords[^1].Add(note);
            }
            else
            {
                chords.Add([note]);
            }
        }

        return chords;
    }

    /// <summary>
    /// Which chords are grace notes: a single note shorter than a 32nd, with the next onset within a dotted 32nd, as is
    /// one that leads into a spread chord; never two in a row.
    /// </summary>
    private static bool[] Graces(List<List<ShownNote>> chords, HeardOnsets together)
    {
        bool[] grace = new bool[chords.Count];
        for (int i = 0; i + 1 < chords.Count; i++)
        {
            Note note = chords[i][0].Note;
            long next = together.Onset(chords[i + 1][0].Note);
            grace[i] = chords[i].Count == 1
                && ((note.Span.Duration < Grid && next - together.Onset(note) < Grid * 3 / 2) || together.LeadsInto(note, next))
                && (i == 0 || !grace[i - 1]);
        }

        return grace;
    }

    /// <summary>Where each chord but a grace note is written to start: on the heard grid, or a 32nd, always after the one before.</summary>
    private static long[] Onsets(List<List<ShownNote>> chords, bool[] grace, TupletSpan[] tuplets, HeardOnsets together)
    {
        long[] onsets = new long[chords.Count];
        long previous = -1;
        for (int i = 0; i < chords.Count; i++)
        {
            if (grace[i])
            {
                continue;
            }

            long raw = together.Onset(chords[i][0].Note);
            long onset = TupletBeats.SnapIn(raw, tuplets, NotationGenerator.HeardGrid);
            onset = onset > previous ? onset : TupletBeats.SnapIn(raw, tuplets, Grid);
            onset = onset > previous ? onset : previous + TupletBeats.StepAt(previous, tuplets);
            onsets[i] = previous = onset;
        }

        return onsets;
    }

    /// <summary>
    /// Where a chord is written to end: with one length, that of its longest note, so that it stays one chord; at the
    /// next onset when it would sound into it (cut) or stop less than <see cref="NotationGenerator.HeldGap"/> before it (held),
    /// its notes counted in <paramref name="counts"/>.
    /// </summary>
    private static long End(List<ShownNote> chord, long[] onsets, int i, int next, TupletSpan[] tuplets, HeardCounts counts)
    {
        long end = Math.Max(TupletBeats.SnapIn(chord.Max(note => note.Note.Span.End.Value), tuplets, NotationGenerator.HeardGrid), onsets[i] + TupletBeats.StepAt(onsets[i], tuplets));
        if (next >= 0 && end > onsets[next])
        {
            end = onsets[next];
            counts.Cut += chord.Count;
        }
        else if (next >= 0 && onsets[next] - end < NotationGenerator.HeldGap && end < onsets[next])
        {
            end = onsets[next];
            counts.Held += chord.Count;
        }

        return end;
    }
}