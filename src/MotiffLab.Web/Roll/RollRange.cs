using MotiffLab.MusicCore.Time;

namespace MotiffLab.Web.Roll;

/// <summary>
/// A drag across the roll, from where it started to where it ended; a click on a bar number when it
/// barely moved. <paramref name="Extend"/> marks a Shift-click on a bar number, which extends the
/// selection to that bar.
/// </summary>
public readonly record struct RollRange(Tick Anchor, Tick Current, bool IsClick, bool Extend = false);
/// <summary>A note pointed at on the score (0018 section 4): when it starts and its key. Each one is a new point, so pointing at the same note again shows it again.</summary>
public sealed class NotePoint(long tick, int pitch)
{
    public long Tick { get; } = tick;

    public int Pitch { get; } = pitch;
}