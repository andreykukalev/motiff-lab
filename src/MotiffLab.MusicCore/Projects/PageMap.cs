using System.Xml;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The rough link between a PDF's pages and the notes recognised from them (0023; Andrei, 2026-11-04): where in the piece
/// each page starts, so switching between the "PDF" and "Notes" views lands on the same stretch of music. It is rough by
/// design: a page starts at the bar the scan began it with, and a bar is on the last page that starts at or before it.
/// </summary>
/// <remarks>Compares by value.</remarks>
/// <param name="Starts">Where each page starts, from page 1, in increasing order; page 1 starts at the beginning.</param>
public sealed record PageMap(IReadOnlyList<Tick> Starts)
{
    public IReadOnlyList<Tick> Starts { get; } = Valid(Starts);

    /// <summary>How many pages the map covers.</summary>
    public int Pages => Starts.Count;

    /// <summary>Where a page starts; a page past the last starts where the last does.</summary>
    public Tick StartOf(int page) => Starts[Math.Clamp(page, 1, Pages) - 1];

    /// <summary>The page a place in the piece is on, from 1.</summary>
    public int PageOf(Tick position)
    {
        int page = 1;
        while (page < Pages && Starts[page] <= position)
        {
            page++;
        }

        return page;
    }

    /// <summary>
    /// The map for notes the scan read from a PDF of <paramref name="pages"/> pages: each page from the bar the scan server
    /// marked as starting it (<c>&lt;print new-page="yes"&gt;</c> in the first part, as services/omr writes it), or, when the
    /// marks are missing or do not match the pages, the bars shared evenly between the pages.
    /// </summary>
    /// <exception cref="ArgumentException">The piece is not MusicXML.</exception>
    public static PageMap Of(ImportedPiece piece, int pages)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);
        if (piece.MusicXml is not { } import || piece.ScoreXml is not { } xml)
        {
            throw new ArgumentException("A page map is made from MusicXML.", nameof(piece));
        }

        IReadOnlyList<Tick> measures = [.. import.Measures.Select(measure => measure.Span.Onset)];
        if (measures.Count == 0)
        {
            return new PageMap([Tick.Zero]);
        }

        IReadOnlyList<int> marked = NewPages(xml);
        IEnumerable<int> firsts = marked.Count + 1 == pages && marked.All(index => index > 0 && index < measures.Count)
            ? [0, .. marked]
            : Enumerable.Range(0, Math.Min(pages, measures.Count)).Select(page => page * measures.Count / Math.Min(pages, measures.Count));
        return new PageMap([.. firsts.Select(index => index == 0 ? Tick.Zero : measures[index]).Distinct()]);
    }

    /// <summary>
    /// The map for a PDF of <paramref name="pages"/> pages added beside notes it was not read into (testing item 16): the
    /// bars before <paramref name="end"/> shared evenly between the pages.
    /// </summary>
    public static PageMap Even(MeterMap meter, Tick end, int pages)
    {
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);
        Tick[] bars = [.. meter.BarsBefore(end).Select(bar => bar.Span.Onset)];
        if (bars.Length == 0)
        {
            return new PageMap([Tick.Zero]);
        }

        int shared = Math.Min(pages, bars.Length);
        return new PageMap([.. Enumerable.Range(0, shared).Select(page => page == 0 ? Tick.Zero : bars[page * bars.Length / shared]).Distinct()]);
    }

    /// <summary>
    /// The map carried from notes read from the PDF over to other notes of the same piece by bar numbers (testing item 16):
    /// a page that starts at bar 9 of the notes read starts at bar 9 of these. A page past the last bar of these notes, or
    /// starting no later than the page before, is left out.
    /// </summary>
    public PageMap Through(MeterMap read, MeterMap to, Tick end)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(to);
        List<Tick> starts = [Tick.Zero];
        foreach (Tick start in Starts.Skip(1))
        {
            int number = read.ToPosition(start).Bar;
            Bar bar;
            try
            {
                bar = to.GetBar(number);
            }
            catch (ArgumentOutOfRangeException)
            {
                break;
            }

            if (bar.Span.Onset >= end)
            {
                break;
            }

            if (bar.Span.Onset > starts[^1])
            {
                starts.Add(bar.Span.Onset);
            }
        }

        return new PageMap(starts);
    }

    /// <summary>The measures of the first part that begin a new page, by index from 0, in order.</summary>
    private static List<int> NewPages(byte[] xml)
    {
        var found = new List<int>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true };
        try
        {
            using var stream = new MemoryStream(xml, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            int measure = -1;
            int depth = -1;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "part")
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.LocalName == "measure")
                {
                    measure++;
                    depth = reader.Depth;
                }
                else if (reader.LocalName == "print" && measure >= 0 && reader.Depth == depth + 1
                    && reader.GetAttribute("new-page") == "yes" && (found.Count == 0 || found[^1] != measure))
                {
                    found.Add(measure);
                }
            }
        }
        catch (XmlException)
        {
            return [];
        }

        return found;
    }

    private static IReadOnlyList<Tick> Valid(IReadOnlyList<Tick> starts)
    {
        ArgumentNullException.ThrowIfNull(starts);
        if (starts.Count == 0 || starts[0] != Tick.Zero)
        {
            throw new ArgumentException("Page 1 starts at the beginning of the piece.", nameof(starts));
        }

        for (int i = 1; i < starts.Count; i++)
        {
            if (starts[i] <= starts[i - 1])
            {
                throw new ArgumentException("Each page starts after the one before.", nameof(starts));
            }
        }

        return [.. starts];
    }

    public bool Equals(PageMap? other) => other is not null && Starts.SequenceEqual(other.Starts);

    public override int GetHashCode() => Starts.Aggregate(0, (hash, start) => HashCode.Combine(hash, start));
}

/// <summary>
/// The PDF piece a piece's notes were read from (0023; Andrei, 2026-11-05): the PDF's project, which is never changed, and
/// where each of its pages starts in these notes, so the player switches between the score as it is and the notes read
/// from it at the same place.
/// </summary>
/// <param name="ProjectId">The PDF piece's project id.</param>
/// <param name="Pages">Where each of its pages starts in these notes.</param>
public sealed record ReadFromPdf(string ProjectId, PageMap Pages)
{
    public string ProjectId { get; } = !string.IsNullOrWhiteSpace(ProjectId) ? ProjectId : throw new ArgumentException("The PDF piece's id is required.", nameof(ProjectId));

    public PageMap Pages { get; } = Pages ?? throw new ArgumentNullException(nameof(Pages));
}