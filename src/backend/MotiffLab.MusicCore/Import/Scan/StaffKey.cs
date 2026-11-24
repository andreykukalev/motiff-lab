namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>
/// The clef and key in force on a staff: the diatonic step of the middle line (<see cref="StaffPitch"/>) and the key's
/// fifths. A staff position counts steps from the middle line, as the scan server's evidence does.
/// </summary>
internal readonly record struct StaffKey(int Middle, int Fifths)
{
    /// <summary>A treble clef: B4 on the middle line, in C.</summary>
    public static StaffKey Treble { get; } = new(StaffPitch.Middle('G', 2, 0), 0);

    /// <summary>A bass clef: D3 on the middle line, in C.</summary>
    public static StaffKey Bass { get; } = new(StaffPitch.Middle('F', 4, 0), 0);

    /// <summary>The MIDI pitch of a staff position under the clef: the key's alteration of its step, or <paramref name="alter"/> when an accidental gives one.</summary>
    public int Pitch(int position, int? alter)
    {
        int diatonic = Middle + position;
        char step = StaffPitch.StepOf(diatonic);
        return (int)StaffPitch.Midi(step, StaffPitch.OctaveOf(diatonic), alter ?? StaffPitch.KeyAlter(Fifths, step));
    }
}