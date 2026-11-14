namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>
/// The notes, chords and rests of one staff: noteheads joined by the stem they share, their length from the notehead, the
/// beams across the stem or the flag at its tip, and the dots after them.
/// </summary>
internal sealed class Chords(VectorPage page, VectorStaff staff, int index, IReadOnlyList<double> barLines, double normalSize)
{
    private readonly double _space = staff.Space;

    /// <summary>What could not be placed, in words for the page report.</summary>
    public List<string> Issues { get; } = [];

    /// <summary>The events of the staff, left to right; spelling and timing come later.</summary>
    public List<VectorEvent> Read(IReadOnlyList<PlacedGlyph> glyphs)
    {
        List<PlacedGlyph> dots = [.. glyphs.Where(glyph => glyph.Glyph == MusicGlyph.AugmentationDot
            && !barLines.Any(x => Math.Abs(glyph.X - x) < 1.2 * _space))];
        // A flag hangs at its stem's tip, which may stand nearer the next staff: flags are looked for on the whole page.
        List<PlacedGlyph> flags = [.. page.Glyphs.Where(glyph => glyph.Glyph.IsFlag())];
        var events = new List<VectorEvent>();
        var stems = new Dictionary<Stroke, List<PlacedGlyph>>();
        foreach (PlacedGlyph head in glyphs.Where(glyph => glyph.Glyph.IsNotehead()))
        {
            if (head.Glyph is MusicGlyph.NoteheadWhole or MusicGlyph.NoteheadDoubleWhole)
            {
                events.Add(Note(head.Glyph, [head], null, 0));
            }
            else if (StemOf(head) is { } stem)
            {
                stems.TryAdd(stem, []);
                stems[stem].Add(head);
            }
            else
            {
                Issues.Add($"a notehead at x {head.X:F0} has no stem");
            }
        }

        foreach ((Stroke stem, List<PlacedGlyph> heads) in stems)
        {
            events.Add(Stemmed(stem, heads, flags));
        }

        foreach (PlacedGlyph rest in glyphs.Where(glyph => glyph.Glyph.IsRest()))
        {
            events.Add(new VectorEvent
            {
                X = rest.X,
                Anchor = rest.X,
                Staff = index,
                Rest = true,
                Glyph = rest.Glyph,
                Right = rest.Right,
                RestY = rest.Y,
                WholeBar = rest.Glyph == MusicGlyph.RestWhole,
            });
        }

        AssignDots(events, dots);
        return [.. events.OrderBy(item => item.X)];
    }

    private VectorEvent Stemmed(Stroke stem, List<PlacedGlyph> heads, List<PlacedGlyph> flags)
    {
        double lowest = heads.Max(head => head.Y), highest = heads.Min(head => head.Y);
        bool up = Math.Abs(stem.Y1 - lowest) < Math.Abs(stem.Y0 - highest);
        double tip = up ? stem.Y0 : stem.Y1;
        (int beams, int beam) = Beams(stem.X0, tip, up);
        foreach (PlacedGlyph flag in flags.Where(flag => Math.Abs(flag.X - stem.X0) < 0.5 * _space && Math.Abs(flag.Y - tip) < 1.2 * _space))
        {
            beams = Math.Max(beams, flag.Glyph.FlagCount());
        }

        VectorEvent note = Note(heads[0].Glyph, heads, up, beams);
        double width = heads.Max(head => head.Right - head.X);
        return new VectorEvent
        {
            X = note.X,
            Anchor = up ? stem.X0 - width : stem.X0,
            Staff = note.Staff,
            Glyph = note.Glyph,
            Heads = note.Heads,
            StemUp = note.StemUp,
            Beams = note.Beams,
            Grace = note.Grace,
            Right = note.Right,
            StemX = stem.X0,
            Tip = tip,
            Beam = beam,
        };
    }

