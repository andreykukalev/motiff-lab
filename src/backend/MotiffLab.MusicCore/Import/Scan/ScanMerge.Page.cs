using System.Globalization;
using System.Xml.Linq;

namespace MotiffLab.MusicCore.Import.Scan;

public static partial class ScanMerge
{
    /// <summary>
    /// The readings of a staff no two engines agree on (or two pairs do), settled by the page (ADR 0035, section 5, steps 3
    /// and 4; research report, section 9): the group that agrees with music-core's own assembly of the staff from the
    /// detected symbols; else, among those that add up (all when none does), the one whose heads are fewest apart from those
    /// found on the page; without the page's bar, the first that adds up, then the first. Ties keep the merge's order.
    /// </summary>
    private static List<(Reading Reading, int Measure)> Settled(List<List<(Reading Reading, int Measure)>> best, int measure, int part, int staff, Page? page)
    {
        if (page?.Assembled(measure, part, staff) is { } assembled
            && best.FirstOrDefault(group => string.Equals(group[0].Reading.Content(group[0].Measure, part, staff), assembled, StringComparison.Ordinal)) is { } agreed)
        {
            return agreed;
        }

        List<List<(Reading Reading, int Measure)>> adding = [.. best.Where(group => group.Any(candidate => candidate.Reading.AddsUp(candidate.Measure, part, staff)))];
        List<List<(Reading Reading, int Measure)>> pool = adding.Count > 0 ? adding : best;
        return page is not null && page.HasBar(measure)
            ? pool.MinBy(group => page.Apart(group[0].Reading, group[0].Measure, measure, part, staff))!
            : pool[0];
    }

    /// <summary>
    /// The page's evidence as the merge reads it: its bars paired with the backbone's (in order when as many, else by
    /// alignment on the heads of each staff), and the page's staves as the backbone's (part, staff), top down.
    /// </summary>
    private sealed class Page
    {
        private readonly ScanEvidence _evidence;
        private readonly Reading _backbone;
        private readonly List<(int Part, int Staff)> _staves;
        private readonly int?[] _bars;

        private Page(ScanEvidence evidence, Reading backbone)
        {
            (_evidence, _backbone) = (evidence, backbone);
            _staves = [.. backbone.Staves()];
            Dictionary<string, int>[] written = [.. Enumerable.Range(0, backbone.Measures).Select(measure => Keys(_staves.Select((staff, index) => (index + 1, backbone.Positions(measure, staff.Part, staff.Staff)))))];
            Dictionary<string, int>[] found = [.. evidence.Bars.Select(bar => Keys(_staves.Select((_, index) => (index + 1, Found(bar, index + 1)))))];
            _bars = written.Length == found.Length
                ? [.. Enumerable.Range(0, written.Length).Select(bar => (int?)bar)]
                : Align(written.Length, found.Length, (i, j) => Likeness(written[i], found[j]));
        }

        public static Page? Of(ScanEvidence? evidence, Reading backbone) => evidence is { Bars.Count: > 0 } ? new Page(evidence, backbone) : null;

        /// <summary>Whether the backbone's bar was found on the page.</summary>
        public bool HasBar(int measure) => _bars[measure] is not null;

        /// <summary>
        /// The staff of the bar as music-core assembles it from the page's symbols (<see cref="ScanAssembly"/>), as the merge
        /// compares notes; null where the bar is not on the page or the assembly does not fill it. The clef and key it starts
        /// from are the backbone's as the bar starts; the first bar of the page may be as short as the backbone reads it.
        /// </summary>
        public string? Assembled(int measure, int part, int staff)
        {
            if (_bars[measure] is not int bar)
            {
                return null;
            }

            long meter = _backbone.BarTicks(measure);
            long[] lengths = measure == 0 ? [meter, _backbone.Import.Measures[0].Span.Duration] : [meter];
            return ScanAssembly.Staff(_evidence.Bars[bar], StaffOnPage(part, staff), _backbone.KeyAt(measure, part, staff), lengths) is { } notes
                ? ContentOf(notes)
                : null;
        }

        /// <summary>How many staff positions the reading's heads on the staff have that the page's do not, and the page's that the reading's do not.</summary>
        public int Apart(Reading reading, int readingMeasure, int measure, int part, int staff)
        {
            IReadOnlySet<int> found = Found(_evidence.Bars[_bars[measure]!.Value], StaffOnPage(part, staff));
            IReadOnlySet<int> written = reading.Positions(readingMeasure, part, staff);
            return written.Count(position => !found.Contains(position)) + found.Count(position => !written.Contains(position));
        }

        /// <summary>The staff positions of the heads found on one staff of a page's bar (grace notes left out).</summary>
        private static IReadOnlySet<int> Found(ScanEvidenceBar bar, int staff) =>
            bar.Symbols.Where(symbol => symbol.Staff == staff && symbol.IsHead).Select(symbol => symbol.Position!.Value).ToHashSet();

