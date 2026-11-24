namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>The key signatures and meters a staff prints, read from its accidental and digit glyphs.</summary>
internal static class Signatures
{
    private const string SharpOrder = "FCGDAEB";

    private const string FlatOrder = "BEADGCF";

    /// <summary>
    /// A key signature printed from <paramref name="start"/> on: a run of accidentals a space or so apart, the first close to
    /// the start, in the order sharps or flats are written (F C G D A E B, or B E A D G C F), naturals cancelling the old key
    /// before them. None of them touches a notehead, as a note's own accidental does. Null when no such run is there.
    /// </summary>
    /// <returns>The fifths of the key and the glyphs it was read from.</returns>
    public static (int Fifths, List<PlacedGlyph> Glyphs)? Key(IEnumerable<PlacedGlyph> accidentals, IReadOnlyList<PlacedGlyph> heads, VectorStaff staff, VectorClef clef, double start)
    {
        double space = staff.Space;
        var run = new List<PlacedGlyph>();
        foreach (PlacedGlyph glyph in accidentals.Where(glyph => glyph.X > start - (0.2 * space)).OrderBy(glyph => glyph.X))
        {
            double previous = run.Count == 0 ? start : run[^1].Right;
            if (glyph.X - previous > (run.Count == 0 ? 3 : 1.2) * space || Touches(glyph, heads, space))
            {
                break;
            }

            run.Add(glyph);
        }

        List<PlacedGlyph> naturals = [.. run.TakeWhile(glyph => glyph.Glyph == MusicGlyph.AccidentalNatural)];
        MusicGlyph? kind = run.Skip(naturals.Count).Select(glyph => (MusicGlyph?)glyph.Glyph).FirstOrDefault();
        if (run.Count == 0 || kind is not (null or MusicGlyph.AccidentalSharp or MusicGlyph.AccidentalFlat))
        {
            return null;
        }

        if (kind is null)
        {
            return (0, naturals);
        }

        List<PlacedGlyph> signs = [.. run.Skip(naturals.Count).TakeWhile(glyph => glyph.Glyph == kind)];

        string order = signs[0].Glyph == MusicGlyph.AccidentalSharp ? SharpOrder : FlatOrder;
        int matched = 0;
        while (matched < signs.Count && matched < 7 && StepOf(signs[matched], staff, clef) == order[matched])
        {
            matched++;
        }

        if (matched == 0)
        {
            return null;
        }

        int fifths = signs[0].Glyph == MusicGlyph.AccidentalSharp ? matched : -matched;
        return (fifths, [.. naturals, .. signs.Take(matched)]);
    }

    /// <summary>The step letter at a glyph's height under a clef.</summary>
    public static char StepOf(PlacedGlyph glyph, VectorStaff staff, VectorClef clef) =>
        StaffPitch.StepOf(clef.Reference + staff.StepsAbove(glyph.Y, clef.LineY(staff)));

    /// <summary>
    /// The meters a staff prints, each where it stands: a column of digits, the upper number above the lower one (each may
    /// have two digits), or a common or cut time sign.
    /// </summary>
    public static List<(double X, VectorMeter Meter)> Meters(IReadOnlyList<PlacedGlyph> glyphs, VectorStaff staff, double normalSize)
    {
        double space = staff.Space;
        var meters = new List<(double X, VectorMeter Meter)>();
        foreach (PlacedGlyph sign in glyphs.Where(glyph => glyph.Glyph is MusicGlyph.TimeSigCommon or MusicGlyph.TimeSigCut))
        {
            meters.Add((sign.X, sign.Glyph == MusicGlyph.TimeSigCommon ? new VectorMeter(4, 4) : new VectorMeter(2, 2)));
        }

        // Digits of a meter stand inside the staff at the music's size; a fingering or a tuplet number is smaller or outside.
        List<PlacedGlyph> digits = [.. glyphs.Where(glyph => glyph.Glyph.IsDigit() && glyph.Size > 0.85 * normalSize
            && glyph.Y >= staff.Top - (0.3 * space) && glyph.Y <= staff.Bottom + (0.3 * space)).OrderBy(glyph => glyph.X)];
        var column = new List<PlacedGlyph>();
        foreach (PlacedGlyph digit in digits)
        {
            if (column.Count > 0 && digit.X > column.Max(glyph => glyph.Right) + (0.3 * space))
            {
                AddMeter(meters, column, space);
                column = [];
            }

            column.Add(digit);
        }

        AddMeter(meters, column, space);
        return [.. meters.OrderBy(meter => meter.X)];
    }

    private static void AddMeter(List<(double X, VectorMeter Meter)> meters, List<PlacedGlyph> column, double space)
    {
        if (column.Count < 2)
        {
            return;
        }

        double top = column.Min(glyph => glyph.Y), bottom = column.Max(glyph => glyph.Y);
        if (bottom - top < 1.5 * space)
        {
            return;
        }

        int upper = Number(column.Where(glyph => glyph.Y - top < 0.5 * space));
        int lower = Number(column.Where(glyph => bottom - glyph.Y < 0.5 * space));
        if (upper > 0 && lower is 1 or 2 or 4 or 8 or 16 or 32)
        {
            meters.Add((column.Min(glyph => glyph.X), new VectorMeter(upper, lower)));
        }
    }

    private static int Number(IEnumerable<PlacedGlyph> digits) => digits.OrderBy(glyph => glyph.X).Aggregate(0, (number, glyph) => (number * 10) + glyph.Glyph.Digit());

    /// <summary>Whether an accidental stands just left of a notehead at its height, as a note's own accidental does.</summary>
    private static bool Touches(PlacedGlyph accidental, IReadOnlyList<PlacedGlyph> heads, double space) =>
        heads.Any(head => Math.Abs(head.Y - accidental.Y) < 0.3 * space && head.X - accidental.Right > -0.3 * space && head.X - accidental.Right < 0.6 * space);
}