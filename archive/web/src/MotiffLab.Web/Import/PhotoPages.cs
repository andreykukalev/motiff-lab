using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Components.Forms;

using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.Web.Localization;

namespace MotiffLab.Web.Import;

/// <summary>
/// Photos of pages on the web (v0.5, plan section E; the 0.4 data note, row 6): several photos of one piece's pages picked
/// at once on Scan printed music, sent to the scan server one request at a time, as on the iPad, and their notes joined into
/// one score by music-core (<see cref="ScanJoin"/>), with the server's report on each page. The photos are not kept: the
/// piece is made from the notes read, as for one photo. Behind <see cref="Setting"/>, off until turned on.
/// </summary>
public static partial class PhotoPages
{
    /// <summary>The experiment's switch, the same key as on the iPad (one device setting each).</summary>
    public const string Setting = "experimental.photoPages";

    /// <summary>The most photos read at once: each takes 20 to 60 s on the scan server, so 20 pages is up to 20 minutes.</summary>
    public const int MaxPhotos = 20;

    /// <summary>True when the file name is a photo the scan server reads (PNG or JPEG), not a PDF.</summary>
    public static bool IsPhoto(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";

    /// <summary>
    /// The photos in page order: by name, with the numbers in names compared as numbers, so "page 2" comes before
    /// "page 10" and a camera's IMG_0009 before IMG_0010.
    /// </summary>
    public static IReadOnlyList<IBrowserFile> InOrder(IEnumerable<IBrowserFile> photos) =>
        [.. photos.OrderBy(photo => SortKey(photo.Name), StringComparer.OrdinalIgnoreCase).ThenBy(photo => photo.Name, StringComparer.Ordinal)];

    /// <summary>
    /// Sends each photo to the scan server, in order, and joins the notes of the pages read into one score: its MusicXML,
    /// the engine, the pages read and their reports (one per page read, or none when a page came without one); the pages
    /// not read are left out and told in <see cref="PhotoPagesRead.LeftOut"/>. <paramref name="reading"/> hears the page
    /// about to be sent, from 1.
    /// </summary>
    public static async Task<PhotoPagesRead> ReadAsync(ScanService scans, IReadOnlyList<IBrowserFile> photos, UiText text, Action<int> reading)
    {
        ArgumentNullException.ThrowIfNull(scans);
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(reading);
        List<byte[]> pages = [];
        List<PageReport> reports = [];
        bool reported = true;
        List<LeftOutPage> leftOut = [];
        string engine = "";
        for (int index = 0; index < photos.Count; index++)
        {
            reading(index + 1);
            ScanResult result = await ReadOneAsync(scans, photos[index], text);
            if (result.MusicXml is not { } musicXml)
            {
                leftOut.Add(new LeftOutPage(index + 1, photos[index].Name, result.Problem ?? ""));
                continue;
            }

            pages.Add(musicXml);
            engine = result.Engine;
            reported &= result.Reports.Count == 1;
            if (result.Reports.Count == 1)
            {
                reports.Add(result.Reports[0]);
            }
        }

        if (pages.Count == 0)
        {
            return new PhotoPagesRead(ScanResult.Failed(leftOut[0].Problem), leftOut);
        }

        try
        {
            ScanResult joined = new(ScanJoin.Join(pages), engine, pages.Count, null) { Reports = reported ? reports : [] };
            return new PhotoPagesRead(joined, leftOut);
        }
        catch (ArgumentException)
        {
            return new PhotoPagesRead(ScanResult.Failed(text["import.scanPagesNotJoined"]), leftOut);
        }
    }

    private static async Task<ScanResult> ReadOneAsync(ScanService scans, IBrowserFile photo, UiText text)
    {
        if (photo.Size == 0)
        {
            return ScanResult.Failed(text["transcribe.empty"]);
        }

        if (photo.Size > ScanService.MaxUploadBytes)
        {
            return ScanResult.Failed(text["import.scanTooLarge"]);
        }

        // One photo in memory at a time: twenty of them would not fit a tablet's.
        await using Stream stream = photo.OpenReadStream(ScanService.MaxUploadBytes);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return await scans.RecogniseAsync(photo.Name, memory.ToArray());
    }

    /// <summary>The name with each run of digits padded to ten, so names sort by their numbers.</summary>
    private static string SortKey(string name) => Digits().Replace(name, match => match.Value.PadLeft(10, '0'));

    [GeneratedRegex("[0-9]+")]
    private static partial Regex Digits();
}

/// <summary>A photo whose page could not be read: its place among the photos picked, from 1, its name and why.</summary>
public sealed record LeftOutPage(int Page, string Name, string Problem);

/// <summary>The notes of the photos read, joined (or why there are none), and the pages left out.</summary>
public sealed record PhotoPagesRead(ScanResult Result, IReadOnlyList<LeftOutPage> LeftOut);