using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>One set of staves over the whole piece: the original, or an ossia above it.</summary>
internal sealed record PartPlan(List<StaffPlan> Staves, ControlEvents[] Controls, PlayingMarks? Marks, HeardCounts Heard, HashSet<string> Shortened, int Approximated, int SlurCount, int PedalCount)
{
    public int FirstStaff => Staves[0].Number;

    public NotationStaff[] StaffInfo => [.. Staves.Select(staff => new NotationStaff(staff.Number, staff.Track, staff.Clef, staff.Hand, staff.IsSplitInferred))];

    /// <summary>The part's staves laid out in a bar, with the marks of the playing that start there.</summary>
    public PartMeasure Measure(Bar bar, int index, int fifths, TransposedSpelling? spelling) => new(FirstStaff, [.. Staves.Select(staff => StaffLayout.LayOut(staff, bar, fifths, spelling))])
    {
        Dynamic = Marks is not null && Marks.Dynamics.TryGetValue(index, out (long At, string Level) dynamic) ? dynamic : null,
        TempoWord = Marks is not null && Marks.TempoWords.TryGetValue(index, out string? word) ? word : null,
        Controls = Controls[index],
    };
}