        /// <summary>Staff positions as the alignment counts them: one of each staff and position.</summary>
        private static Dictionary<string, int> Keys(IEnumerable<(int Staff, IReadOnlySet<int> Positions)> staves) =>
            staves.SelectMany(staff => staff.Positions.Select(position => string.Create(CultureInfo.InvariantCulture, $"{staff.Staff}:{position}")))
                .ToDictionary(key => key, _ => 1, StringComparer.Ordinal);

        private int StaffOnPage(int part, int staff) => _staves.IndexOf((part, staff)) + 1;
    }

    private sealed partial class Reading
    {
        private static readonly HashSet<int> s_noHeads = [];

        private Staffing? _staffing;

        /// <summary>The staff positions of the heads on one staff of a bar under the reading's own clefs (a unison once; grace notes and cue notes left out).</summary>
        public IReadOnlySet<int> Positions(int measure, int part, int staff) =>
            Staffs().Heads.TryGetValue((measure, part, staff), out HashSet<int>? positions) ? positions : s_noHeads;

        /// <summary>The clef and key in force on a staff as the bar starts, after the attributes before its first note.</summary>
        public StaffKey KeyAt(int measure, int part, int staff) =>
            Staffs().Keys.TryGetValue((measure, part, staff), out StaffKey key) ? key : Default(staff);

        private static StaffKey Default(int staff) => staff == 2 ? StaffKey.Bass : StaffKey.Treble;

        private Staffing Staffs() => _staffing ??= ReadStaffing();

        /// <summary>Each part's measures walked in order, the clefs (by staff) and the key changing where they are written.</summary>
        private Staffing ReadStaffing()
        {
            var staffing = new Staffing();
            ILookup<int, int> staves = Staves().ToLookup(staff => staff.Part, staff => staff.Staff);
            for (int part = 0; part < Parts.Count; part++)
            {
                var clefs = new Dictionary<int, int>();
                int fifths = 0;
                for (int measure = 0; measure < Parts[part].Count; measure++)
                {
                    bool started = false;
                    foreach (XElement element in Parts[part][measure].Measure.Elements())
                    {
                        if (!started && IsMusic(element))
                        {
                            staffing.Start(measure, part, staves[part], clefs, fifths);
                            started = true;
                        }

                        fifths = staffing.Read(element, measure, part, clefs, fifths);
                    }

                    if (!started)
                    {
                        staffing.Start(measure, part, staves[part], clefs, fifths);
                    }
                }
            }

            return staffing;
        }

        /// <summary>The staff positions of each staff's heads and the clef and key each bar starts with, by (measure, part, staff).</summary>
        private sealed class Staffing
        {
            public Dictionary<(int Measure, int Part, int Staff), HashSet<int>> Heads { get; } = [];

            public Dictionary<(int Measure, int Part, int Staff), StaffKey> Keys { get; } = [];

            public void Start(int measure, int part, IEnumerable<int> staves, Dictionary<int, int> clefs, int fifths)
            {
                foreach (int staff in staves)
                {
                    Keys[(measure, part, staff)] = new StaffKey(clefs.TryGetValue(staff, out int middle) ? middle : Default(staff).Middle, fifths);
                }
            }

            /// <summary>Reads one element of a measure: a key or clef sets what is in force; a pitched note adds its head's staff position.</summary>
            /// <returns>The key's fifths in force after it.</returns>
            public int Read(XElement element, int measure, int part, Dictionary<int, int> clefs, int fifths)
            {
                switch (element.Name.LocalName)
                {
                    case "attributes":
                        foreach (XElement clef in element.Elements("clef"))
                        {
                            SetClef(clef, clefs);
                        }

                        return Number((string?)element.Element("key")?.Element("fifths")) ?? fifths;
                    case "note":
                        AddHead(element, measure, part, clefs);
                        return fifths;
                    default:
                        return fifths;
                }
            }

            private static void SetClef(XElement clef, Dictionary<int, int> clefs)
            {
                if (((string?)clef.Element("sign"))?.Trim() is { Length: 1 } sign
                    && StaffPositions.Middle(sign[0], Number((string?)clef.Element("line")) ?? 2, Number((string?)clef.Element("clef-octave-change")) ?? 0) is int middle)
                {
                    clefs[Number(clef.Attribute("number")?.Value) ?? 1] = middle;
                }
            }

            /// <summary>Adds a pitched note's head (not a grace or cue note) at its staff position under the clef in force on its staff.</summary>
            private void AddHead(XElement note, int measure, int part, Dictionary<int, int> clefs)
            {
                if (note.Element("pitch") is not { } pitch || note.Element("grace") is not null || note.Element("cue") is not null
                    || ((string?)pitch.Element("step"))?.Trim() is not { Length: 1 } step || Number((string?)pitch.Element("octave")) is not int octave)
                {
                    return;
                }

                int staff = Number((string?)note.Element("staff")) ?? 1;
                int middle = clefs.TryGetValue(staff, out int written) ? written : Default(staff).Middle;
                if (!Heads.TryGetValue((measure, part, staff), out HashSet<int>? positions))
                {
                    Heads[(measure, part, staff)] = positions = [];
                }

                positions.Add(StaffPositions.Diatonic(step[0], octave) - middle);
            }

            private static int? Number(string? text) =>
                int.TryParse(text?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : null;
        }
    }
}