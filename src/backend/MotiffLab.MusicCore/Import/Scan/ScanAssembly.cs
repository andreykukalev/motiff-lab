using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>
/// music-core's own assembly of one staff of a bar from the symbols the detector found on the page (ADR 0035, section 5,
/// step 3; research report, section 9), without any engine's reading. The merge asks it as a referee when no two engines
/// agree on a staff, and takes the reading it agrees with; the assembly itself is never written in place of a reading,
/// since as a replacement it was worse than the engines. Nothing here looks at another bar.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Heads (a staff position and a value from the head with its flags or beams), augmentation dots, accidentals, rests
/// and triplet marks are taken when the detector was at least <see cref="Sure"/> sure; a head found twice at one place is
/// taken once, the surer.</item>
/// <item>Columns: heads at one place left to right; a second's heads sit side by side, so a head of the same value a step
/// from one of the column joins it.</item>
/// <item>Time: a column starts when the earliest note still sounding ends, so no voices have to be told apart.</item>
/// <item>Pitch: the staff position under the clef in force (the last clef drawn on the staff left of the head, else the
/// one the bar starts with) and the key (the same way), with an accidental drawn left of the head, which holds for that
/// step until the bar line.</item>
/// <item>A triplet mark takes the three neighbouring columns of one value nearest to it.</item>
/// </list>
/// A whole-bar rest (the evidence's <c>measure</c>) lasts the bar, a whole rest four quarters. The staff is assembled only
/// where its notes and rests fill the bar exactly. Time is exact (<see cref="QuarterFraction"/>);
/// the page's coordinates only order the symbols and tell which belong together.
/// </remarks>
internal static class ScanAssembly
{
    /// <summary>The least confidence of a symbol the assembly takes.</summary>
    public const double Sure = 0.5;

    private static readonly Dictionary<string, QuarterFraction> s_rests = new(StringComparer.Ordinal)
    {
        ["restDoubleWhole"] = QuarterFraction.FromQuarters(8),
        ["restWhole"] = QuarterFraction.FromQuarters(4),
        ["restHalf"] = QuarterFraction.FromQuarters(2),
        ["restQuarter"] = QuarterFraction.FromQuarters(1),
        ["rest8th"] = new(1, 2),
        ["rest16th"] = new(1, 4),
        ["rest32nd"] = new(1, 8),
        ["rest64th"] = new(1, 16),
        ["rest128th"] = new(1, 32),
    };

    private static readonly Dictionary<string, int> s_accidentals = new(StringComparer.Ordinal)
    {
        ["accidentalSharp"] = 1,
        ["accidentalFlat"] = -1,
        ["accidentalNatural"] = 0,
        ["accidentalDoubleSharp"] = 2,
        ["accidentalDoubleFlat"] = -2,
    };

    /// <summary>
    /// The notes of one staff of a page's bar as assembled, (onset in the bar, MIDI pitch, length) in ticks; null when the
    /// staff has nothing the assembly takes, or its notes and rests do not end together at one of <paramref name="lengths"/>
    /// (in ticks; the first is the meter's, which a whole-bar rest lasts).
    /// </summary>
    public static IReadOnlyList<(long Onset, int Pitch, long Length)>? Staff(ScanEvidenceBar bar, int staff, StaffKey start, IReadOnlyList<long> lengths)
    {
        if (!bar.Spaces.TryGetValue(staff, out double space) || space <= 0)
        {
            return null;
        }

        ScanSymbol[] own = [.. bar.Symbols.Where(symbol => symbol.Staff == staff && symbol.Confidence >= Sure).OrderBy(symbol => symbol.X).ThenBy(symbol => symbol.Y)];
        var length = new QuarterFraction(lengths[0], CanonicalTime.TicksPerQuarter);
        List<Item> items = [.. Heads(own, space).Select(head => Item.Head(head, own, space)), .. Rests(own, length)];
        if (items.Count == 0)
        {
            return null;
        }

        List<List<Item>> columns = Columns(items, space);
        MarkTriplets(columns, own, space);
        return Timed(columns, own, space, start, lengths);
    }

