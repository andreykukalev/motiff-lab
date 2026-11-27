using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Projects.File.JsonRead;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// How a Notes was made, when it was recognised: from a PDF or photos ("scan": the bars the player checked and what the
/// scan server saw of each page, version 0.4) or heard in a recording ("transcription": the bars and tempo worked out
/// (0016), where they change signature, the seams and where the stretch sent started, version 0.5). The newer fields are
/// optional within version 15: versions 0.2 and 0.3 drop "scan"."pages", and 0.2 to 0.4 drop "meters" and "sentFromMs"
/// (the bars fall back to the grid's "signature" from the same starts), when they write the file again.
/// </summary>
internal static class OriginCodec
{
    internal static void WriteOrigin(Utf8JsonWriter json, ScanOrigin? scan, TranscriptionOrigin? heard)
    {
        if (scan is not null)
        {
            json.WriteStartObject("scan");
            JsonWrite.Numbers(json, "confirmedBars", scan.ConfirmedBars);
            WriteScanPages(json, scan.Pages);
            if (scan.Unread.Count > 0)
            {
                JsonWrite.Numbers(json, "unread", scan.Unread);
                JsonWrite.Numbers(json, "read", scan.ReadPages);
            }

            WriteMarks(json, scan.Marks);
            json.WriteEndObject();
        }

        if (heard is not null)
        {
            WriteHeard(json, heard);
        }
    }

    /// <summary>"transcription": the bars and tempo worked out, the seams and where the stretch sent started (0016; version 0.5).</summary>
    private static void WriteHeard(Utf8JsonWriter json, TranscriptionOrigin heard)
    {
        json.WriteStartObject("transcription");
        if (heard.Bars is { } bars)
        {
            json.WriteStartObject("bars");
            json.WriteNumber("origin", bars.Origin.Value);
            json.WriteNumber("sixteenthMicroseconds", bars.SixteenthMicroseconds);
            json.WriteString("signature", bars.Signature.ToString());
            JsonWrite.Numbers(json, "barStarts", bars.BarStarts);
            json.WriteNumber("stepsPerBeat", bars.StepsPerBeat);
            JsonWrite.Numbers(json, "beats", bars.Beats);
            WriteMeters(json, bars);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("bars");
        }

        if (heard.Seams.Count > 0)
        {
            JsonWrite.Numbers(json, "seams", heard.Seams);
        }

        WriteSentFrom(json, heard);
        json.WriteEndObject();
    }

