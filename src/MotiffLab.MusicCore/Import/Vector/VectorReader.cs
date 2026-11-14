using MotiffLab.MusicCore.Import.Scan;

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>How a page of a PDF is read.</summary>
public enum VectorPageKind
{
    /// <summary>A page a notation program drew, read here from its glyphs and lines.</summary>
    Read,

    /// <summary>A page for the scan server: a scanned image, music in a font the reader does not know, or a page that failed the checks.</summary>
    Scan,

    /// <summary>A page with no music: a title page, notes, a blank page.</summary>
    Blank,
}

/// <summary>One page of a PDF as the vector reader leaves it.</summary>
/// <param name="Number">The page number, from 1.</param>
/// <param name="Kind">How the page is read.</param>
/// <param name="MusicXml">The page's music for a page read here, in the scan server's shape; null otherwise.</param>
/// <param name="Notes">The notes read.</param>
/// <param name="Bars">The bars read.</param>
/// <param name="Issues">What could not be placed or does not add up, in English, for the page report.</param>
public sealed record VectorPageReading(int Number, VectorPageKind Kind, byte[]? MusicXml, int Notes, int Bars, IReadOnlyList<string> Issues)
{
    /// <summary>The staves of each system.</summary>
    public int Staves { get; init; }

    /// <summary>
    /// The page's report as the scan server writes one (<see cref="PageReport"/>), for a page read here: drawn, so nothing
    /// is turned, blurred or faint, and every bar was read.
    /// </summary>
    public PageReport Report => new(Number, 0, Sharpness: 1, Contrast: 255, Staves, Noteheads: Notes, BarLines: Bars, SpacePx: 0, Measures: Bars);
}

/// <summary>
/// Reads the music of a born-digital PDF on the device, with no image and no model (decision record 0029): a notation
/// program prints every symbol as a glyph of its music font and every staff line, stem, bar line and beam as a vector
/// path, so the notes, their lengths, the clefs, keys and meters are read exactly as printed. Each page is read on its own
/// and checked; a page that is an image, uses a font the reader does not know, or fails the checks is left to the scan
/// server. Pure and deterministic.
/// </summary>
public static class VectorReader
{
    /// <summary>The engine name pages read here carry in their provenance.</summary>
    public const string Engine = "vector";

    /// <summary>The reader's version: raised when it reads the same page differently.</summary>
    public const string Version = "1";

    /// <summary>The engine and version, as a scan's provenance names them ("homr 0.7.1").</summary>
    public const string EngineAndVersion = Engine + " " + Version;

    /// <summary>A page with more noteheads left unread than this share goes to the scan server.</summary>
    private const double MostHeadsLeftOut = 0.02;

    /// <summary>A page with more bars that do not add up than this share goes to the scan server.</summary>
    private const double MostBarsWrong = 0.1;

    /// <summary>Each page of the PDF, read or left to the scan server.</summary>
    /// <exception cref="ArgumentException">The bytes are not a PDF the reader can open.</exception>
    public static IReadOnlyList<VectorPageReading> Read(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        PigDocument document;
        try
        {
            document = PigDocument.Open(pdf);
        }
        catch (Exception exception) when (exception is PdfDocumentFormatException or InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            throw new ArgumentException("The file is not a PDF that can be read.", nameof(pdf), exception);
        }

        using (document)
        {
            var state = new ReadingState();
            var pages = new List<VectorPageReading>();
            foreach (Page page in document.GetPages())
            {
                pages.Add(ReadPage(VectorPage.Read(document, page), state));
            }

            return pages;
        }
    }

    private static VectorPageReading ReadPage(VectorPage page, ReadingState state)
    {
        List<VectorSystem> systems = Staves.Find(page);
        if (systems.Count == 0)
        {
            return new(page.Number, page.MusicGlyphs + page.UnknownMusicGlyphs > 0 || page.Images > 0 ? VectorPageKind.Scan : VectorPageKind.Blank, null, 0, 0,
                [page.Images > 0 ? "the page is an image" : "no staves were found"]);
        }

        if (page.UnknownMusicGlyphs > page.MusicGlyphs || page.Glyphs.Count(glyph => glyph.Glyph.IsNotehead()) == 0)
        {
            return new(page.Number, VectorPageKind.Scan, null, 0, 0, ["the music is printed in a font the reader does not know"]);
        }

        int staves = systems[0].Staves.Count;
        if (systems.Any(system => system.Staves.Count != staves))
        {
            return new(page.Number, VectorPageKind.Scan, null, 0, 0, ["the systems have different numbers of staves"]);
        }

        (IReadOnlyList<VectorClef> Clefs, int Fifths, VectorMeter? Meter) opening = ([.. Enumerable.Range(0, staves).Select(state.ClefOf)], state.FifthsOf(0), state.Meter);
        var issues = new List<string>();
        List<VectorBar> bars = ReadSystems(page, systems, state, issues);
        int wrong = BarCheck.Check(bars, issues, state, systems[0].Space);
        int heads = page.Glyphs.Count(glyph => glyph.Glyph.IsNotehead());
        int notes = bars.Sum(bar => bar.Staves.Sum(events => events.Sum(item => item.Pitches.Count)));
        bool failed = heads - notes > MostHeadsLeftOut * heads || wrong > MostBarsWrong * bars.Count || bars.Count == 0;
        if (failed)
        {
            issues.Insert(0, $"{heads - notes} of {heads} noteheads were left out and {wrong} of {bars.Count} bars do not add up");
        }

        return new(page.Number, failed ? VectorPageKind.Scan : VectorPageKind.Read, failed ? null : VectorMusicXml.Write(bars, staves, opening), notes, bars.Count, issues)
        {
            Staves = staves,
        };
    }

    private static List<VectorBar> ReadSystems(VectorPage page, List<VectorSystem> systems, ReadingState state, List<string> issues)
    {
        double normal = page.Glyphs.Where(glyph => glyph.Glyph.IsNotehead()).GroupBy(glyph => Math.Round(glyph.Size, 1))
            .OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();
        Dictionary<VectorStaff, List<PlacedGlyph>> glyphs = StaffAssignment.ByStaff(page, systems);
        var bars = new List<VectorBar>();
        for (int index = 0; index < systems.Count; index++)
        {
            var reading = new SystemReading(page, systems[index], state, normal, [.. systems.SelectMany(system => system.Staves)]);
            List<VectorBar> read = reading.Read([.. systems[index].Staves.Select(staff => glyphs[staff])]);
            issues.AddRange(reading.Issues.Select(issue => $"system {index + 1}, {issue}"));
            foreach (VectorBar bar in read)
            {
                state.Meter = bar.Meter ?? state.Meter;
                bar.InForce = state.Meter;
                bars.Add(bar);
            }
        }

        return bars;
    }
}