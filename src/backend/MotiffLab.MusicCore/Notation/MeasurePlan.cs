using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>A bar as written: its key, each part's staves laid out in it, and its metronome marks.</summary>
internal sealed record MeasurePlan(Bar Bar, int Fifths, PartMeasure[] Parts)
{
    /// <summary>The metronome marks in the bar (version 0.5.4).</summary>
    public TempoMark[] Tempos { get; init; } = [];

    /// <summary>The MEI number of the original's top staff, which the marks stand over.</summary>
    public int TempoStaff { get; init; } = 1;
}

/// <summary>One part's staves in a bar, with the marks of the playing that start there.</summary>
/// <param name="FirstStaff">The MEI number of the part's top staff; the others follow it.</param>
internal sealed record PartMeasure(int FirstStaff, StaffMeasure[] Staves)
{
    /// <summary>A dynamic written under the top staff, at an onset in the bar.</summary>
    public (long At, string Level)? Dynamic { get; init; }

    /// <summary>A tempo word written over the top staff at the bar's start.</summary>
    public string? TempoWord { get; init; }

    /// <summary>Hairpins, slurs and pedal marks that start in the bar.</summary>
    public ControlEvents Controls { get; init; } = new();
}

/// <summary>One staff in a bar: a layer of elements for each voice, empty where the voice has no note.</summary>
internal sealed record StaffMeasure(List<List<Element>> Layers);