    /// <summary>The heads with a value, each place once: the surer of two found less than 0.4 of a space apart both ways.</summary>
    private static List<ScanSymbol> Heads(ScanSymbol[] own, double space)
    {
        var kept = new List<ScanSymbol>();
        foreach (ScanSymbol head in own.Where(symbol => symbol.IsHead && NoteValue.Named.ContainsKey(symbol.Duration ?? "")).OrderByDescending(symbol => symbol.Confidence))
        {
            if (!kept.Any(other => Math.Abs(other.X - head.X) < 0.4 * space && Math.Abs(other.Y - head.Y) < 0.4 * space))
            {
                kept.Add(head);
            }
        }

        return kept;
    }

    /// <summary>The rests: a whole-bar rest lasts the bar, a whole rest four quarters.</summary>
    private static IEnumerable<Item> Rests(ScanSymbol[] own, QuarterFraction bar) =>
        own.Where(symbol => s_rests.ContainsKey(symbol.Class))
            .Select(rest => new Item(rest, rest.Measure ? bar : s_rests[rest.Class]));

    /// <summary>The items in columns left to right (see the remarks).</summary>
    private static List<List<Item>> Columns(List<Item> items, double space)
    {
        var columns = new List<List<Item>>();
        foreach (Item item in items.OrderBy(item => item.Symbol.X).ThenBy(item => item.Symbol.Position ?? int.MinValue))
        {
            List<Item>? last = columns.Count > 0 ? columns[^1] : null;
            double apart = last is null ? double.MaxValue : Math.Abs(item.Symbol.X - last[^1].Symbol.X);
            bool second = item.Symbol.Position is int position && apart < 1.4 * space
                && last!.Any(other => other.Symbol.Position is int beside && Math.Abs(beside - position) == 1 && other.Value == item.Value);
            if (last is not null && (apart < 0.6 * space || second))
            {
                last.Add(item);
            }
            else
            {
                columns.Add([item]);
            }
        }

        return columns;
    }

    /// <summary>Each triplet mark takes the three neighbouring columns of one value, not in a triplet yet, nearest to it (within 3 spaces).</summary>
    private static void MarkTriplets(List<List<Item>> columns, ScanSymbol[] own, double space)
    {
        foreach (ScanSymbol mark in own.Where(symbol => symbol.Class == "tuplet3"))
        {
            (double Distance, int First)? best = null;
            for (int first = 0; first + 3 <= columns.Count; first++)
            {
                List<Item>[] three = [.. columns.Skip(first).Take(3)];
                double distance = Math.Abs((three.Sum(column => column[0].Symbol.X) / 3) - mark.X);
                bool alike = three.Select(column => column.Min(item => item.Value)).Distinct().Count() == 1 && !three.Any(column => column.Any(item => item.InTriplet));
                if (alike && distance < 3 * space && (best is null || distance < best.Value.Distance))
                {
                    best = (distance, first);
                }
            }

            foreach (Item item in best is { } taken ? columns.Skip(taken.First).Take(3).SelectMany(column => column) : [])
            {
                item.InTriplet = true;
            }
        }
    }

    /// <summary>The columns in time (see the remarks), in ticks; null when they do not fill the bar.</summary>
    private static IReadOnlyList<(long Onset, int Pitch, long Length)>? Timed(List<List<Item>> columns, ScanSymbol[] own, double space, StaffKey start, IReadOnlyList<long> lengths)
    {
        var notes = new List<(QuarterFraction Onset, int Pitch, QuarterFraction End)>();
        var altered = new Dictionary<int, int>();
        var sounding = new List<QuarterFraction>();
        QuarterFraction now = QuarterFraction.Zero, end = QuarterFraction.Zero;
        foreach (List<Item> column in columns)
        {
            now = sounding.Count > 0 ? sounding.Min() : now;
            foreach (Item item in column)
            {
                QuarterFraction over = now + item.Length;
                sounding.Add(over);
                end = over > end ? over : end;
                if (item.Symbol.Position is int position)
                {
                    notes.Add((now, PitchOf(item.Symbol, position, own, space, start, altered), over));
                }
            }

            sounding.RemoveAll(over => over <= now);
        }

        if (!lengths.Any(length => end == new QuarterFraction(length, CanonicalTime.TicksPerQuarter)))
        {
            return null;
        }

        return [.. notes.Select(note => (Ticks(note.Onset), note.Pitch, Ticks(note.End) - Ticks(note.Onset)))];
    }

