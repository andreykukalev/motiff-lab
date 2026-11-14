namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>Which staff each glyph of a page belongs to.</summary>
internal static class StaffAssignment
{
    /// <summary>
    /// Each glyph on its staff. A glyph within or near a staff is that staff's. A notehead far outside, between two staves,
    /// is the staff its ledger lines lead from; an accidental or a dot goes with the notehead at its height beside it. Any
    /// other glyph goes to the staff nearest it, when it stands within the staff's width and a few spaces of its lines.
    /// </summary>
    public static Dictionary<VectorStaff, List<PlacedGlyph>> ByStaff(VectorPage page, List<VectorSystem> systems)
    {
        List<VectorStaff> staves = [.. systems.SelectMany(system => system.Staves)];
        Dictionary<VectorStaff, List<PlacedGlyph>> glyphs = staves.ToDictionary(staff => staff, _ => new List<PlacedGlyph>());
        var heads = new Dictionary<PlacedGlyph, VectorStaff>();
        foreach (PlacedGlyph head in page.Glyphs.Where(glyph => glyph.Glyph.IsNotehead()))
        {
            if (OfHead(head, Beside(staves, head), page.Horizontal) is { } staff)
            {
                heads[head] = staff;
                glyphs[staff].Add(head);
            }
        }

        foreach (PlacedGlyph glyph in page.Glyphs.Where(glyph => !glyph.Glyph.IsNotehead()))
        {
            List<VectorStaff> beside = Beside(staves, glyph);
            VectorStaff? nearest = beside.MinBy(staff => Distance(staff, glyph.Y));
            if (nearest is null || Distance(nearest, glyph.Y) >= 8 * nearest.Space)
            {
                continue;
            }

            VectorStaff? owner = Distance(nearest, glyph.Y) > nearest.Space && (glyph.Glyph.IsAccidental() || glyph.Glyph == MusicGlyph.AugmentationDot)
                ? HeadBeside(glyph, heads, nearest.Space)
                : null;
            glyphs[owner ?? nearest].Add(glyph);
        }

        return glyphs;
    }

    private static List<VectorStaff> Beside(List<VectorStaff> staves, PlacedGlyph glyph) =>
        [.. staves.Where(staff => glyph.X >= staff.Left - (2 * staff.Space) && glyph.X <= staff.Right + staff.Space)];

    private static VectorStaff? OfHead(PlacedGlyph head, List<VectorStaff> beside, List<Stroke> horizontal)
    {
        List<VectorStaff> nearest = [.. beside.OrderBy(staff => Distance(staff, head.Y)).Take(2)];
        if (nearest.Count == 0 || Distance(nearest[0], head.Y) >= 8 * nearest[0].Space)
        {
            return null;
        }

        if (nearest.Count == 1 || Distance(nearest[0], head.Y) <= nearest[0].Space)
        {
            return nearest[0];
        }

        return nearest.FirstOrDefault(staff => LedgerFrom(staff, head, horizontal)) ?? nearest[0];
    }

    /// <summary>Whether a ledger line a space beyond the staff's edge, toward the notehead, stands at the notehead's x.</summary>
    private static bool LedgerFrom(VectorStaff staff, PlacedGlyph head, List<Stroke> horizontal)
    {
        double space = staff.Space;
        double y = head.Y < staff.Top ? staff.Top - space : staff.Bottom + space;
        return horizontal.Any(line => Math.Abs(line.Y0 - y) < 0.2 * space && line.X1 - line.X0 < 4 * space
            && line.X0 < head.Right && line.X1 > head.X);
    }

    /// <summary>The staff of the notehead an accidental stands before or a dot after, at its height.</summary>
    private static VectorStaff? HeadBeside(PlacedGlyph glyph, Dictionary<PlacedGlyph, VectorStaff> heads, double space)
    {
        bool before = glyph.Glyph.IsAccidental();
        return heads.Where(pair => Math.Abs(pair.Key.Y - glyph.Y) < 0.6 * space
                && (before ? pair.Key.X - glyph.Right : glyph.X - pair.Key.Right) is > -0.3 and var gap && gap < 5 * space)
            .OrderBy(pair => Math.Abs(pair.Key.X - glyph.X)).Select(pair => pair.Value).FirstOrDefault();
    }

    private static double Distance(VectorStaff staff, double y) => y < staff.Top ? staff.Top - y : y > staff.Bottom ? y - staff.Bottom : 0;
}