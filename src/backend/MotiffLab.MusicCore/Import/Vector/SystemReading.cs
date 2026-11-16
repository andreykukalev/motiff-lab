namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>What carries from one system to the next: each staff's clef and key, and the meter.</summary>
internal sealed class ReadingState
{
    public List<VectorClef> Clefs { get; } = [];

    public List<int> Fifths { get; } = [];

    public VectorMeter? Meter { get; set; }

    /// <summary>The bars read so far in the piece.</summary>
    public int Bars { get; set; }

    /// <summary>How long the piece's first bar is when it is a pickup, short of its meter.</summary>
    public int? Pickup { get; set; }

    /// <summary>Each staff's octave line still running at the end of the last system, in octaves (0 for none).</summary>
    public List<int> Ottavas { get; } = [];

    public int OttavaOf(int staff) => staff < Ottavas.Count ? Ottavas[staff] : 0;

    public void SetOttava(int staff, int octaves)
    {
        while (Ottavas.Count <= staff)
        {
            Ottavas.Add(0);
        }

        Ottavas[staff] = octaves;
    }

    /// <summary>Each staff's notes tied on past the last system's end: their alterations by diatonic step.</summary>
    public Dictionary<int, Dictionary<int, int>> Ties { get; } = [];

    public Dictionary<int, int> TiesOf(int staff) => Ties.TryGetValue(staff, out Dictionary<int, int>? ties) ? ties : [];

    public void SetTies(int staff, Dictionary<int, int> ties) => Ties[staff] = ties;

    public VectorClef ClefOf(int staff) => staff < Clefs.Count ? Clefs[staff] : staff == 0 ? VectorClef.Treble : VectorClef.Bass;

    public int FifthsOf(int staff) => staff < Fifths.Count ? Fifths[staff] : Fifths.Count > 0 ? Fifths[0] : 0;

    public void Set(int staff, VectorClef clef, int fifths)
    {
        while (Clefs.Count <= staff)
        {
            Clefs.Add(ClefOf(Clefs.Count));
            Fifths.Add(FifthsOf(Fifths.Count));
        }

        Clefs[staff] = clef;
        Fifths[staff] = fifths;
    }
}

/// <summary>
/// Reads one system into bars: the bar lines cut it, each staff's glyphs become notes and rests, and each note's pitch is
/// spelled with the clef in force, the key signature, and the accidentals printed before it in the bar (an accidental holds
/// to the bar line, for its step and octave).
/// </summary>
internal sealed class SystemReading(VectorPage page, VectorSystem system, ReadingState state, double normalSize, IReadOnlyList<VectorStaff> pageStaves)
{
    private readonly double _space = system.Space;

    public List<string> Issues { get; } = [];

    public List<VectorBar> Read(IReadOnlyList<List<PlacedGlyph>> glyphsByStaff)
    {
        List<double> bounds = Bounds();
        List<VectorBar> bars = [.. Enumerable.Range(0, bounds.Count - 1)
            .Select(_ => new VectorBar { Staves = [.. system.Staves.Select(_ => new List<VectorEvent>())] })];
        List<Ottava>[] ottavas = Ottavas.Find(page, system, pageStaves, state);
        for (int staff = 0; staff < system.Staves.Count; staff++)
        {
            ReadStaff(staff, [.. glyphsByStaff[staff].OrderBy(glyph => glyph.X)], bounds, bars, ottavas[staff]);
        }

        Tuplets.Mark(page, system, [.. bars.SelectMany(bar => bar.Staves.SelectMany(events => events))]);

        // The narrow gap between the two lines of a repeat or a double bar line is no bar.
        return [.. bars.Where((bar, index) => bar.Meter is not null || bar.Staves.Any(events => events.Count > 0)
            || bounds[index + 1] - bounds[index] > 6 * _space)];
    }

    /// <summary>Where the bars start and end: the system's left edge, every bar line, and its right edge when no bar line closes it.</summary>
    private List<double> Bounds()
    {
        List<double> bounds = [system.Left, .. system.BarLines.Where(x => x > system.Left + _space)];
        if (system.Right - bounds[^1] > 4 * _space)
        {
            bounds.Add(system.Right);
        }

        return bounds;
    }

