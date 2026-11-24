using PigDocument = UglyToad.PdfPig.PdfDocument;

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Fonts.CompactFontFormat;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Tokens;

namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>A glyph of a music font where the page prints it, y measured down from the page's top, in points.</summary>
/// <param name="X">The glyph's origin, its left edge.</param>
/// <param name="Y">The glyph's origin on its baseline: a notehead's centre line, an accidental's or a clef's reference line.</param>
/// <param name="Size">The font size it is printed at; a grace note's or a cue note's is smaller.</param>
/// <param name="Right">The right edge of its advance; of the glyph drawn, in a legacy font (<see cref="VectorPage.Placed"/>).</param>
internal sealed record PlacedGlyph(MusicGlyph Glyph, double X, double Y, double Size, double Right);

/// <summary>A number printed in a text font where a tuplet's number may be: its value, left and right edges, baseline and size.</summary>
internal sealed record PlacedNumber(int Value, double X, double Right, double Y, double Size)
{
    /// <summary>The middle of the figure, about a third of the size above the baseline.</summary>
    public double Middle => Y - (0.35 * Size);
}

/// <summary>A straight stroke or the axis of a thin filled bar, y measured down: a staff line, a stem, a bar line.</summary>
internal readonly record struct Stroke(double X0, double Y0, double X1, double Y1)
{
    public double Length => Math.Abs(X1 - X0) + Math.Abs(Y1 - Y0);
}

/// <summary>The box around a curve, y measured down: a tie or a slur.</summary>
internal readonly record struct Curve(double Left, double Right, double Top, double Bottom);

/// <summary>A filled four-sided shape, y measured down, as a beam is drawn: its left and right edges, top and bottom at each.</summary>
internal readonly record struct Quad(double Left, double Right, double TopLeft, double BottomLeft, double TopRight, double BottomRight)
{
    public double Thickness => ((BottomLeft - TopLeft) + (BottomRight - TopRight)) / 2;

    /// <summary>The middle of the shape at x, along its slope.</summary>
    public double MiddleAt(double x)
    {
        double t = Right > Left ? Math.Clamp((x - Left) / (Right - Left), 0, 1) : 0;
        return (((TopLeft + BottomLeft) / 2) * (1 - t)) + (((TopRight + BottomRight) / 2) * t);
    }
}

/// <summary>
/// What one page of a PDF draws, as the vector reader needs it (decision record 0029): the glyphs of its music fonts, its
/// straight strokes and thin bars, its filled quadrilaterals, and how many images it holds. Read with PdfPig; nothing else
/// of the page is kept.
/// </summary>
internal sealed class VectorPage
{
    public required int Number { get; init; }

    public required double Width { get; init; }

    public required double Height { get; init; }

    public List<PlacedGlyph> Glyphs { get; } = [];

    /// <summary>Strokes that run across: staff lines, ledger lines, the lines of a beam drawn as a stroke.</summary>
    public List<Stroke> Horizontal { get; } = [];

    /// <summary>Strokes that run up and down: stems and bar lines, drawn as lines or as thin filled bars.</summary>
    public List<Stroke> Vertical { get; } = [];

    public List<Quad> Quads { get; } = [];

    /// <summary>Curves the page draws, as boxes: ties and slurs among them.</summary>
    public List<Curve> Curves { get; } = [];

    /// <summary>Straight strokes at a slant: the lines of a tuplet's bracket over a sloping group, hairpins, slashes.</summary>
    public List<Stroke> Slanted { get; } = [];

    /// <summary>Single digits in text fonts: tuplet numbers and octave lines' 8 among them, also fingerings and bar numbers.</summary>
    public List<PlacedNumber> Numbers { get; } = [];

    public int Images { get; init; }

    /// <summary>Glyphs of the music fonts the reader knows.</summary>
    public int MusicGlyphs { get; set; }

    /// <summary>Glyphs of a music font the reader does not know, which make the page one for the scan server.</summary>
    public int UnknownMusicGlyphs { get; set; }

    /// <summary>The page as PdfPig reads it.</summary>
    public static VectorPage Read(PigDocument document, Page page)
    {
        var read = new VectorPage { Number = page.Number, Width = page.Width, Height = page.Height, Images = page.GetImages().Count() };
        var emmentaler = new Dictionary<string, CompactFontFormatFontCollection?>(StringComparer.Ordinal);
        foreach (Letter letter in page.Letters)
        {
            read.Add(document, page, letter, emmentaler);
        }

        foreach (PdfPath path in page.Paths)
        {
            read.Add(path);
        }

        read.MergeVertical();
        return read;
    }