    private VectorEvent Note(MusicGlyph glyph, List<PlacedGlyph> heads, bool? up, int beams)
    {
        List<PlacedGlyph> ordered = [.. heads.OrderByDescending(head => head.Y)];
        return new VectorEvent
        {
            X = heads.Min(head => head.X),
            Anchor = heads.Min(head => head.X),
            Staff = index,
            Glyph = glyph,
            Heads = ordered,
            StemUp = up,
            Beams = beams,
            Grace = heads.Max(head => head.Size) < 0.85 * normalSize,
            Right = heads.Max(head => head.Right),
        };
    }

    /// <summary>The stem a notehead hangs on: an upright line at its left or right edge that reaches it.</summary>
    private Stroke? StemOf(PlacedGlyph head)
    {
        double reach = 0.35 * _space;
        Stroke? best = null;
        double distance = double.MaxValue;
        foreach (Stroke line in page.Vertical)
        {
            double gap = Math.Min(Math.Abs(line.X0 - head.X), Math.Abs(line.X0 - head.Right));
            if (gap < reach && gap < distance && line.Y1 - line.Y0 > 1.5 * _space
                && head.Y >= line.Y0 - (0.6 * _space) && head.Y <= line.Y1 + (0.6 * _space)
                && !barLines.Any(x => Math.Abs(x - line.X0) < 0.3 * _space && line.Y1 - line.Y0 > 3.5 * _space))
            {
                best = line;
                distance = gap;
            }
        }

        return best;
    }

    /// <summary>The beams across a stem near its tip: filled shapes a beam thick, the first at the tip, the rest toward the heads.</summary>
    /// <returns>How many, and the one at the tip (its place in the page's shapes, -1 for none).</returns>
    private (int Count, int Outer) Beams(double x, double tip, bool up)
    {
        int count = 0, outer = -1;
        double nearest = double.MaxValue;
        for (int index = 0; index < page.Quads.Count; index++)
        {
            Quad beam = page.Quads[index];
            if (x < beam.Left - 0.6 || x > beam.Right + 0.6 || beam.Thickness < 0.25 * _space || beam.Thickness > 0.8 * _space)
            {
                continue;
            }

            double along = (beam.MiddleAt(x) - tip) * (up ? 1 : -1);
            if (along > -0.3 * _space && along < 3.2 * _space)
            {
                count++;
                if (along < nearest)
                {
                    nearest = along;
                    outer = index;
                }
            }
        }

        return (Math.Min(count, 4), outer);
    }

    /// <summary>
    /// Gives each augmentation dot to the note or rest it follows: the first dot stands close after its right edge, at a
    /// head's height or half a space above it; each next dot close after the one before. A dot between a grace note and the
    /// main note it leads to belongs to the main note.
    /// </summary>
    private void AssignDots(List<VectorEvent> events, List<PlacedGlyph> dots)
    {
        // The dots of a chord stand in columns, one dot a head; each column is one dot of length.
        var columns = new Dictionary<VectorEvent, List<double>>();
        foreach (PlacedGlyph dot in dots.OrderBy(dot => dot.X))
        {
            VectorEvent? owner = columns.FirstOrDefault(pair => pair.Value.Any(x => dot.X - x > 0.3 * _space && dot.X - x < 1.6 * _space)
                    && AtHeight(pair.Key, dot.Y, 1.0)).Key
                ?? events.Where(item => dot.X - item.Right > -0.1 * _space && dot.X - item.Right < 2.6 * _space && AtHeight(item, dot.Y, 0.6))
                    .OrderBy(item => item.Grace ? 1 : 0).ThenBy(item => dot.X - item.Right).FirstOrDefault();
            if (owner is null)
            {
                continue;
            }

            columns.TryAdd(owner, []);
            if (!columns[owner].Any(x => Math.Abs(x - dot.X) < 0.3 * _space))
            {
                columns[owner].Add(dot.X);
            }
        }

        foreach ((VectorEvent item, List<double> xs) in columns)
        {
            item.Dots = xs.Count;
        }
    }

    private bool AtHeight(VectorEvent item, double y, double within) =>
        item.Rest ? Math.Abs(item.RestY - y) < 1.0 * _space : item.Heads.Any(head => Math.Abs(head.Y - y) < within * _space);
}