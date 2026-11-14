namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>Five staff lines of one staff on a page, y measured down, top line first.</summary>
internal sealed record VectorStaff(IReadOnlyList<double> Lines, double Left, double Right)
{
    /// <summary>The distance between two lines, the unit every tolerance of the reader is measured in.</summary>
    public double Space => (Lines[4] - Lines[0]) / 4;

    public double Top => Lines[0];

    public double Bottom => Lines[4];

    public double Middle => Lines[2];

    /// <summary>Half spaces from y up to the reference y, rounded: a notehead's steps above a clef's line.</summary>
    public int StepsAbove(double y, double reference) => (int)Math.Round((reference - y) / (Space / 2), MidpointRounding.AwayFromZero);
}

/// <summary>One system: the staves read together, top first, and the x of its bar lines.</summary>
internal sealed record VectorSystem(IReadOnlyList<VectorStaff> Staves, IReadOnlyList<double> BarLines)
{
    public double Left => Staves.Min(staff => staff.Left);

    public double Right => Staves.Max(staff => staff.Right);

    public double Space => Staves[0].Space;
}

/// <summary>Finds the staves of a page from its staff lines, the systems from the lines that join them, and the bar lines.</summary>
internal static class Staves
{
    /// <summary>A staff line runs at least this many points; shorter strokes are ledger lines, stems of beams, ties.</summary>
    private const double ShortestStaffLine = 40;

    public static List<VectorSystem> Find(VectorPage page)
    {
        List<VectorStaff> staves = FindStaves(page.Horizontal);
        var systems = new List<VectorSystem>();
        var current = new List<VectorStaff>();
        foreach (VectorStaff staff in staves)
        {
            if (current.Count > 0 && !Joined(page.Vertical, current[^1], staff))
            {
                systems.Add(Close(page, current));
                current = [];
            }

            current.Add(staff);
        }

        if (current.Count > 0)
        {
            systems.Add(Close(page, current));
        }

        return systems;
    }

    /// <summary>Five lines of one extent, evenly spaced, make a staff; a line belongs to one staff only.</summary>
    private static List<VectorStaff> FindStaves(IEnumerable<Stroke> horizontal)
    {
        Stroke[] lines = [.. Joined(horizontal).Where(line => line.X1 - line.X0 >= ShortestStaffLine)
            .OrderBy(line => line.Y0).ThenBy(line => line.X0)];
        var used = new bool[lines.Length];
        var staves = new List<VectorStaff>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (!used[i] && StaffFrom(lines, used, i) is { } staff)
            {
                staves.Add(staff);
            }
        }

        return [.. staves.OrderBy(staff => staff.Top).ThenBy(staff => staff.Left)];
    }

    /// <summary>
    /// Staff lines with the pieces of one line joined: MuseScore draws a staff line bar by bar, and some programs draw it
    /// twice; pieces at one height that touch or overlap are one line.
    /// </summary>
    private static List<Stroke> Joined(IEnumerable<Stroke> horizontal)
    {
        var joined = new List<Stroke>();
        foreach (IGrouping<double, Stroke> row in horizontal.GroupBy(line => Math.Round(line.Y0, 1)))
        {
            Stroke? open = null;
            foreach (Stroke piece in row.OrderBy(line => line.X0))
            {
                if (open is { } line && piece.X0 <= line.X1 + 1)
                {
                    open = line with { X1 = Math.Max(line.X1, piece.X1) };
                    continue;
                }

                if (open is { } done)
                {
                    joined.Add(done);
                }

                open = piece;
            }

            if (open is { } last)
            {
                joined.Add(last);
            }
        }

        return joined;
    }

    private static VectorStaff? StaffFrom(Stroke[] lines, bool[] used, int first)
    {
        var group = new List<int> { first };
        for (int j = first + 1; j < lines.Length && group.Count < 5; j++)
        {
            if (used[j] || Math.Abs(lines[j].X0 - lines[first].X0) > 1.5 || Math.Abs(lines[j].X1 - lines[first].X1) > 1.5)
            {
                continue;
            }

            double gap = lines[j].Y0 - lines[group[^1]].Y0;
            double expected = group.Count > 1 ? lines[group[1]].Y0 - lines[first].Y0 : gap;
            if (gap > 1.5 && gap < 15 && Math.Abs(gap - expected) < 0.3)
            {
                group.Add(j);
            }
            else if (gap >= 15)
            {
                break;
            }
        }

        if (group.Count < 5)
        {
            return null;
        }

        foreach (int index in group)
        {
            used[index] = true;
        }

        return new VectorStaff([.. group.Select(index => lines[index].Y0)], lines[first].X0, lines[first].X1);
    }

    /// <summary>Two staves are one system when an upright line runs from one to the other (the system's line or a bar line).</summary>
    private static bool Joined(IEnumerable<Stroke> vertical, VectorStaff upper, VectorStaff lower) =>
        lower.Top - upper.Bottom < 16 * upper.Space
        && vertical.Any(line => line.Y0 <= upper.Bottom + (0.3 * upper.Space) && line.Y1 >= lower.Top - (0.3 * upper.Space)
            && line.X0 >= Math.Min(upper.Left, lower.Left) - upper.Space && line.X0 <= Math.Max(upper.Right, lower.Right) + upper.Space);

    private static VectorSystem Close(VectorPage page, List<VectorStaff> staves) => new([.. staves], BarLines(page, staves));

    /// <summary>
    /// The x of the bar lines: upright lines across each staff of the system at one x, drawn as one line or staff by staff,
    /// with no notehead on them (a stem is not a bar line); a double or a thick and thin bar line counts once.
    /// </summary>
    private static List<double> BarLines(VectorPage page, List<VectorStaff> staves)
    {
        double space = staves[0].Space;
        List<double>[] perStaff = [.. staves.Select(staff => page.Vertical
            .Where(line => line.Y0 <= staff.Top + (0.3 * space) && line.Y1 >= staff.Bottom - (0.3 * space)
                && line.X0 >= staff.Left - space && line.X0 <= staff.Right + space
                && !page.Glyphs.Any(glyph => glyph.Glyph.IsNotehead() && Touches(glyph, line, space)))
            .Select(line => line.X0)
            .ToList())];
        double[] xs = [.. perStaff[0].Where(x => perStaff.All(others => others.Any(other => Math.Abs(other - x) < 0.3 * space))).Order()];
        var merged = new List<double>();
        foreach (double x in xs)
        {
            if (merged.Count == 0 || x - merged[^1] > 1.5 * space)
            {
                merged.Add(x);
            }
        }

        return merged;
    }

    private static bool Touches(PlacedGlyph head, Stroke line, double space) =>
        (Math.Abs(head.X - line.X0) < 0.3 * space || Math.Abs(head.Right - line.X0) < 0.3 * space)
        && head.Y >= line.Y0 - (0.6 * space) && head.Y <= line.Y1 + (0.6 * space);
}