    /// <summary>
    /// Upright strokes drawn twice (a filled bar with its outline stroked, as LilyPond draws a stem) made one: strokes at one
    /// x whose lengths overlap are joined.
    /// </summary>
    private void MergeVertical()
    {
        var merged = new List<Stroke>();
        foreach (Stroke line in Vertical.OrderBy(line => line.X0).ThenBy(line => line.Y0))
        {
            int same = merged.FindLastIndex(other => line.X0 - other.X0 < 0.3 && line.Y0 <= other.Y1 && line.Y1 >= other.Y0);
            if (same >= 0)
            {
                Stroke other = merged[same];
                merged[same] = new Stroke((other.X0 + line.X0) / 2, Math.Min(other.Y0, line.Y0), (other.X0 + line.X0) / 2, Math.Max(other.Y1, line.Y1));
            }
            else
            {
                merged.Add(line);
            }
        }

        Vertical.Clear();
        Vertical.AddRange(merged);
    }

    private void Add(PigDocument document, Page page, Letter letter, Dictionary<string, CompactFontFormatFontCollection?> emmentaler)
    {
        MusicFonts.Family family = MusicFonts.Of(letter.FontName ?? "");
        if (string.IsNullOrEmpty(letter.Value))
        {
            return;
        }

        if (family == MusicFonts.Family.None)
        {
            if (letter.Value is [>= '0' and <= '9'])
            {
                Numbers.Add(new PlacedNumber(letter.Value[0] - '0', letter.StartBaseLine.X, letter.EndBaseLine.X, Height - letter.StartBaseLine.Y, letter.PointSize));
            }

            return;
        }

        if (family == MusicFonts.Family.Unknown)
        {
            UnknownMusicGlyphs++;
            return;
        }

        int code = char.ConvertToUtf32(letter.Value, 0);
        MusicGlyph glyph = family == MusicFonts.Family.Emmentaler
            ? MusicFonts.ByEmmentalerName(EmmentalerName(document, page, letter.FontName!, code, emmentaler))
            : MusicFonts.ByCodePoint(family, code);
        MusicGlyphs++;
        if (glyph != MusicGlyph.Other)
        {
            Glyphs.Add(Placed(glyph, family, letter, Height));
        }
    }

    /// <summary>
    /// A music font's letter as a glyph on a page of the given height. A legacy font's advance runs wider than its glyph
    /// (Opus's notehead by about 1.5 pt, past the stem beside it), so its right edge is the drawn glyph's.
    /// </summary>
    internal static PlacedGlyph Placed(MusicGlyph glyph, MusicFonts.Family family, Letter letter, double height) =>
        new(glyph, letter.StartBaseLine.X, height - letter.StartBaseLine.Y, letter.PointSize,
            family == MusicFonts.Family.Legacy ? letter.BoundingBox.Right : letter.EndBaseLine.X);

    private void Add(PdfPath path)
    {
        if (path.IsClipping)
        {
            return;
        }

        foreach (PdfSubpath subpath in path)
        {
            List<PdfPoint> points = Points(subpath, out bool curved);
            if (curved && subpath.GetBoundingRectangle() is { } box)
            {
                Curves.Add(new Curve(box.Left, box.Right, Height - box.Top, Height - box.Bottom));
            }

            if (curved || points.Count < 2)
            {
                continue;
            }

            if (path.IsFilled && points.Count >= 4)
            {
                AddFilled(points);
            }
            else if (path.IsStroked && points.Count == 2)
            {
                AddStroke(points[0], points[1]);
            }
            else if (path.IsStroked)
            {
                for (int i = 1; i < points.Count; i++)
                {
                    AddStroke(points[i - 1], points[i]);
                }
            }
        }
    }

    private void AddStroke(PdfPoint a, PdfPoint b)
    {
        var stroke = new Stroke(Math.Min(a.X, b.X), Height - Math.Max(a.Y, b.Y), Math.Max(a.X, b.X), Height - Math.Min(a.Y, b.Y));
        if (stroke.Y1 - stroke.Y0 < 0.01 && stroke.X1 - stroke.X0 > 0.5)
        {
            Horizontal.Add(stroke);
        }
        else if (stroke.X1 - stroke.X0 < 0.01 && stroke.Y1 - stroke.Y0 > 0.5)
        {
            Vertical.Add(stroke);
        }
        else if (stroke.Length > 2)
        {
            // Kept with its ends as drawn, left end first: a bracket line rises or falls with the group under it.
            (PdfPoint left, PdfPoint right) = a.X <= b.X ? (a, b) : (b, a);
            Slanted.Add(new Stroke(left.X, Height - left.Y, right.X, Height - right.Y));
        }
    }

