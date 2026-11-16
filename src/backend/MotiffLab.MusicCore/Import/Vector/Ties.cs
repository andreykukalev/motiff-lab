namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>The ties of one staff of a system: which notehead each tie ends on came from, and the ties that cross a system break.</summary>
internal sealed class TieMap
{
    /// <summary>The notehead each tie ending in the system starts from.</summary>
    public Dictionary<PlacedGlyph, PlacedGlyph> Back { get; } = [];

    /// <summary>Noteheads whose tie runs on past the system's end.</summary>
    public HashSet<PlacedGlyph> ToNext { get; } = [];

    /// <summary>Noteheads a tie from the system before ends on.</summary>
    public HashSet<PlacedGlyph> FromBefore { get; } = [];

    public bool Starts(PlacedGlyph head) => ToNext.Contains(head) || Back.ContainsValue(head);

    public bool Stops(PlacedGlyph head) => FromBefore.Contains(head) || Back.ContainsKey(head);
}

/// <summary>
/// Finds ties: a curve from just after one notehead to just before the next at the same height. A tied note keeps the
/// accidental of the note it is tied from, across the bar line, and the two sound as one.
/// </summary>
internal static class Ties
{
    public static TieMap Find(IReadOnlyList<Curve> curves, VectorStaff staff, List<PlacedGlyph> heads, double firstHead, double systemEnd)
    {
        double space = staff.Space;
        var map = new TieMap();
        foreach (Curve curve in curves.Where(curve => curve.Right - curve.Left > 0.8 * space && curve.Bottom - curve.Top < 2 * space
            && curve.Bottom > staff.Top - (6 * space) && curve.Top < staff.Bottom + (6 * space)))
        {
            Add(map, curve, heads, space, (firstHead, systemEnd));
        }

        return map;
    }

    /// <summary>The tie a curve draws, if it draws one: between two noteheads, or from one past the system's end, or to one from before its start.</summary>
    private static void Add(TieMap map, Curve curve, List<PlacedGlyph> heads, double space, (double FirstHead, double SystemEnd) edges)
    {
        double middle = (curve.Top + curve.Bottom) / 2;
        bool Beside(PlacedGlyph head) => head.Y > curve.Top - space && head.Y < curve.Bottom + space;
        List<PlacedGlyph> starts = [.. heads.Where(head => Beside(head) && curve.Left > head.X - (0.2 * space) && curve.Left < head.Right + space)];
        List<PlacedGlyph> ends = [.. heads.Where(head => Beside(head) && curve.Right > head.X - space && curve.Right < head.Right + (0.2 * space))];
        (PlacedGlyph From, PlacedGlyph To)? pair = starts
            .SelectMany(from => ends.Where(to => to.X > from.Right - (0.2 * space) && Math.Abs(to.Y - from.Y) < 0.2 * space).Select(to => (From: from, To: to)))
            .OrderBy(tie => Math.Abs(tie.From.Y - middle)).Cast<(PlacedGlyph, PlacedGlyph)?>().FirstOrDefault();
        if (pair is { } tie)
        {
            map.Back.TryAdd(tie.To, tie.From);
        }
        else if (starts.Count > 0 && ends.Count == 0 && curve.Right > edges.SystemEnd - (1.5 * space))
        {
            map.ToNext.Add(starts.MinBy(head => Math.Abs(head.Y - middle))!);
        }
        else if (ends.Count > 0 && starts.Count == 0 && curve.Left < edges.FirstHead)
        {
            map.FromBefore.Add(ends.MinBy(head => Math.Abs(head.Y - middle))!);
        }
    }
}