    /// <summary>
    /// The pitch of a head: its staff position under the clef in force, with the key's alteration or an accidental drawn
    /// left of it (2.2 spaces at most, less than 0.7 of a space higher or lower), which <paramref name="altered"/> keeps
    /// for the step until the bar line.
    /// </summary>
    private static int PitchOf(ScanSymbol head, int position, ScanSymbol[] own, double space, StaffKey start, Dictionary<int, int> altered)
    {
        int middle = own.LastOrDefault(symbol => symbol.X < head.X && Middle(symbol) is not null) is { } clef ? Middle(clef)!.Value : start.Middle;
        int fifths = own.LastOrDefault(symbol => symbol.X < head.X && symbol.Class == "keySignature" && symbol.Fifths is not null)?.Fifths ?? start.Fifths;
        var key = new StaffKey(middle, fifths);
        int diatonic = middle + position;
        if (own.LastOrDefault(symbol => s_accidentals.ContainsKey(symbol.Class) && symbol.X < head.Left && symbol.X > head.Left - (2.2 * space)
            && Math.Abs(symbol.Y - head.Y) < 0.7 * space) is { } accidental)
        {
            altered[diatonic] = s_accidentals[accidental.Class];
        }

        return key.Pitch(position, altered.TryGetValue(diatonic, out int alter) ? alter : null);
    }

    /// <summary>The middle line under a drawn clef ("gClef", "fClef8vb", "cClef"… with its line); null for another symbol.</summary>
    private static int? Middle(ScanSymbol symbol)
    {
        string name = symbol.Class;
        if (name.Length < 5 || name[1..5] != "Clef" || name[0] is not ('g' or 'f' or 'c'))
        {
            return null;
        }

        int octaves = name[5..] switch
        {
            "8va" => 1,
            "8vb" => -1,
            "15ma" => 2,
            "15mb" => -2,
            _ => 0,
        };
        char sign = char.ToUpperInvariant(name[0]);
        return StaffPitch.Middle(sign, symbol.Line ?? (sign == 'F' ? 4 : sign == 'C' ? 3 : 2), octaves);
    }

    private static long Ticks(QuarterFraction position) => CanonicalTime.ToTick(position).Tick.Value;

    /// <summary>A head or rest taken: its symbol, its value with its dots, and whether a triplet mark took it.</summary>
    private sealed class Item(ScanSymbol symbol, QuarterFraction value)
    {
        public ScanSymbol Symbol { get; } = symbol;

        public QuarterFraction Value { get; } = value;

        public bool InTriplet { get; set; }

        /// <summary>How long it lasts: its value, two thirds of it in a triplet.</summary>
        public QuarterFraction Length => InTriplet ? new QuarterFraction(Value.Numerator * 2, Value.Denominator * 3) : Value;

        /// <summary>A head with its dots: those it says, else one when an augmentation dot sits just right of it.</summary>
        public static Item Head(ScanSymbol head, ScanSymbol[] own, double space)
        {
            int dots = head.Dots > 0 ? head.Dots
                : own.Any(dot => dot.Class == "augmentationDot" && dot.X > head.Right - (0.1 * space) && dot.X < head.Right + (1.3 * space)
                    && Math.Abs(dot.Y - head.Y) < 0.6 * space) ? 1 : 0;
            return new Item(head, NoteValue.Dotted(NoteValue.Named[head.Duration!], dots));
        }
    }
}