    private void ReadStaff(int index, List<PlacedGlyph> glyphs, List<double> bounds, List<VectorBar> bars, List<Ottava> ottavas)
    {
        VectorStaff staff = system.Staves[index];
        List<PlacedGlyph> heads = [.. glyphs.Where(glyph => glyph.Glyph.IsNotehead())];
        var chords = new Chords(page, staff, index, system.BarLines, normalSize);
        List<VectorEvent> events = chords.Read(glyphs);
        Issues.AddRange(chords.Issues.Select(issue => $"staff {index + 1}: {issue}"));

        List<(double X, VectorClef Clef)> clefs = [.. glyphs.Where(glyph => glyph.Glyph.IsClef()).Select(glyph => (glyph.X, VectorClef.Of(glyph.Glyph, staff, glyph.Y)))];
        VectorClef before = state.ClefOf(index);
        VectorClef ClefAt(double x) => clefs.FindLastIndex(clef => clef.X <= x) is int at and >= 0 ? clefs[at].Clef : before;

        List<PlacedGlyph> accidentals = [.. glyphs.Where(glyph => glyph.Glyph.IsAccidental())];
        var keys = new List<(int Bar, int Fifths)>();
        var used = new HashSet<PlacedGlyph>();
        for (int bar = 0; bar < bars.Count; bar++)
        {
            double start = bar == 0 ? StartOfSystem(glyphs, clefs, events) : bounds[bar];
            double firstEvent = events.Where(item => item.X >= bounds[bar]).Select(item => item.X).DefaultIfEmpty(bounds[bar + 1]).First();
            // Inside a system a lone accidental after a bar line is a note's (in a chord, set apart): a key change has two or more.
            if (Signatures.Key(accidentals.Where(glyph => glyph.X < firstEvent), heads, staff, ClefAt(start), start) is { } key && (bar == 0 || key.Glyphs.Count > 1))
            {
                keys.Add((bar, key.Fifths));
                used.UnionWith(key.Glyphs);
            }
            else if (bar == 0)
            {
                keys.Add((0, 0));
            }
        }

        Dictionary<PlacedGlyph, MusicGlyph> written = Attach(accidentals.Where(glyph => !used.Contains(glyph)), heads, staff, bounds[^1], index);
        PlaceMeters(Signatures.Meters(glyphs, staff, normalSize), bounds, bars);
        PlaceClefs(index, clefs, bounds, bars);
        int Octaves(double x) => ottavas.Where(ottava => x >= ottava.From - (0.5 * _space) && x <= ottava.To + (0.5 * _space)).Sum(ottava => ottava.Octaves);
        TieMap ties = Ties.Find(page.Curves, staff, heads, heads.Count > 0 ? heads.Min(head => head.X) : system.Right, bounds[^1]);
        Spell(index, events, (bounds, bars), keys, new Spelling(staff, written, ties, (ClefAt, Octaves)) { FromBefore = state.TiesOf(index) });
        state.Set(index, clefs.Count > 0 ? clefs[^1].Clef : state.ClefOf(index), keys[^1].Fifths);
    }

    /// <summary>Where a system's key signature may start: after the clef that opens it, or at its left edge.</summary>
    private double StartOfSystem(List<PlacedGlyph> glyphs, List<(double X, VectorClef Clef)> clefs, List<VectorEvent> events)
    {
        double firstEvent = events.Count > 0 ? events[0].X : system.Right;
        PlacedGlyph? opening = glyphs.FirstOrDefault(glyph => glyph.Glyph.IsClef() && glyph.X < firstEvent);
        return opening?.Right ?? (clefs.Count > 0 && clefs[0].X < firstEvent ? clefs[0].X : system.Left);
    }

    /// <summary>The accidental written before each notehead: the nearest one to its left at its height. One that fits no head is reported.</summary>
    private Dictionary<PlacedGlyph, MusicGlyph> Attach(IEnumerable<PlacedGlyph> accidentals, List<PlacedGlyph> heads, VectorStaff staff, double end, int index)
    {
        var written = new Dictionary<PlacedGlyph, MusicGlyph>();
        foreach (PlacedGlyph accidental in accidentals.OrderByDescending(glyph => glyph.X))
        {
            PlacedGlyph? head = heads.Where(head => !written.ContainsKey(head) && Math.Abs(head.Y - accidental.Y) < 0.3 * staff.Space
                    && head.X - accidental.Right > -0.3 * staff.Space && head.X - accidental.Right < 5 * staff.Space)
                .OrderBy(head => head.X - accidental.Right).FirstOrDefault();
            if (head is not null)
            {
                written[head] = accidental.Glyph;
            }
            else if (accidental.X < end)
            {
                Issues.Add($"staff {index + 1}: an accidental at x {accidental.X:F0} is before no notehead");
            }
        }

        return written;
    }

    private void PlaceMeters(List<(double X, VectorMeter Meter)> meters, List<double> bounds, List<VectorBar> bars)
    {
        foreach ((double x, VectorMeter meter) in meters)
        {
            // A meter at the system's end, after its last bar line, only warns of the next system's.
            int bar = bounds.FindLastIndex(bound => bound <= x + _space);
            if (bar >= 0 && bar < bars.Count)
            {
                bars[bar].Meter ??= meter;
            }
        }
    }

    private void PlaceClefs(int index, List<(double X, VectorClef Clef)> clefs, List<double> bounds, List<VectorBar> bars)
    {
        VectorClef current = state.ClefOf(index);
        foreach ((double x, VectorClef clef) in clefs)
        {
            int bar = bounds.FindLastIndex(bound => bound <= x);
            if (clef != current && bar >= 0 && bar < bars.Count)
            {
                bars[bar].Clefs.Add(new ClefChange(index, x, clef));
            }

            current = clef;
        }
    }

    private void Spell(int index, List<VectorEvent> events, (List<double> Bounds, List<VectorBar> Bars) cut, List<(int Bar, int Fifths)> keys, Spelling spelling)
    {
        (List<double> bounds, List<VectorBar> bars) = cut;
        for (int bar = 0; bar < bars.Count; bar++)
        {
            int fifths = keys.Last(key => key.Bar <= bar).Fifths;
            if (keys.Any(key => key.Bar == bar) && index == 0)
            {
                bars[bar].Fifths = fifths;
            }

            List<VectorEvent> inBar = [.. events.Where(item => item.X >= bounds[bar] && item.X < bounds[bar + 1])];
            spelling.Bar(inBar, fifths);
            bars[bar].Staves[index].AddRange(inBar);
        }

        state.SetTies(index, spelling.ToNext);
    }
}