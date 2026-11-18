using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// The staves a part's notes go on, the clef of each, and the voices of a staff: each track on a staff, or on two for a
/// piano part (by imported hands, or split at middle C for a single track without hands); notes that start and end
/// together as chords; overlapping chords in voices of their own, up to <see cref="NotationGenerator.MaxVoicesPerStaff"/>.
/// </summary>
internal static class StaffVoices
{
    private const int MiddleC = 60;

    /// <summary>The staves for the notes, numbered from <paramref name="firstStaff"/> down.</summary>
    public static List<StaffPlan> PlanStaves(ShownNote[] notes, int firstStaff)
    {
        int[] tracks = [.. notes.Select(note => note.Note.Track).Distinct().Order()];
        var staves = new List<StaffPlan>();
        if (tracks.Length == 0)
        {
            staves.Add(new StaffPlan(firstStaff, 0, Clef.Treble, null, false, []));
            return staves;
        }

        foreach (int track in tracks)
        {
            ShownNote[] trackNotes = [.. notes.Where(note => note.Note.Track == track)];
            bool hasHands = trackNotes.Any(note => note.Note.Hand is not null);
            bool spansBothClefs = trackNotes.Any(note => note.Note.Pitch.MidiNumber < MiddleC - 5)
                && trackNotes.Any(note => note.Note.Pitch.MidiNumber > MiddleC + 5);

            if (hasHands || (tracks.Length == 1 && spansBothClefs))
            {
                static bool IsRight(ShownNote note) => note.Note.Hand?.Value is { } hand ? hand == Hand.Right : note.Note.Pitch.MidiNumber >= MiddleC;
                ShownNote[] right = [.. trackNotes.Where(IsRight)];
                ShownNote[] left = [.. trackNotes.Where(note => !IsRight(note))];
                staves.Add(new StaffPlan(firstStaff + staves.Count, track, ClefFor(right, Clef.Treble), Hand.Right, !hasHands, right));
                staves.Add(new StaffPlan(firstStaff + staves.Count, track, ClefFor(left, Clef.Bass), Hand.Left, !hasHands, left));
            }
            else
            {
                staves.Add(new StaffPlan(firstStaff + staves.Count, track, ClefFor(trackNotes, Clef.Treble), null, false, trackNotes));
            }
        }

        return staves;
    }

    /// <summary>
    /// The clef a staff's notes read best in: treble when most are at or above middle C, bass when most are below, and
    /// <paramref name="usual"/> on a tie or with no notes. So a right hand written low, as in a piece on two bass clefs
    /// (v0.5, issue #479, item 15), is not shown on ledger lines under a treble clef. A choice for showing the notes, made
    /// from their pitches; the notes do not change.
    /// </summary>
    public static Clef ClefFor(ShownNote[] notes, Clef usual)
    {
        int high = notes.Count(note => note.Note.Pitch.MidiNumber >= MiddleC);
        int low = notes.Length - high;
        return high > low ? Clef.Treble : low > high ? Clef.Bass : usual;
    }

    /// <summary>Chords in voices that never overlap, the highest voice first; adds the id of each note it shortens to <paramref name="shortened"/>.</summary>
    public static List<List<Chord>> AssignVoices(ShownNote[] notes, HashSet<string> shortened)
    {
        Chord[] chords = [.. notes
            .GroupBy(note => (note.Onset, note.End))
            .Select(group => new Chord(group.Key.Onset, group.Key.End, [.. group.OrderBy(note => note.Note.Pitch.MidiNumber).ThenBy(note => note.Note.Id, StringComparer.Ordinal)]))
            .OrderBy(chord => chord.Onset)
            .ThenByDescending(chord => chord.Notes[^1].Note.Pitch.MidiNumber)
            .ThenByDescending(chord => chord.End)];

        var voices = new List<List<Chord>>();
        foreach (Chord chord in chords)
        {
            List<Chord>? voice = voices.FirstOrDefault(candidate => candidate[^1].End <= chord.Onset);
            if (voice is null && voices.Count < NotationGenerator.MaxVoicesPerStaff)
            {
                voice = [];
                voices.Add(voice);
            }
            else if (voice is null)
            {
                // Every voice is busy: shorten the chord that ends first, or join it if it starts here too.
                voice = voices.MinBy(candidate => candidate[^1].End)!;
                Chord previous = voice[^1];
                if (previous.Onset == chord.Onset)
                {
                    long joinedEnd = Math.Min(previous.End, chord.End);
                    foreach (ShownNote note in previous.Notes.Concat(chord.Notes).Where(note => note.End > joinedEnd))
                    {
                        shortened.Add(note.Note.Id);
                    }

                    previous.Notes = [.. previous.Notes.Concat(chord.Notes).OrderBy(note => note.Note.Pitch.MidiNumber).ThenBy(note => note.Note.Id, StringComparer.Ordinal)];
                    previous.End = joinedEnd;
                    continue;
                }

                foreach (ShownNote note in previous.Notes)
                {
                    shortened.Add(note.Note.Id);
                }

                previous.End = chord.Onset;
            }

            voice.Add(chord);
        }

        return [.. voices.OrderByDescending(voice => voice.SelectMany(chord => chord.Notes).Average(note => note.Note.Pitch.MidiNumber))];
    }
}