namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>
/// A page as it was read before (its MusicXML, as <see cref="ScanJoin.Split"/> cuts it from the joined score) and its
/// measures, by index from 0, the player marked as read badly («Как напечатано», step 3), for <see cref="ScanMerge.Merge"/>.
/// </summary>
internal sealed record ScanBefore(byte[] MusicXml, IReadOnlySet<int> Bad);

internal static partial class ScanMerge
{
    /// <summary>
    /// The page as read before, as the merge reads it (c-14b2): its bars paired with the backbone's by alignment on their
    /// notes, as another engine's are, and the backbone's bars paired with one the player marked as read badly.
    /// </summary>
    private sealed class Before
    {
        private readonly Reading _reading;
        private readonly Reading _backbone;
        private readonly Dictionary<int, int> _bad;

        private Before(Reading reading, Reading backbone, Dictionary<int, int> bad) => (_reading, _backbone, _bad) = (reading, backbone, bad);

        /// <summary>The page as read before beside the backbone; null without one, without a bar marked, or when it is no MusicXML of the backbone's parts.</summary>
        public static Before? Of(ScanBefore? before, Reading backbone)
        {
            if (before is not { Bad.Count: > 0 } || Reading.Of(new ScanReading("before", before.MusicXml), EngineOrder.Count) is not { } reading
                || reading.Import.Parts.Count != backbone.Import.Parts.Count)
            {
                return null;
            }

            int?[] pairs = Align(backbone, reading);
            Dictionary<int, int> bad = [];
            for (int measure = 0; measure < pairs.Length; measure++)
            {
                if (pairs[measure] is int earlier && before.Bad.Contains(earlier))
                {
                    bad[measure] = earlier;
                }
            }

            return new Before(reading, backbone, bad);
        }

        /// <summary>
        /// For a backbone bar paired with one marked as read badly, another reading than the one taken before: of the
        /// candidates whose notes on the bar's staves are not those read before, the group most of them read alike, the first
        /// in the merge's order among as many, every staff taken from it (from the backbone when it is in the group); null for
        /// another bar, or when every engine read the bar as before.
        /// </summary>
        public List<Choice>? Another(List<(Reading Reading, int Measure)> candidates, int measure)
        {
            if (!_bad.TryGetValue(measure, out int earlier))
            {
                return null;
            }

            (int Part, int Staff)[] staves = [.. _backbone.Staves()];
            string Key(Reading reading, int at) => string.Join("|", staves.Select(staff => reading.Content(at, staff.Part, staff.Staff)));
            string taken = Key(_reading, earlier);
            if (candidates.GroupBy(candidate => Key(candidate.Reading, candidate.Measure), StringComparer.Ordinal)
                .Where(group => !string.Equals(group.Key, taken, StringComparison.Ordinal))
                .OrderByDescending(static group => group.Count())
                .FirstOrDefault() is not { } other)
            {
                return null;
            }

            (Reading source, int at) = other.FirstOrDefault(candidate => candidate.Reading == _backbone, other.First());
            Reading[] agreeing = [.. other.Select(static candidate => candidate.Reading)];
            return [.. staves.Select(staff => new Choice(measure, staff.Part, staff.Staff, source, at, agreeing))];
        }
    }
}