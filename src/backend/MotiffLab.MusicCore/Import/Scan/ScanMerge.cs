using System.Globalization;
using System.Text;
using System.Xml.Linq;

using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>One engine's reading of a page: the engine as the scan server names it ("homr", "audiveris 5.11"…) and its MusicXML.</summary>
public sealed record ScanReading(string Engine, byte[] MusicXml);

/// <summary>How sure a merged bar is, from how many engines agree on its staves (ADR 0035, section 5).</summary>
public enum ScanLevel
{
    /// <summary>Three or more engines agree on every staff.</summary>
    Sure,

    /// <summary>Two engines, at least, agree on every staff, and fewer than three on some.</summary>
    Probable,

    /// <summary>No two engines agree on some staff: the bar is worth checking.</summary>
    Check,
}

/// <summary>Which engines gave one staff of a merged bar: those whose reading of it was taken, in the merge's order.</summary>
/// <param name="Part">The part, from 1.</param>
/// <param name="Staff">The staff in the part, from 1.</param>
public sealed record ScanStaffSource(int Part, int Staff, IReadOnlyList<string> Engines);

/// <summary>One bar of a merged page: its level and the engines that gave each staff.</summary>
public sealed record ScanMergedBar(ScanLevel Level, IReadOnlyList<ScanStaffSource> Staves);

/// <summary>A page merged from several readings: its MusicXML, the engines that read it in the merge's order, and each bar in order.</summary>
public sealed record ScanMergedPage(byte[] MusicXml, IReadOnlyList<string> Engines, IReadOnlyList<ScanMergedBar> Bars);

/// <summary>
/// Merges several engines' readings of one scanned page into one, staff by staff (ADR 0035, section 5; c-1a). Each reading
/// is repaired first (<see cref="ScanRepair"/> with the rules for a reading). The bars of the readings are paired by
/// alignment on their notes, so an engine that lost or split a bar still pairs the rest. For each staff of each bar, the
/// reading two or more engines agree on is taken (the notes: onset in the bar, pitch and length; rests and grace notes are
/// not compared). Without one, or with two such readings, the page's evidence settles it when it is sent (c-1b): the
/// reading that agrees with music-core's own assembly of that staff from the detected symbols (<see cref="ScanAssembly"/>),
/// else, among those that add up, the one whose heads agree best with the heads found on the page; without evidence, or
/// where neither tells, the one that adds up, then the first engine in <see cref="EngineOrder"/>. The assembly only
/// referees: it is never written in place of a reading. No bar is ever taken from another bar of the piece.
/// </summary>
/// <remarks>
/// <para>
/// The bars are those of the backbone: the reading with as many bars as most readings have, the first engine among them.
/// A reading with other parts than the backbone's is not compared. The level of a bar is the fewest engines agreeing on
/// any of its staves: three or more sure, two probable, else check. One reading alone leaves every bar to check.
/// </para>
/// <para>
/// A staff taken from another reading than the backbone is written into the backbone's measure with its own notes, in
/// divisions both share; the measure keeps the backbone's attributes, directions and bar lines. A bar to check is marked
/// (<see cref="MusicXmlImport.EnginesDisagree"/>), so <see cref="ScanCheck"/> flags it wherever the score is read: the
/// Notes' Check flags (0030) with the reason EnginesDisagree. The merge is said in the score's encoding description, and the
/// notes stay detected. Pure and deterministic: the same readings give the same bytes.
/// </para>
/// </remarks>
public static partial class ScanMerge
{
    /// <summary>The engines in the order a tie is settled in; an engine not named here comes after them, in the order given.</summary>
    public static readonly IReadOnlyList<string> EngineOrder = ["homr", "copisteria", "audiveris", "transcoda"];

    /// <summary>The fewest engines agreeing on every staff of a sure bar.</summary>
    public const int SureEngines = 3;

    /// <summary>The fewest engines agreeing on a staff that is not to check.</summary>
    public const int AgreeingEngines = 2;