    /// <summary>
    /// "marks" in "scan" (version 18): the player's marks «что не так», each {"kind", "bar", "last", "staff" (a clef or octave
    /// mark's), "value" (what the page prints)}; left out when there are none.
    /// </summary>
    private static void WriteMarks(Utf8JsonWriter json, IReadOnlyList<ReadingMark> marks)
    {
        if (marks.Count == 0)
        {
            return;
        }

        json.WriteStartArray("marks");
        foreach (ReadingMark mark in marks)
        {
            json.WriteStartObject();
            WriteMark(json, mark);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>A mark's fields as the file keeps them, in an object already open: "kind", "bar", "last", and "staff" and "value" when it has them.</summary>
    internal static void WriteMark(Utf8JsonWriter json, ReadingMark mark)
    {
        json.WriteString("kind", JsonNamingPolicy.CamelCase.ConvertName(mark.Kind.ToString()));
        json.WriteNumber("bar", mark.Bar);
        json.WriteNumber("last", mark.Last);
        if (mark.Staff is { } staff)
        {
            json.WriteNumber("staff", staff);
        }

        if (mark.Value is { } value)
        {
            json.WriteString("value", value);
        }
    }

    /// <summary>"pages" in "scan": each page's report as the scan server sent it, numbers only; left out when there are none.</summary>
    private static void WriteScanPages(Utf8JsonWriter json, IReadOnlyList<PageReport> pages)
    {
        if (pages.Count == 0)
        {
            return;
        }

        json.WriteStartArray("pages");
        foreach (PageReport page in pages)
        {
            json.WriteStartObject();
            json.WriteNumber("page", page.Page);
            json.WriteNumber("rotation", page.Rotation);
            json.WriteNumber("sharpness", page.Sharpness);
            json.WriteNumber("contrast", page.Contrast);
            json.WriteNumber("staves", page.Staves);
            json.WriteNumber("noteheads", page.Noteheads);
            json.WriteNumber("barLines", page.BarLines);
            json.WriteNumber("spacePx", page.SpacePx);
            json.WriteNumber("measures", page.Measures);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>"sentFromMs" in "transcription": where in the recording file the stretch heard started; left out when not kept.</summary>
    private static void WriteSentFrom(Utf8JsonWriter json, TranscriptionOrigin transcription)
    {
        if (transcription.SentFromMilliseconds is { } from)
        {
            json.WriteNumber("sentFromMs", from);
        }
    }

    /// <summary>"meters": [{"at": 16th, "signature": "7/8"}] in a grid's "bars"; left out without a change of signature.</summary>
    private static void WriteMeters(Utf8JsonWriter json, BarGrid bars)
    {
        if (bars.Meters.Count == 0)
        {
            return;
        }

        json.WriteStartArray("meters");
        foreach (GridMeter meter in bars.Meters)
        {
            json.WriteStartObject();
            json.WriteNumber("at", meter.At);
            json.WriteString("signature", meter.Signature.ToString());
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>A scan's checked bars and page reports, with the engine, original and time its provenance names.</summary>
    internal static ScanOrigin ReadScanBlock(JsonElement scan, string engine, string? from, DateTimeOffset? at)
    {
        int[] bars = [.. Property(scan, "confirmedBars", JsonValueKind.Array)
            .EnumerateArray()
            .Select(bar => bar.ValueKind == JsonValueKind.Number && bar.TryGetInt32(out int value) && value >= 0 ? value : throw Damaged("checked bars"))];
        ScanOrigin read = new(engine, bars) { From = from, At = at, Pages = ReadScanPages(scan), Marks = ReadMarks(scan) };
        return scan.TryGetProperty("unread", out JsonElement unread) ? WithUnreadIn(read, unread, Property(scan, "read", JsonValueKind.Array)) : read;
    }

    /// <summary>
    /// "marks" in "scan" (version 18), none without it. A mark of a kind this build does not know, from a later one (whose
    /// file opens read-only), is left out; one this build knows that says what its kind cannot is damage.
    /// </summary>
    private static IReadOnlyList<ReadingMark> ReadMarks(JsonElement scan)
    {
        if (!scan.TryGetProperty("marks", out JsonElement marks))
        {
            return [];
        }

        if (marks.ValueKind != JsonValueKind.Array)
        {
            throw Damaged("reading marks");
        }

        List<ReadingMark> read = [];
        foreach (JsonElement mark in marks.EnumerateArray())
        {
            Expect(mark, JsonValueKind.Object, "reading marks");
            string kind = mark.TryGetProperty("kind", out JsonElement named) && named.ValueKind == JsonValueKind.String ? named.GetString()! : throw Damaged("reading marks");
            if (!Enum.TryParse(kind, ignoreCase: true, out ReadingMarkKind known) || JsonNamingPolicy.CamelCase.ConvertName(known.ToString()) != kind)
            {
                continue;
            }

            try
            {
                read.Add(new ReadingMark(known, Whole(mark, "bar"), Whole(mark, "last"), mark.TryGetProperty("staff", out _) ? Whole(mark, "staff") : null,
                    mark.TryGetProperty("value", out JsonElement value) ? value.ValueKind == JsonValueKind.String ? value.GetString() : throw Damaged("reading marks") : null));
            }
            catch (ArgumentException)
            {
                throw Damaged("reading marks");
            }
        }

        return ScanOrigin.Ordered(read);

        static int Whole(JsonElement mark, string name) =>
            mark.TryGetProperty(name, out JsonElement number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out int value) ? value : throw Damaged("reading marks");
    }

    /// <summary>"unread" and "read" in "scan" (version 18): the PDF's pages no engine read and those the notes have, each from 1.</summary>
    private static ScanOrigin WithUnreadIn(ScanOrigin scan, JsonElement unread, JsonElement read)
    {
        static int[] Pages(JsonElement pages) => pages.ValueKind == JsonValueKind.Array
            ? [.. pages.EnumerateArray().Select(page => page.ValueKind == JsonValueKind.Number && page.TryGetInt32(out int value) ? value : throw Damaged("unread pages"))]
            : throw Damaged("unread pages");

        try
        {
            return scan.WithUnread(Pages(unread), Pages(read));
        }
        catch (ArgumentException)
        {
            throw Damaged("unread pages");
        }
    }

    /// <summary>A transcription's bars, seams and stretch sent, with the engine, original and time its provenance names.</summary>
    internal static Import.Transcription.TranscriptionOrigin ReadHeard(JsonElement transcription, int version, string engine, string? from, DateTimeOffset? at)
    {
        if (string.IsNullOrWhiteSpace(engine))
        {
            throw Damaged("engine");
        }

        // Seams came within version 12 (0018, preparation item 12): a file without them was heard in one part, or before they were kept.
        long[] seams = [];
        if (transcription.TryGetProperty("seams", out JsonElement saved))
        {
            seams = saved.ValueKind == JsonValueKind.Array
                ? [.. saved.EnumerateArray().Select(seam => seam.ValueKind == JsonValueKind.Number && seam.TryGetInt64(out long value) && value > 0 ? value : throw Damaged("seams"))]
                : throw Damaged("seams");
        }

        try
        {
            return new Import.Transcription.TranscriptionOrigin(engine, version >= 10 ? ReadBars(transcription, version) : null) { Seams = seams, From = from, At = at, SentFromMilliseconds = SentFromIn(transcription) };
        }
        catch (ArgumentException)
        {
            throw Damaged("seams");
        }
    }

    /// <summary>The scan's page reports, read as the scan server's header is (<see cref="PageReport.Of"/>); none without "pages".</summary>
    private static IReadOnlyList<PageReport> ReadScanPages(JsonElement scan)
    {
        if (!scan.TryGetProperty("pages", out JsonElement pages))
        {
            return [];
        }

        try
        {
            return pages.ValueKind == JsonValueKind.Array ? PageReport.Of(pages) : throw Damaged("scan pages");
        }
        catch (FormatException)
        {
            throw Damaged("scan pages");
        }
    }

    private static BarGrid? ReadBars(JsonElement transcription, int version)
    {
        if (!transcription.TryGetProperty("bars", out JsonElement bars) || bars.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object))
        {
            throw Damaged("bars");
        }

        if (bars.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string[] signature = Text(bars, "signature").Split('/');
        TimeSignature? meter = signature.Length == 2 && int.TryParse(signature[0], NumberStyles.None, CultureInfo.InvariantCulture, out int numerator)
            && int.TryParse(signature[1], NumberStyles.None, CultureInfo.InvariantCulture, out int denominator)
            ? BarGrid.Signatures.FirstOrDefault(offered => offered.Numerator == numerator && offered.Denominator == denominator)
            : null;
        int[] starts = [.. Property(bars, "barStarts", JsonValueKind.Array)
            .EnumerateArray()
            .Select(start => start.ValueKind == JsonValueKind.Number && start.TryGetInt32(out int value) ? value : throw Damaged("bars"))];
        long origin = Long(bars, "origin");
        long[] beats = version >= 11
            ? [.. Property(bars, "beats", JsonValueKind.Array)
                .EnumerateArray()
                .Select(beat => beat.ValueKind == JsonValueKind.Number && beat.TryGetInt64(out long value) ? value : throw Damaged("bars"))]
            : [];
        int perBeat = version >= 11 ? Integer(bars, "stepsPerBeat") : 2;
        if (meter is not { } chosen || !BarGrid.Signatures.Contains(chosen) || origin < 0)
        {
            throw Damaged("bars");
        }

        try
        {
            return WithMetersIn(new BarGrid(new Tick(origin), Integer(bars, "sixteenthMicroseconds"), chosen, starts, beats, perBeat), bars);
        }
        catch (ArgumentException)
        {
            throw Damaged("bars");
        }
    }

    /// <summary>The grid with the changes of signature its "bars" keep; a change that is not well formed is damage.</summary>
    private static BarGrid WithMetersIn(BarGrid grid, JsonElement bars)
    {
        if (!bars.TryGetProperty("meters", out JsonElement meters))
        {
            return grid;
        }

        Expect(meters, JsonValueKind.Array, "bars");
        GridMeter[] read = [.. meters.EnumerateArray().Select(meter =>
        {
            Expect(meter, JsonValueKind.Object, "bars");
            return new GridMeter(Integer(meter, "at"), SignatureOf(Text(meter, "signature")) ?? throw Damaged("bars"));
        })];
        try
        {
            return grid with { Meters = read };
        }
        catch (ArgumentException)
        {
            throw Damaged("bars");
        }
    }

    /// <summary>A signature written as "7/8": any whole numbers a time signature can have; null otherwise.</summary>
    private static TimeSignature? SignatureOf(string text)
    {
        string[] parts = text.Split('/');
        if (parts.Length != 2
            || !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int numerator)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int denominator))
        {
            return null;
        }

        try
        {
            return new TimeSignature(numerator, denominator);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The "sentFromMs" of a "transcription", null when left out; a value that is not a whole number of 0 or more is damage.</summary>
    private static long? SentFromIn(JsonElement transcription) =>
        !transcription.TryGetProperty("sentFromMs", out JsonElement from) ? null
            : from.ValueKind == JsonValueKind.Number && from.TryGetInt64(out long value) && value >= 0 ? value
            : throw Damaged("sentFromMs");
}