namespace MotiffLab.MusicCore.Notation;

/// <summary>The marks of the playing that start in one bar of a part, written after its staves (<see cref="ControlPlan"/>).</summary>
internal sealed class ControlEvents
{
    public List<HairpinPlan> Hairpins { get; } = [];

    public List<(Chord From, Chord To, int Staff)> Slurs { get; } = [];

    /// <summary>"Ped." (down) or "*" (up) at a tick, under a staff.</summary>
    public List<(long At, bool Down, int Staff)> Pedals { get; } = [];

    /// <summary>Fermatas over the top staff, at the onsets of the chords held.</summary>
    public List<long> Fermatas { get; } = [];
}

/// <summary>A crescendo or diminuendo from an onset: a hairpin, or a word.</summary>
/// <param name="Tstamp2">Where it ends: bars ahead and the beat in that bar, as MEI writes it; none for "cresc." or "dim." written as a word.</param>
internal sealed record HairpinPlan(long At, bool Crescendo, string? Tstamp2);