    /// <summary>The cost of a bar left unpaired when the readings are aligned; a pair costs one less its notes' likeness.</summary>
    private const double UnpairedBar = 0.6;

    /// <summary>
    /// The readings of one page merged staff by staff, with the page's evidence when the scan server gave one. A reading
    /// that is not partwise MusicXML with bars is left out, as an engine that did not read the page (spec 2а): the page
    /// merges on the rest.
    /// </summary>
    /// <exception cref="ArgumentException">No reading of the page is partwise MusicXML with bars.</exception>
    public static ScanMergedPage Merge(IReadOnlyList<ScanReading> readings, ScanEvidence? evidence = null)
    {
        ArgumentNullException.ThrowIfNull(readings);
        List<Reading> ordered = [.. readings.Select((reading, index) => Reading.Of(reading, index)).OfType<Reading>().OrderBy(reading => reading.Rank)];
        if (ordered.Count == 0)
        {
            throw new ArgumentException(readings.Count == 0 ? "The page has no reading." : "No engine's reading of the page is MusicXML with bars.", nameof(readings));
        }

        Reading backbone = Backbone(ordered);
        List<(Reading Reading, int?[] Pairs)> compared = [.. ordered
            .Where(reading => reading.Import.Parts.Count == backbone.Import.Parts.Count)
            .Select(reading => (reading, reading == backbone ? [.. Enumerable.Range(0, backbone.Measures).Select(i => (int?)i)] : Align(backbone, reading)))];

        Page? page = Page.Of(evidence, backbone);
        var bars = new List<ScanMergedBar>(backbone.Measures);
        var taken = new List<Choice>();
        for (int measure = 0; measure < backbone.Measures; measure++)
        {
            List<(Reading Reading, int Measure)> candidates = [.. compared
                .Where(pair => pair.Pairs[measure] is not null)
                .Select(pair => (pair.Reading, pair.Pairs[measure]!.Value))];
            var staves = new List<ScanStaffSource>();
            int fewest = int.MaxValue;
            foreach ((int part, int staff) in backbone.Staves())
            {
                Choice choice = Vote(candidates, measure, part, staff, backbone, page);
                taken.Add(choice);
                staves.Add(new ScanStaffSource(part + 1, staff, [.. choice.Agreeing.Select(reading => reading.Engine)]));
                fewest = Math.Min(fewest, choice.Agreeing.Count);
            }

            bars.Add(new ScanMergedBar(fewest >= SureEngines ? ScanLevel.Sure : fewest >= AgreeingEngines ? ScanLevel.Probable : ScanLevel.Check, staves));
        }

        XElement root = Written(backbone, taken, bars);
        ScanRepair.Say(root, $"Motiff Lab scan merge: each staff of each bar from the reading most of {string.Join(", ", ordered.Select(reading => reading.Engine))} agree on; "
            + (page is null ? "" : "without agreement, the reading the page's symbols agree with; ")
            + $"{bars.Count(bar => bar.Level == ScanLevel.Check)} of {bars.Count} bars to check.");
        byte[] bytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n" + root.ToString(SaveOptions.DisableFormatting));
        return new ScanMergedPage(bytes, [.. ordered.Select(reading => reading.Engine)], bars);
    }

    /// <summary>The place of an engine in <see cref="EngineOrder"/> by its name's first word, any case; others after, by the order given.</summary>
    private static int RankOf(string engine, int given)
    {
        string name = engine.Trim().Split(' ', 2)[0];
        int known = EngineOrder.ToList().FindIndex(order => string.Equals(order, name, StringComparison.OrdinalIgnoreCase));
        return known >= 0 ? known : EngineOrder.Count + given;
    }