    /// <summary>A filled shape: a thin upright bar is a stem or a bar line, a thin flat one a staff line, any other four-sided one may be a beam.</summary>
    private void AddFilled(List<PdfPoint> points)
    {
        double left = points.Min(point => point.X), right = points.Max(point => point.X);
        double top = Height - points.Max(point => point.Y), bottom = Height - points.Min(point => point.Y);
        double width = right - left, height = bottom - top;
        if (width < 1.0 && height > 2 * width)
        {
            Vertical.Add(new Stroke((left + right) / 2, top, (left + right) / 2, bottom));
            return;
        }

        if (height < 0.6 && width > 10 * height)
        {
            Horizontal.Add(new Stroke(left, (top + bottom) / 2, right, (top + bottom) / 2));
            return;
        }

        List<PdfPoint> corners = [.. points.DistinctBy(point => (Math.Round(point.X, 2), Math.Round(point.Y, 2)))];
        if (corners.Count != 4)
        {
            return;
        }

        PdfPoint[] byX = [.. corners.OrderBy(point => point.X)];
        (double topLeft, double bottomLeft) = Edge(byX[0], byX[1]);
        (double topRight, double bottomRight) = Edge(byX[2], byX[3]);
        if (Math.Abs(byX[1].X - byX[0].X) < 0.5 && Math.Abs(byX[3].X - byX[2].X) < 0.5)
        {
            Quads.Add(new Quad(left, right, topLeft, bottomLeft, topRight, bottomRight));
        }
    }

    private (double Top, double Bottom) Edge(PdfPoint a, PdfPoint b) => (Height - Math.Max(a.Y, b.Y), Height - Math.Min(a.Y, b.Y));

    private static List<PdfPoint> Points(PdfSubpath subpath, out bool curved)
    {
        curved = false;
        var points = new List<PdfPoint>();
        foreach (PdfSubpath.IPathCommand command in subpath.Commands)
        {
            switch (command)
            {
                case PdfSubpath.Move move:
                    points.Add(move.Location);
                    break;
                case PdfSubpath.Line line:
                    if (points.Count == 0 || points[^1] != line.From)
                    {
                        points.Add(line.From);
                    }

                    points.Add(line.To);
                    break;
                case PdfSubpath.BezierCurve:
                    curved = true;
                    break;
            }
        }

        if (points.Count > 2 && points[0] == points[^1])
        {
            points.RemoveAt(points.Count - 1);
        }

        return points;
    }

    /// <summary>
    /// The glyph name of an Emmentaler character: LilyPond embeds its font as CFF with the names kept ("noteheads.s2"), and
    /// the character code is the index in the font's encoding.
    /// </summary>
    private static string EmmentalerName(PigDocument document, Page page, string fontName, int code, Dictionary<string, CompactFontFormatFontCollection?> cache)
    {
        if (!cache.TryGetValue(fontName, out CompactFontFormatFontCollection? font))
        {
            font = cache[fontName] = EmbeddedCff(document, page, fontName);
        }

        try
        {
            return font?.GetCharacterName(code, false) ?? "";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IndexOutOfRangeException or KeyNotFoundException)
        {
            return "";
        }
    }

    private static CompactFontFormatFontCollection? EmbeddedCff(PigDocument document, Page page, string fontName)
    {
        try
        {
            if (Resolve<DictionaryToken>(document, page.Dictionary.Data.GetValueOrDefault("Resources")) is not { } resources
                || Resolve<DictionaryToken>(document, resources.Data.GetValueOrDefault("Font")) is not { } fonts)
            {
                return null;
            }

            foreach (IToken value in fonts.Data.Values)
            {
                if (Resolve<DictionaryToken>(document, value) is { } font
                    && font.Data.GetValueOrDefault("BaseFont") is NameToken name && name.Data == fontName
                    && Resolve<DictionaryToken>(document, font.Data.GetValueOrDefault("FontDescriptor")) is { } descriptor
                    && Resolve<StreamToken>(document, descriptor.Data.GetValueOrDefault("FontFile3")) is { } file)
                {
                    return CompactFontFormatParser.Parse(new CompactFontFormatData(UglyToad.PdfPig.PdfExtensions.Decode(file, DefaultFilterProvider.Instance).ToArray()));
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IndexOutOfRangeException or FormatException)
        {
            // A font PdfPig cannot parse gives no names: its glyphs stay unread and the page goes to the scan server.
        }

        return null;
    }

    private static T? Resolve<T>(PigDocument document, IToken? token) where T : class, IToken =>
        token is IndirectReferenceToken reference ? document.Structure.GetObject(reference.Data)?.Data as T : token as T;
}