using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Notation;

/// <summary>One staff over the whole piece: its number, track, clef and hand, its notes, voices and triplet beats.</summary>
internal sealed record StaffPlan(int Number, int Track, Clef Clef, Hand? Hand, bool IsSplitInferred, ShownNote[] Notes)
{
    public List<List<Chord>> Voices { get; set; } = [];

    /// <summary>The staff's beats written as triplets, in order.</summary>
    public TupletSpan[] Tuplets { get; init; } = [];
}