    /// <summary>The reading whose number of bars most readings share, the first engine among them.</summary>
    private static Reading Backbone(List<Reading> ordered) =>
        ordered.GroupBy(reading => reading.Measures)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.First().Rank)
            .First()
            .First();

    /// <summary>
    /// The staff of the bar from the candidates (in the merge's order): the notes the most of them read, when two or more
    /// do and no other notes as many; else, among those read most often, the one the page settles (<see cref="Settled"/>).
    /// </summary>
    private static Choice Vote(List<(Reading Reading, int Measure)> candidates, int backboneMeasure, int part, int staff, Reading backbone, Page? page)
    {
        List<List<(Reading Reading, int Measure)>> groups = [.. candidates.GroupBy(candidate => candidate.Reading.Content(candidate.Measure, part, staff), StringComparer.Ordinal).Select(group => group.ToList())];
        int most = groups.Max(group => group.Count);
        List<List<(Reading Reading, int Measure)>> best = [.. groups.Where(group => group.Count == most)];
        List<(Reading Reading, int Measure)> chosen = best.Count == 1 && most >= AgreeingEngines
            ? best[0]
            : Settled(best, backboneMeasure, part, staff, page);
        (Reading Reading, int Measure)[] own = [.. chosen.Where(candidate => candidate.Reading == backbone)];
        (Reading Reading, int Measure) source = own.Length > 0 ? own[0] : chosen.FirstOrDefault(candidate => candidate.Reading.AddsUp(candidate.Measure, part, staff), chosen[0]);
        return new Choice(backboneMeasure, part, staff, source.Reading, source.Measure, [.. chosen.Select(candidate => candidate.Reading)]);
    }

    /// <summary>
    /// The bars of <paramref name="other"/> paired with the backbone's, by global alignment on the notes of each bar (part,
    /// staff, onset in the bar, pitch): null where a backbone bar has no pair.
    /// </summary>
    private static int?[] Align(Reading backbone, Reading other) =>
        Align(backbone.Measures, other.Measures, (i, j) => Likeness(backbone.Heads(i), other.Heads(j)));

    /// <summary>
    /// <paramref name="n"/> bars paired with <paramref name="m"/> others by global alignment: a pair costs one less the bars'
    /// <paramref name="likeness"/>, a bar left unpaired <see cref="UnpairedBar"/>; null where a bar of the first has no pair.
    /// </summary>
    private static int?[] Align(int n, int m, Func<int, int, double> likeness)
    {
        var cost = new double[n + 1, m + 1];
        var step = new int[n + 1, m + 1];
        for (int i = 1; i <= n; i++)
        {
            (cost[i, 0], step[i, 0]) = (i * UnpairedBar, 1);
        }

        for (int j = 1; j <= m; j++)
        {
            (cost[0, j], step[0, j]) = (j * UnpairedBar, 2);
        }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                (double Cost, int Step)[] ways =
                [
                    (cost[i - 1, j - 1] + 1 - likeness(i - 1, j - 1), 0),
                    (cost[i - 1, j] + UnpairedBar, 1),
                    (cost[i, j - 1] + UnpairedBar, 2),
                ];
                (cost[i, j], step[i, j]) = ways.MinBy(way => way.Cost);
            }
        }

        var pairs = new int?[n];
        for (int i = n, j = m; i > 0 && j > 0;)
        {
            switch (step[i, j])
            {
                case 0:
                    pairs[--i] = --j;
                    break;
                case 1:
                    i--;
                    break;
                default:
                    j--;
                    break;
            }
        }

        return pairs;
    }

    /// <summary>Notes as the merge compares them: (onset in the bar, pitch, length within the bar) in ticks, in order, as text.</summary>
    private static string ContentOf(IEnumerable<(long Onset, int Pitch, long Length)> notes) =>
        string.Join(";", notes.Order().Select(note => string.Create(CultureInfo.InvariantCulture, $"{note.Onset}:{note.Pitch}:{note.Length}")));

    /// <summary>How alike two bars' notes are: those in common over the more of them; two empty bars are alike.</summary>
    private static double Likeness(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        int larger = Math.Max(a.Values.Sum(), b.Values.Sum());
        return larger == 0 ? 1 : (double)a.Sum(pair => Math.Min(pair.Value, b.GetValueOrDefault(pair.Key))) / larger;
    }

    /// <summary>The reading taken for one staff of one bar of the backbone, and the readings that agree with it.</summary>
    private sealed record Choice(int Measure, int Part, int Staff, Reading Source, int SourceMeasure, IReadOnlyList<Reading> Agreeing);

    /// <summary>One engine's reading, repaired, as XML (each part's bars) and as imported notes.</summary>
    private sealed partial class Reading
    {
        private readonly ILookup<(int Measure, int Track, int Staff), MusicXmlNote> _notes;
        private readonly Dictionary<string, int>[] _heads;

        private Reading(string engine, int rank, XElement root, MusicXmlImport import)
        {
            (Engine, Rank, Root, Import) = (engine, rank, root, import);
            Parts = [.. root.Elements("part").Select(ScanRepair.Bars)];
            _notes = import.SourceNotes.ToLookup(note => (note.MeasureIndex, note.Track, note.Staff));
            _heads = [.. Enumerable.Range(0, Measures).Select(HeadsOf)];
        }

        public string Engine { get; }

        public int Rank { get; }

        public XElement Root { get; }

        public MusicXmlImport Import { get; }

        public List<List<ScanRepair.Bar>> Parts { get; }

        public int Measures => Import.Measures.Count;

        /// <summary>The engine's MusicXML repaired by the rules for a reading, read as XML and as notes; null when it is not partwise MusicXML with bars.</summary>
        public static Reading? Of(ScanReading reading, int given)
        {
            ArgumentNullException.ThrowIfNull(reading);
            int rank = RankOf(reading.Engine, given);
            byte[] repaired = ScanRepair.RepairReading(reading.MusicXml, printedMeters: rank != 0);
            return ScanRepair.Parse(repaired) is { } root && PieceImporter.Import("scan.musicxml", repaired).Piece?.MusicXml is { Measures.Count: > 0 } import
                ? new Reading(reading.Engine, rank, root, import)
                : null;
        }

        /// <summary>Each part's staves, (part from 0, staff from 1): those its part list declares, at least one.</summary>
        public IEnumerable<(int Part, int Staff)> Staves() =>
            Import.Parts.SelectMany(part => Enumerable.Range(1, Math.Max(1, part.Staves)).Select(staff => (part.Index, staff)));

        /// <summary>The notes of one staff of a bar as text: onset in the bar, pitch and length within the bar, in order.</summary>
        public string Content(int measure, int part, int staff)
        {
            TickSpan bar = Import.Measures[measure].Span;
            return ContentOf(_notes[(measure, part, staff)]
                .Select(note => (note.Span.Onset.Value - bar.Onset.Value, note.Pitch, Math.Min(note.Span.End.Value, bar.End.Value) - note.Span.Onset.Value)));
        }

        /// <summary>Whether one staff of a bar adds up to the meter in force there (<see cref="ScanRepair.AddsUp"/>).</summary>
        public bool AddsUp(int measure, int part, int staff)
        {
            if (measure >= Parts[part].Count)
            {
                return false;
            }

            ScanRepair.Bar bar = Parts[part][measure];
            return ScanRepair.AddsUp(bar, new QuarterFraction(BarTicks(measure), CanonicalTime.TicksPerQuarter), bar.Onsets, staff);
        }

        /// <summary>How long a bar is by the meter in force there, in ticks.</summary>
        public long BarTicks(int measure) => Import.Meter.SignatureAt(Import.Measures[measure].Span.Onset).BarTicks;

        /// <summary>The notes of a bar as counted for alignment: how many of each part, staff, onset in the bar and pitch.</summary>
        public Dictionary<string, int> Heads(int measure) => _heads[measure];

        private Dictionary<string, int> HeadsOf(int measure)
        {
            long start = Import.Measures[measure].Span.Onset.Value;
            return Import.SourceNotes.Where(note => note.MeasureIndex == measure)
                .GroupBy(note => string.Create(CultureInfo.InvariantCulture, $"{note.Track}:{note.Staff}:{note.Span.Onset.Value - start}:{note.Pitch}"), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        }
    }
}