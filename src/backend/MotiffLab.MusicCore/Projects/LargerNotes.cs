namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// «Ноты крупнее» (version 1.7, the score-reading spec 4.3; project file 19, "largerNotes"): the step the player chose for
/// a piece, 0 for the A4 layout as it is, each step 10 % larger notes than the one before. Only the step is kept; the
/// layout it gives is the app's, the same every time for the same step, page size and orientation (the 1.7 plan's lead
/// decision 2). Ten steps reach 200 %, past the landscape 11″ screen's layout of the spec (about 1.7 times today's notes,
/// step 7).
/// </summary>
internal static class LargerNotes
{
    /// <summary>The largest step: 200 %.</summary>
    public const int MaxStep = 10;

    /// <summary>How much larger each step makes the notes, in per cent of the A4 layout.</summary>
    public const int PercentPerStep = 10;

    /// <summary>Whether <paramref name="step"/> is one the player can choose: 0 to <see cref="MaxStep"/>.</summary>
    public static bool IsValid(int step) => step is >= 0 and <= MaxStep;

    /// <summary>The size of the notes at <paramref name="step"/>, in per cent of the A4 layout: 100 at step 0.</summary>
    public static int Percent(int step) => 100 + (PercentPerStep * step);

    /// <summary>A step a file holds, brought into 0 to <see cref="MaxStep"/>: a later build's larger one reads as the largest here.</summary>
    public static int Clamped(double step) => (int)Math.Clamp(step, 0, MaxStep);
}