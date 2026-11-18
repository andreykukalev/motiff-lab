namespace MotiffLab.MusicCore.Notation;

/// <summary>A beat written as a tuplet: three (triplet eighths in a quarter) or six (triplet 16ths) values in the time of two or four.</summary>
internal readonly record struct TupletSpan(long Start, long Length, int Num)
{
    public long End => Start + Length;

    /// <summary>The time between two of its onsets.</summary>
    public long Step => Length / Num;
}