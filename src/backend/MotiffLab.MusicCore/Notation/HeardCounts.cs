namespace MotiffLab.MusicCore.Notation;

/// <summary>What writing notes as heard changed (<see cref="HeardRhythm"/>): notes held to the next, cut at it, and written as grace notes.</summary>
internal sealed class HeardCounts
{
    public int Held { get; set; }

    public int Cut { get; set; }

    public int Graces { get; set; }
}