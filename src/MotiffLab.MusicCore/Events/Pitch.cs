namespace MotiffLab.MusicCore.Events;

/// <summary>A sounding pitch as a MIDI note number: 60 is middle C (C4), 69 is A4.</summary>
/// <remarks>
/// Spelling (C♯ or D♭) is not part of the pitch. Notation spelling, when the source has it,
/// belongs to the note's source; spelling for display is derived from the key.
/// </remarks>
public readonly struct Pitch : IEquatable<Pitch>, IComparable<Pitch>
{
    public const int MaxMidiNumber = 127;

    private static readonly string[] s_names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];

    public Pitch(int midiNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(midiNumber);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(midiNumber, MaxMidiNumber);
        MidiNumber = midiNumber;
    }

    public int MidiNumber { get; }

    /// <summary>0 for C up to 11 for B.</summary>
    public int PitchClass => MidiNumber % 12;

    /// <summary>Scientific pitch notation octave: middle C is in octave 4.</summary>
    public int Octave => (MidiNumber / 12) - 1;

    public static bool operator ==(Pitch left, Pitch right) => left.Equals(right);

    public static bool operator !=(Pitch left, Pitch right) => !left.Equals(right);

    public static bool operator <(Pitch left, Pitch right) => left.MidiNumber < right.MidiNumber;

    public static bool operator >(Pitch left, Pitch right) => left.MidiNumber > right.MidiNumber;

    public static bool operator <=(Pitch left, Pitch right) => left.MidiNumber <= right.MidiNumber;

    public static bool operator >=(Pitch left, Pitch right) => left.MidiNumber >= right.MidiNumber;

    public int CompareTo(Pitch other) => MidiNumber.CompareTo(other.MidiNumber);

    public bool Equals(Pitch other) => MidiNumber == other.MidiNumber;

    public override bool Equals(object? obj) => obj is Pitch other && Equals(other);

    public override int GetHashCode() => MidiNumber;

    public override string ToString() => $"{s_names[PitchClass]}{Octave}";
}