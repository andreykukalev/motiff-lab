using System.Globalization;
using System.Xml.Linq;

namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>
/// What the page check reads from a reading's MusicXML (ADR 0035, section 5, steps 3 and 4): each part's measures walked
/// in order, the clefs (by staff) and the key changing where they are written, for the staff positions of each staff's
/// heads under the reading's own clefs and the clef and key each bar starts with.
/// </summary>
public static partial class ScanMerge
{
    internal sealed partial class Reading
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

            /// <summary>Sets the clef a <c>clef</c> element writes in force on its staff (the first without a number): G, F or C on its line.</summary>
            private static void SetClef(XElement clef, Dictionary<int, int> clefs)
            {
                if (((string?)clef.Element("sign"))?.Trim() is { Length: 1 } sign && sign[0] is ('G' or 'F' or 'C'))
                {
                    clefs[Number(clef.Attribute("number")?.Value) ?? 1] = StaffPitch.Middle(sign[0], Number((string?)clef.Element("line")) ?? 2, Number((string?)clef.Element("clef-octave-change")) ?? 0);
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

                positions.Add(StaffPitch.Diatonic(step[0], octave) - middle);
            }

            private static int? Number(string? text) =>
                int.TryParse(text?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : null;
        }
    }
}