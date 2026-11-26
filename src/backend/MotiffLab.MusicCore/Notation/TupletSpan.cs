namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// A span written as a tuplet: <paramref name="Num"/> values in the time of <paramref name="NumBase"/>; the generator's triplet
/// beats are three (triplet eighths in a quarter) or six (triplet 16ths) in the time of two or four, a score's as printed.
/// </summary>
internal readonly record struct TupletSpan(long Start, long Length, int Num, int NumBase)
{
    public long End => Start + Length;

    /// <summary>The time between two of its onsets.</summary>
    public long Step => Length / Num;
}