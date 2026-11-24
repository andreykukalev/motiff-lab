namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>
/// Spells the notes of one staff of a system, bar by bar: the step from the notehead's height under the clef in force and
/// any octave line, the alteration from the accidental printed before it, else one printed earlier in the bar for that
/// step and octave, else the note it is tied from, else the key signature.
/// </summary>
internal sealed class Spelling(VectorStaff staff, Dictionary<PlacedGlyph, MusicGlyph> written, TieMap ties, (Func<double, VectorClef> ClefAt, Func<double, int> Octaves) at)
{
    private readonly Dictionary<PlacedGlyph, int> _alters = [];

    /// <summary>The alterations of notes tied over from the system before, by diatonic step.</summary>
    public Dictionary<int, int> FromBefore { get; init; } = [];

    /// <summary>The alterations of notes tied on to the next system, by diatonic step.</summary>
    public Dictionary<int, int> ToNext { get; } = [];

    public void Bar(IEnumerable<VectorEvent> events, int fifths)
    {
        var held = new Dictionary<int, int>();
        foreach (VectorEvent item in events)
        {
            foreach (PlacedGlyph head in item.Heads)
            {
                VectorClef clef = at.ClefAt(head.X);
                int diatonic = clef.Reference + staff.StepsAbove(head.Y, clef.LineY(staff));
                int sounding = diatonic + (7 * at.Octaves(head.X));
                char step = StaffPitch.StepOf(diatonic);
                int alter;
                if (written.TryGetValue(head, out MusicGlyph accidental))
                {
                    alter = held[diatonic] = accidental.Alter();
                }
                else if (ties.Back.TryGetValue(head, out PlacedGlyph? from) && _alters.TryGetValue(from, out int tied))
                {
                    alter = tied;
                }
                else if (ties.FromBefore.Contains(head) && FromBefore.TryGetValue(sounding, out int carried))
                {
                    alter = carried;
                }
                else
                {
                    alter = held.TryGetValue(diatonic, out int kept) ? kept : StaffPitch.KeyAlter(fifths, step);
                }

                _alters[head] = alter;
                if (ties.ToNext.Contains(head))
                {
                    ToNext[sounding] = alter;
                }

                item.Pitches.Add(new VectorPitch(step, alter, StaffPitch.OctaveOf(sounding), ties.Starts(head), ties.Stops(head)));
            }
        }
    }
}