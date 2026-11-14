using Microsoft.JSInterop;

using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Vector;
using MotiffLab.Web.Import;
using MotiffLab.Web.Localization;

namespace MotiffLab.Web.Pdf;

/// <summary>
/// «Распознать ноты» on one score (0011, 0023; v0.5, issue #479 item 16; v0.5.6, #533 section 1): a PDF of one page goes
/// whole, as the player added it; a longer one, up to <see cref="ScanParts.MostPages"/> pages, goes a page a request
/// (<see cref="ScanParts.PagesARequest"/>), each page a PDF of its own cut out by pdf.js (<c>Scripts/import/pdf-parts.ts</c>),
/// sent one after another to the same server, and music-core joins what they read (<see cref="ScanJoin"/>), so the pages
/// start in the notes where they would have. The progress is the pages' real answers (<see cref="PageProgress"/>). Only the
/// PDF's pages go, only to the server set in Settings; the server is not changed, and an older one reads a page as well.
/// </summary>
public sealed class ScoreScan(IJSRuntime js, ScanService scans, UiText text)
{
    private IJSObjectReference? _module;

    /// <summary>The notes read from the PDF, or why there are none; <paramref name="progress"/> hears as each page is sent and read.</summary>
    public async Task<ScanResult> ReadAsync(string fileName, byte[] pdf, Action<PageProgress> progress)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(progress);
        if (await scans.EngineChoiceAsync() == ScanService.Auto && await VectorAsync(pdf) is { } read)
        {
            return await AutoAsync(fileName, pdf, read, progress);
        }

