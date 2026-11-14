using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>
/// Joins the MusicXML read from each page of a photographed or scanned piece into one score, and splits it back, so pages
/// can be read one request at a time and a page read again replaces its own measures (0011; the scan server's
/// services/omr/app/merge.py, ported).
/// </summary>
/// <remarks>
/// <para>
/// The engine writes one partwise score per page, each with its own part list and attributes. The pages of a piano piece
/// have the same parts, so the measures of page 2 follow those of page 1 in each part, and measure numbers run on. Each
/// page's first measure keeps its attributes (divisions, key, time, clefs), which MusicXML allows mid-part, so durations
/// stay right even when pages use different divisions. The first measure of each page after the first starts with
/// <c>&lt;print new-page="yes"/&gt;</c>, so the bars of each page are known (0023, <see cref="Projects.PageMap"/>).
/// </para>
/// <para>Pure and deterministic: the same pages give the same bytes. Nothing is read from outside the text (no DTD, no entities).</para>
/// </remarks>
public static class ScanJoin
{
    private static readonly XmlReaderSettings Safe = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

    /// <summary>The pages joined into one score, the engine's mistakes in it put right (<see cref="ScanRepair"/>); one page that needs no repair comes back as it is.</summary>
    /// <exception cref="ArgumentException">No page, a page that is not partwise MusicXML, or pages with different parts.</exception>
    public static byte[] Join(IReadOnlyList<byte[]> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
        {
            throw new ArgumentException("No page was read.", nameof(pages));
        }

        XDocument[] scores = [.. pages.Select((page, index) => Parse(page, index + 1))];
        XElement first = scores[0].Root!;
        List<XElement> parts = [.. first.Elements("part")];
        if (parts.Count == 0)
        {
            throw new ArgumentException("Page 1 has no music.", nameof(pages));
        }

        if (pages.Count == 1)
        {
            return ScanRepair.Repair(pages[0]);
        }

        int number = LastNumber(parts[0]);
        for (int page = 2; page <= scores.Length; page++)
        {
            List<XElement> pageParts = [.. scores[page - 1].Root!.Elements("part")];
            if (pageParts.Count != parts.Count)
            {
                throw new ArgumentException($"Page {page} has {pageParts.Count} parts where page 1 has {parts.Count}; scan the pages one at a time instead.", nameof(pages));
            }

            int added = 0;
            foreach ((XElement part, XElement pagePart) in parts.Zip(pageParts))
            {
                List<XElement> measures = [.. pagePart.Elements("measure")];
                for (int offset = 1; offset <= measures.Count; offset++)
                {
                    XElement measure = new(measures[offset - 1]);
                    measure.SetAttributeValue("number", (number + offset).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (offset == 1)
                    {
                        StartPage(measure);
                    }

                    part.Add(measure);
                }

                added = Math.Max(added, measures.Count);
            }

            number += added;
        }

        return ScanRepair.Repair(Write(first));
    }

    /// <summary>
    /// A joined score cut back into its pages at the measures marked as starting one, each a whole score with the joined
    /// score's header and part list, its measures keeping their joined numbers and losing only the page mark. A score
    /// without marks is one page. <c>Join(Split(score))</c> gives the score again.
    /// </summary>
    /// <exception cref="ArgumentException">The score is not partwise MusicXML, or its parts start pages in different places.</exception>
    public static IReadOnlyList<byte[]> Split(byte[] score)
    {
        ArgumentNullException.ThrowIfNull(score);
        XElement root = Parse(score, 1).Root!;
        List<XElement> parts = [.. root.Elements("part")];
        List<List<List<XElement>>> pagesOfParts = [.. parts.Select(PagesOf)];
        int count = pagesOfParts.Count == 0 ? 1 : pagesOfParts[0].Count;
        if (pagesOfParts.Any(pages => pages.Count != count))
        {
            throw new ArgumentException("The parts of the score start their pages in different places.", nameof(score));
        }

        var split = new List<byte[]>(count);
        for (int page = 0; page < count; page++)
        {
            XElement copy = new(root.Name, root.Attributes(), root.Nodes().Where(node => node is not XElement { Name.LocalName: "part" }).Select(Clone));
            for (int part = 0; part < parts.Count; part++)
            {
                XElement pagePart = new(parts[part].Name, parts[part].Attributes(), pagesOfParts[part][page].Select(measure => new XElement(measure)));
                if (page > 0)
                {
                    EndPageMark(pagePart.Elements("measure").First());
                }

                copy.Add(pagePart);
            }

            split.Add(Write(copy));
        }

        return split;
    }

    /// <summary>The score with the pages in <paramref name="replaced"/> (by number, from 1) read again: split, swapped, joined.</summary>
    /// <exception cref="ArgumentException">A page number is not a page of the score, or <see cref="Split"/> or <see cref="Join"/> fails.</exception>
    public static byte[] Replace(byte[] score, IReadOnlyDictionary<int, byte[]> replaced)
    {
        ArgumentNullException.ThrowIfNull(replaced);
        List<byte[]> pages = [.. Split(score)];
        foreach ((int page, byte[] musicXml) in replaced.OrderBy(pair => pair.Key))
        {
            if (page < 1 || page > pages.Count)
            {
                throw new ArgumentException($"The score has {pages.Count} pages, so there is no page {page} to replace.", nameof(replaced));
            }

            pages[page - 1] = musicXml;
        }

        return Join(pages);
    }

    /// <summary>The measures of a part, page by page, cut before each measure marked as starting a page.</summary>
    private static List<List<XElement>> PagesOf(XElement part)
    {
        List<List<XElement>> pages = [[]];
        foreach (XElement measure in part.Elements("measure"))
        {
            if (pages[^1].Count > 0 && StartsPage(measure))
            {
                pages.Add([]);
            }

            pages[^1].Add(measure);
        }

        return pages;
    }

    private static bool StartsPage(XElement measure) =>
        measure.Elements("print").Any(print => (string?)print.Attribute("new-page") == "yes");

    /// <summary>Marks the measure as the first on a new page, keeping any layout the engine gave it.</summary>
    private static void StartPage(XElement measure)
    {
        XElement? printed = measure.Element("print");
        if (printed is null)
        {
            printed = new XElement("print");
            measure.AddFirst(printed);
        }

        printed.SetAttributeValue("new-page", "yes");
    }

    /// <summary>Takes away the page mark <see cref="StartPage"/> put on, and the print element when nothing else is left in it.</summary>
    private static void EndPageMark(XElement measure)
    {
        foreach (XElement print in measure.Elements("print").Where(print => (string?)print.Attribute("new-page") == "yes").ToList())
        {
            print.SetAttributeValue("new-page", null);
            if (!print.HasAttributes && !print.HasElements)
            {
                print.Remove();
            }
        }
    }

    private static XNode Clone(XNode node) => node switch
    {
        XElement element => new XElement(element),
        XText text => new XText(text),
        XComment comment => new XComment(comment),
        XProcessingInstruction instruction => new XProcessingInstruction(instruction),
        _ => new XText(string.Empty),
    };

    private static XDocument Parse(byte[] page, int number)
    {
        XDocument document;
        try
        {
            using var stream = new MemoryStream(page, writable: false);
            using var reader = XmlReader.Create(stream, Safe);
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new ArgumentException($"The music read from page {number} is damaged.", exception);
        }

        if (document.Root is not { Name.LocalName: "score-partwise" })
        {
            throw new ArgumentException($"The music read from page {number} is not a partwise MusicXML score.");
        }

        return document;
    }

    private static int LastNumber(XElement part)
    {
        List<XElement> measures = [.. part.Elements("measure")];
        for (int i = measures.Count - 1; i >= 0; i--)
        {
            if (int.TryParse((string?)measures[i].Attribute("number"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int number))
            {
                return number;
            }
        }

        return measures.Count;
    }

    private static byte[] Write(XElement root) =>
        Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n" + root.ToString(SaveOptions.DisableFormatting));
}