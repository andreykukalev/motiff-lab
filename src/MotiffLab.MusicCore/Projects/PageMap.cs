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