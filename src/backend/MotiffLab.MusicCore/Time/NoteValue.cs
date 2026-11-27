namespace MotiffLab.MusicCore.Time;

/// <summary>
/// Written note values: how long each of MusicXML's named values is, and a value with its dots, each dot adding half of what
/// the one before added. The one rule the readers (MusicXML, scans, vector PDFs) and the notation writer share.
/// </summary>
internal static class NoteValue
{
    private static readonly Dictionary<string, QuarterFraction> s_named = new(StringComparer.Ordinal)
    {
        ["breve"] = QuarterFraction.FromQuarters(8),
        ["whole"] = QuarterFraction.FromQuarters(4),
        ["half"] = QuarterFraction.FromQuarters(2),
        ["quarter"] = QuarterFraction.FromQuarters(1),
        ["eighth"] = new(1, 2),
        ["16th"] = new(1, 4),
        ["32nd"] = new(1, 8),
        ["64th"] = new(1, 16),
        ["128th"] = new(1, 32),
    };

    /// <summary>The values by MusicXML's names, from the breve to the 128th.</summary>
    public static IReadOnlyDictionary<string, QuarterFraction> Named => s_named;

    /// <summary>A value with its dots: 1 + 1/2 + … + 1/2^dots of it.</summary>
    public static QuarterFraction Dotted(QuarterFraction value, int dots)
    {
        long half = 1L << dots;
        return new QuarterFraction(value.Numerator * ((2 * half) - 1), value.Denominator * half);
    }

    /// <summary>A length in ticks with its dots, as <see cref="Dotted(QuarterFraction, int)"/>, rounded down to a tick.</summary>
    public static int Dotted(int ticks, int dots)
    {
        QuarterFraction dotted = Dotted(new QuarterFraction(ticks, 1), dots);
        return (int)(dotted.Numerator / dotted.Denominator);
    }
}