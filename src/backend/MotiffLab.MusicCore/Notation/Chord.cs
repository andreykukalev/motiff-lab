namespace MotiffLab.MusicCore.Notation;

/// <summary>Notes of one staff written together in one voice: from one onset to one end, cut into pieces at bar lines.</summary>
internal sealed class Chord(long onset, long end, ShownNote[] notes)
{
    public long Onset { get; } = onset;

    public long End { get; set; } = end;

    /// <summary>Lowest pitch first.</summary>
    public ShownNote[] Notes { get; set; } = notes;

    /// <summary>Grace notes written in front of the chord, in the order played.</summary>
    public List<Chord> Graces { get; } = [];

    /// <summary>Played clearly louder than the notes around it.</summary>
    public bool IsAccented { get; set; }
}