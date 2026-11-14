namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>A stretch of a staff played an octave or two away from where it is written: from and to x, and the octaves.</summary>
internal readonly record struct Ottava(double From, double To, int Octaves);

/// <summary>
/// Finds the octave lines of a system: an 8 (or 15) above a staff, or below it, followed by a dashed line to where the
/// shift ends. The notes under the line sound an octave (two) higher, or lower when it is below. A line still running at
/// the system's end goes on, with no number, at the next system's start.
/// </summary>
internal static class Ottavas
{
    /// <summary>The octave lines of each staff of the system.</summary>
    /// <param name="page">The page.</param>
    /// <param name="system">The system.</param>
    /// <param name="staves">Every staff of the page: a number nearer another system's staff is that system's.</param>
    /// <param name="state">What carries from the system before.</param>
    public static List<Ottava>[] Find(VectorPage page, VectorSystem system, IReadOnlyList<VectorStaff> staves, ReadingState state)
    {
        double space = system.Space;
        List<Ottava>[] found = [.. system.Staves.Select(_ => new List<Ottava>())];
        foreach ((PlacedNumber first, int value, double right) in Figures(page.Numbers))
        {
            if (value is not (8 or 15) || first.X < system.Left - space || first.X > system.Right)
            {
                continue;
            }

            int staff = Enumerable.Range(0, system.Staves.Count).MinBy(index => Distance(system.Staves[index], first.Middle));
            VectorStaff near = system.Staves[staff];
            double distance = Distance(near, first.Middle);
            if (distance < 0.5 * space || distance > 8 * space || staves.Any(other => Distance(other, first.Middle) < distance)
                || Line(page, right, first.Middle, space, 4) is not { } end)
            {
                continue;
            }

            int octaves = (value == 8 ? 1 : 2) * (first.Middle < near.Top ? 1 : -1);
            found[staff].Add(new Ottava(first.X, end, octaves));
        }

        for (int staff = 0; staff < system.Staves.Count; staff++)
        {
            Carry(page, system, state, staff, found[staff]);
        }

        return found;
    }

    /// <summary>A line carried on from the system before, and whether this system's last line runs on to the next.</summary>
    private static void Carry(VectorPage page, VectorSystem system, ReadingState state, int staff, List<Ottava> found)
    {
        double space = system.Space;
        VectorStaff near = system.Staves[staff];
        int carried = state.OttavaOf(staff);
        if (carried != 0)
        {
            double y = carried > 0 ? near.Top : near.Bottom;
            Stroke? start = page.Horizontal.Where(line => line.X0 >= system.Left - space && line.X0 < system.Left + (12 * space)
                    && Math.Abs(line.Y0 - y) > space && Math.Abs(line.Y0 - y) < 8 * space && (line.Y0 < y) == (carried > 0)
                    && !found.Any(ottava => ottava.From < line.X1))
                .OrderBy(line => line.X0).Cast<Stroke?>().FirstOrDefault();
            if (start is { } line && Line(page, line.X0, line.Y0, space, 0.5) is { } end)
            {
                found.Insert(0, new Ottava(system.Left, end, carried));
            }
        }

        Ottava? last = found.Count > 0 ? found[^1] : null;
        state.SetOttava(staff, last is { } open && open.To >= system.BarLines.DefaultIfEmpty(system.Right).Max() - space ? open.Octaves : 0);
    }

    /// <summary>Where a dashed line starting within <paramref name="reach"/> spaces after x, at height y, ends; null when none starts there.</summary>
    private static double? Line(VectorPage page, double x, double y, double space, double reach)
    {
        List<Stroke> dashes = [.. page.Horizontal.Where(line => Math.Abs(line.Y0 - y) < 0.8 * space && line.X1 > x).OrderBy(line => line.X0)];
        double? end = null;
        foreach (Stroke dash in dashes)
        {
            double from = end ?? x;
            if (dash.X0 - from > (end is null ? reach : 2) * space || dash.X0 < x - (0.5 * space))
            {
                if (end is not null)
                {
                    break;
                }

                continue;
            }

            end = Math.Max(end ?? dash.X1, dash.X1);
        }

        return end;
    }

    /// <summary>The numbers of the page, a digit joined to the one right after it ("1" and "5" make 15).</summary>
    private static IEnumerable<(PlacedNumber First, int Value, double Right)> Figures(List<PlacedNumber> numbers)
    {
        foreach (PlacedNumber number in numbers)
        {
            PlacedNumber? next = numbers.FirstOrDefault(other => Math.Abs(other.Y - number.Y) < 0.1 * number.Size
                && other.X - number.Right > -0.05 * number.Size && other.X - number.Right < 0.2 * number.Size);
            bool follows = numbers.Any(other => Math.Abs(other.Y - number.Y) < 0.1 * number.Size
                && number.X - other.Right > -0.05 * number.Size && number.X - other.Right < 0.2 * number.Size);
            if (!follows)
            {
                yield return next is null ? (number, number.Value, number.Right) : (number, (number.Value * 10) + next.Value, next.Right);
            }
        }
    }

    private static double Distance(VectorStaff staff, double y) => y < staff.Top ? staff.Top - y : y > staff.Bottom ? y - staff.Bottom : 0;
}