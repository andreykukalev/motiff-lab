namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>
/// Finds the tuplets of a system from their printed numbers: a number in a bracket marks the notes under the bracket; a
/// number with no bracket marks the notes of the beam beside it. A digit near neither (a fingering, a bar number) is no
/// tuplet.
/// </summary>
internal static class Tuplets
{
    /// <summary>The normal notes a tuplet of <paramref name="actual"/> notes takes the time of: 3 in 2, 5, 6 or 7 in 4, 2 in 3.</summary>
    public static int Normal(int actual) => actual switch
    {
        2 => 3,
        4 => 3,
        _ => 1 << (int)Math.Floor(Math.Log2(actual - 1)),
    };

    /// <summary>Marks the events of the system that the numbers near it stand for.</summary>
    public static void Mark(VectorPage page, VectorSystem system, List<VectorEvent> events)
    {
        double space = system.Space;
        double top = system.Staves[0].Top - (8 * space), bottom = system.Staves[^1].Bottom + (8 * space);
        foreach (PlacedNumber number in page.Numbers.Where(number => number.Value >= 2 && number.Middle > top && number.Middle < bottom
            && number.X > system.Left && number.X < system.Right))
        {
            List<VectorEvent> group = Bracketed(page, number, events, space) ?? Beamed(page, number, events, space);
            foreach (VectorEvent item in group.Where(item => !item.Grace && item.Tuplet == (1, 1)))
            {
                item.Tuplet = (number.Value, Normal(number.Value));
            }
        }
    }

    /// <summary>The notes under a bracket the number stands in: a line ending just before it and one starting just after it.</summary>
    private static List<VectorEvent>? Bracketed(VectorPage page, PlacedNumber number, List<VectorEvent> events, double space)
    {
        List<Stroke> lines = [.. page.Horizontal, .. page.Slanted];
        Stroke? left = lines.Where(line => number.X - line.X1 > -0.2 * space && number.X - line.X1 < 1.5 * space && Math.Abs(line.Y1 - number.Middle) < space)
            .Cast<Stroke?>().FirstOrDefault();
        Stroke? right = lines.Where(line => line.X0 - number.Right > -0.2 * space && line.X0 - number.Right < 1.5 * space && Math.Abs(line.Y0 - number.Middle) < space)
            .Cast<Stroke?>().FirstOrDefault();
        if (left is not { } from || right is not { } to)
        {
            return null;
        }

        List<VectorEvent> under = [.. events.Where(item => item.Anchor >= from.X0 - (0.6 * space) && item.Anchor <= to.X1 + (0.3 * space))];
        return Nearest(under, number);
    }

    /// <summary>The notes on the beam the number stands beside, above or below it.</summary>
    private static List<VectorEvent> Beamed(VectorPage page, PlacedNumber number, List<VectorEvent> events, double space)
    {
        double middle = (number.X + number.Right) / 2;
        int beam = Enumerable.Range(0, page.Quads.Count)
            .Where(index => page.Quads[index].Left <= middle && page.Quads[index].Right >= middle
                && Math.Abs(page.Quads[index].MiddleAt(middle) - number.Middle) < 2.5 * space)
            .OrderBy(index => Math.Abs(page.Quads[index].MiddleAt(middle) - number.Middle))
            .DefaultIfEmpty(-1).First();
        return beam < 0 ? [] : [.. events.Where(item => item.Beam == beam)];
    }

    /// <summary>
    /// Of the notes under a bracket, those of the staff nearest the number: its notes with the stem direction of the nearest
    /// one when they make a whole tuplet (a length the number divides into a plain note value), else all of them.
    /// </summary>
    private static List<VectorEvent> Nearest(List<VectorEvent> under, PlacedNumber number)
    {
        if (under.Count == 0)
        {
            return under;
        }

        double Gap(VectorEvent item) => Math.Abs((item.Tip ?? item.Heads.Select(head => head.Y).DefaultIfEmpty(item.RestY).Average()) - number.Middle);
        VectorEvent closest = under.MinBy(Gap)!;
        List<VectorEvent> staff = [.. under.Where(item => item.Staff == closest.Staff && !item.Grace)];
        List<VectorEvent> sameStem = [.. staff.Where(item => item.StemUp is null || closest.StemUp is null || item.StemUp == closest.StemUp)];
        return Whole(sameStem, number.Value) ? sameStem : staff;
    }

    private static bool Whole(List<VectorEvent> group, int actual)
    {
        int total = group.Sum(item => BarTiming.Dotted(item.Written, item.Dots));
        return total % actual == 0 && (total / actual) is 30 or 60 or 120 or 240 or 480 or 960;
    }
}