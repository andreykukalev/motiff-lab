using System.Text.Json;

namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>
/// A scanned page's evidence as the scan server gives it beside the engines' readings (ADR 0035, section 1; version 1,
/// v1.3 s-2): the bars it found by image processing, system by system, and the symbols copisteria's detector found, each
/// placed in a system, a bar and a staff. Coordinates are pixels of the page image: the merge uses them only to tell left
/// from right and near from far, never for time. Nothing here is a note; <see cref="ScanAssembly"/> and the merge read it.
/// </summary>
internal sealed class ScanEvidence
{
    /// <summary>The version of the evidence's schema this reads.</summary>
    public const int Version = 1;

    private ScanEvidence(IReadOnlyList<ScanEvidenceBar> bars, ScanImage? image) => (Bars, Image) = (bars, image);

    /// <summary>The page's bars in reading order: the systems top down, each system's bars left to right.</summary>
    internal IReadOnlyList<ScanEvidenceBar> Bars { get; }

    /// <summary>The page image all coordinates are in, as the server turned it; null when the evidence does not say.</summary>
    internal ScanImage? Image { get; }

    /// <summary>The evidence of one page, as JSON: {"evidence": 1, "systems": [...], "symbols": [...]}.</summary>
    /// <exception cref="ArgumentException">The text is not version 1 of the page evidence.</exception>
    public static ScanEvidence Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            throw new ArgumentException("The page's evidence is not the scan server's, version 1.", nameof(json), exception);
        }
    }

    private static ScanEvidence Read(JsonElement root)
    {
        if (root.GetProperty("evidence").GetInt32() != Version)
        {
            throw new FormatException("Another version of the evidence.");
        }

        var bars = new List<(int System, int Bar, Dictionary<int, double> Spaces, List<ScanSymbol> Symbols, ScanBox? Box)>();
        int system = 0;
        foreach (JsonElement found in root.GetProperty("systems").EnumerateArray())
        {
            Dictionary<int, double> spaces = found.GetProperty("staves").EnumerateArray()
                .ToDictionary(staff => staff.GetProperty("staff").GetInt32(), staff => staff.GetProperty("space").GetDouble());
            double[]? box = Numbers(found, "box", 4);
            bars.AddRange(found.GetProperty("bars").EnumerateArray().Select((bar, index) =>
                (system, index, spaces, new List<ScanSymbol>(), box is not null && Numbers(bar, "x", 2) is { } x ? new ScanBox(x[0], box[1], x[1], box[3]) : (ScanBox?)null)));
            system++;
        }

        Dictionary<(int System, int Bar), List<ScanSymbol>> placed = bars.ToDictionary(bar => (bar.System, bar.Bar), bar => bar.Symbols);
        foreach (JsonElement symbol in root.GetProperty("symbols").EnumerateArray())
        {
            if (placed.TryGetValue((symbol.GetProperty("system").GetInt32(), symbol.GetProperty("bar").GetInt32()), out List<ScanSymbol>? into))
            {
                into.Add(ScanSymbol.Read(symbol));
            }
        }

        ScanImage? image = root.TryGetProperty("image", out JsonElement page) && page.ValueKind == JsonValueKind.Object
            && page.TryGetProperty("width", out JsonElement width) && page.TryGetProperty("height", out JsonElement height)
            ? new ScanImage(width.GetInt32(), height.GetInt32(), page.TryGetProperty("rotated", out JsonElement rotated) && rotated.ValueKind == JsonValueKind.Number ? rotated.GetInt32() : 0)
            : null;
        return new ScanEvidence([.. bars.Select(bar => new ScanEvidenceBar(bar.Spaces, bar.Symbols) { Box = bar.Box })], image);
    }

    /// <summary>The <paramref name="count"/> numbers of a list named <paramref name="name"/>; null when there is no such list.</summary>
    private static double[]? Numbers(JsonElement element, string name, int count) =>
        element.TryGetProperty(name, out JsonElement list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() == count
            ? [.. list.EnumerateArray().Select(value => value.GetDouble())]
            : null;
}

/// <summary>The page image the evidence's coordinates are in: its size in pixels and the degrees clockwise the server turned the page (0, 90, 180 or 270).</summary>
internal sealed record ScanImage(int Width, int Height, int Rotated);

/// <summary>A box on the page image, in its pixels from the top left: left, top, right, bottom.</summary>
internal sealed record ScanBox(double Left, double Top, double Right, double Bottom);

/// <summary>
/// One bar found on the page: each staff's space (the distance between its lines, by staff from 1), the symbols in it, and
/// its box on the page, its x range over its system's height (null when the evidence gives no box).
/// </summary>
internal sealed record ScanEvidenceBar(IReadOnlyDictionary<int, double> Spaces, IReadOnlyList<ScanSymbol> Symbols)
{
    public ScanBox? Box { get; init; }
}

/// <summary>
/// A symbol the detector found: its SMuFL class ("noteheadBlack", "restQuarter", "accidentalSharp", "augmentationDot",
/// "tuplet3", "gClef", "keySignature"…), its staff in the system from 1, its centre and the left and right of its box,
/// and how sure the detector was. A notehead says its staff position (0 the middle line, a step a position), its value as
/// a MusicXML type, its dots and whether it is a grace note; a clef its line; a key signature its fifths; a whole-bar rest
/// says so.
/// </summary>
internal sealed record ScanSymbol(int Staff, string Class, double X, double Y, double Left, double Right, double Confidence)
{
    public int? Position { get; init; }

    public string? Duration { get; init; }

    public int Dots { get; init; }

    public bool Grace { get; init; }

    public int? Line { get; init; }

    public int? Fifths { get; init; }

    public bool Measure { get; init; }

    /// <summary>Whether the symbol is a notehead the assembly and the page check count: a staff position and a value, not a grace note.</summary>
    public bool IsHead => Class.StartsWith("notehead", StringComparison.Ordinal) && Position is not null && !Grace;

    public static ScanSymbol Read(JsonElement symbol)
    {
        double x = symbol.GetProperty("x").GetDouble();
        double[] box = symbol.TryGetProperty("box", out JsonElement written) && written.GetArrayLength() == 4
            ? [.. written.EnumerateArray().Select(value => value.GetDouble())]
            : [x, 0, x, 0];
        return new ScanSymbol(symbol.GetProperty("staff").GetInt32(), symbol.GetProperty("class").GetString()!, x, symbol.GetProperty("y").GetDouble(),
            box[0], box[2], symbol.TryGetProperty("confidence", out JsonElement sure) ? sure.GetDouble() : 1)
        {
            Position = Int(symbol, "position"),
            Duration = symbol.TryGetProperty("duration", out JsonElement duration) && duration.ValueKind == JsonValueKind.String ? duration.GetString() : null,
            Dots = Int(symbol, "dots") ?? 0,
            Grace = symbol.TryGetProperty("grace", out JsonElement grace) && grace.ValueKind == JsonValueKind.True,
            Line = Int(symbol, "line"),
            Fifths = Int(symbol, "fifths"),
            Measure = symbol.TryGetProperty("measure", out JsonElement measure) && measure.ValueKind == JsonValueKind.True,
        };
    }

    private static int? Int(JsonElement symbol, string name) =>
        symbol.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}