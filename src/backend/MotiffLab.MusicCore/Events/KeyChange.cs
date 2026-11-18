using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Events;

/// <summary>A key signature from a position onwards.</summary>
/// <param name="Fifths">Sharps (positive) or flats (negative), −7 to 7.</param>
public readonly record struct KeyChange
{
    public KeyChange(Tick at, int fifths)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fifths, -7);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fifths, 7);
        At = at;
        Fifths = fifths;
    }

    public Tick At { get; }

    public int Fifths { get; }
}