        int pages = await (await ModuleAsync()).InvokeAsync<int>("openParts", pdf);
        try
        {
            if (pages > ScanParts.MostPages)
            {
                return ScanResult.Failed(text.Format("scan.tooManyPages", pages, ScanParts.MostPages));
            }

            // A PDF of one page, or one this browser cannot read, goes whole, as before: the server says what is wrong with it.
            if (pages <= 1)
            {
                progress(new PageProgress(1));
                return await scans.RecogniseAsync(fileName, pdf);
            }

            return await PagesAsync(fileName, pages, progress);
        }
        finally
        {
            await (await ModuleAsync()).InvokeVoidAsync("closeParts");
        }
    }

    private async Task<ScanResult> PagesAsync(string fileName, int pages, Action<PageProgress> progress)
    {
        List<byte[]> read = [];
        List<PageReport> reports = [];
        bool reported = true;
        string engine = "";
        string name = Path.GetFileNameWithoutExtension(fileName);
        var state = new PageProgress(pages);
        foreach (PageRange range in ScanParts.Plan(pages, ScanParts.PagesARequest))
        {
            progress(state);
            if (await (await ModuleAsync()).InvokeAsync<byte[]?>("partOf", range.First, range.Last) is not { } bytes)
            {
                return ScanResult.Failed(text["pdf.unreadable"]);
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            ScanResult result = await scans.RecogniseAsync($"{name} {range.First}.pdf", bytes);
            if (result.MusicXml is not { } musicXml)
            {
                return result with { Problem = text.Format("scan.pageFailed", range.First, result.Problem ?? text["pdf.readNone"]) };
            }

            state = state.Read(watch.ElapsedMilliseconds);
            read.Add(musicXml);
            engine = result.Engine;
            reported &= result.Reports.Count == range.Count;
            reports.AddRange(result.Reports);
        }

        progress(state);
        try
        {
            return new ScanResult(ScanJoin.Join(read), engine, pages, null) { Reports = reported ? reports : [] };
        }
        catch (ArgumentException)
        {
            return ScanResult.Failed(text["import.scanPagesNotJoined"]);
        }
    }

    /// <summary>The PDF's pages as the vector reader reads them on this device; null when it cannot open the file.</summary>
    private static async Task<IReadOnlyList<VectorPageReading>?> VectorAsync(byte[] pdf)
    {
        // Let the page draw its progress before the reading, which runs on this thread.
        await Task.Yield();
        try
        {
            return VectorReader.Read(pdf);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// «Авто» (decision record 0029): the pages read on this device are kept as read; the others go to the server one a request,
    /// with its own engine; a page with no music is left out. The pages are joined in their order, and the engines that read
    /// them are named together ("vector 1 + homr 0.7.1").
    /// </summary>
    private async Task<ScanResult> AutoAsync(string fileName, byte[] pdf, IReadOnlyList<VectorPageReading> read, Action<PageProgress> progress)
    {
        List<VectorPageReading> toServer = [.. read.Where(page => page.Kind == VectorPageKind.Scan)];
        var musicXml = new SortedDictionary<int, (byte[] Score, PageReport? Report)>();
        foreach (VectorPageReading page in read.Where(page => page.MusicXml is not null))
        {
            musicXml[page.Number] = (page.MusicXml!, page.Report);
        }

        var engines = new List<string>();
        if (musicXml.Count > 0)
        {
            engines.Add(VectorReader.EngineAndVersion);
        }

        if (toServer.Count > 0)
        {
            (ScanResult? failed, IReadOnlyList<string> serverEngines) = await ServerPagesAsync(fileName, pdf, toServer, musicXml, progress);
            if (failed is not null)
            {
                return failed;
            }

            engines.AddRange(serverEngines);
        }

        if (musicXml.Count == 0)
        {
            return ScanResult.Failed(text["pdf.readNone"]);
        }

        List<PageReport?> reports = [.. musicXml.Values.Select(page => page.Report)];
        try
        {
            return new ScanResult(ScanJoin.Join([.. musicXml.Values.Select(page => page.Score)]), string.Join(" + ", engines.Distinct()), read.Count, null)
            {
                Reports = reports.All(report => report is not null) ? [.. reports.Select(report => report!)] : [],
            };
        }
        catch (ArgumentException)
        {
            return ScanResult.Failed(text["import.scanPagesNotJoined"]);
        }
    }

    /// <summary>Sends the pages the device could not read to the server, one a request.</summary>
    /// <returns>Why not, when a page did not come back; the engines that read them.</returns>
    private async Task<(ScanResult? Failed, IReadOnlyList<string> Engines)> ServerPagesAsync(string fileName, byte[] pdf, List<VectorPageReading> pages, SortedDictionary<int, (byte[] Score, PageReport? Report)> read, Action<PageProgress> progress)
    {
        var engines = new List<string>();
        int count = await (await ModuleAsync()).InvokeAsync<int>("openParts", pdf);
        try
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            var state = new PageProgress(pages.Count);
            foreach (VectorPageReading page in pages)
            {
                progress(state);
                byte[]? bytes = count <= 1 ? pdf : await (await ModuleAsync()).InvokeAsync<byte[]?>("partOf", page.Number, page.Number);
                if (bytes is null)
                {
                    return (ScanResult.Failed(text["pdf.unreadable"]), engines);
                }

                var watch = System.Diagnostics.Stopwatch.StartNew();
                ScanResult result = await scans.RecogniseAsync($"{name} {page.Number}.pdf", bytes);
                if (result.MusicXml is not { } musicXml)
                {
                    return (result with { Problem = text.Format("scan.pageFailed", page.Number, result.Problem ?? text["pdf.readNone"]) }, engines);
                }

                state = state.Read(watch.ElapsedMilliseconds);
                read[page.Number] = (musicXml, result.Reports.Count == 1 ? result.Reports[0] : null);
                engines.Add(result.Engine);
            }

            progress(state);
            return (null, engines);
        }
        finally
        {
            await (await ModuleAsync()).InvokeVoidAsync("closeParts");
        }
    }

    /// <summary>The module, loaded once for the app's life (the service lives as long as the page).</summary>
    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/import/pdf-parts.js");
}