using Microsoft.JSInterop;

using MotiffLab.MusicCore.Import.Scan;
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

    /// <summary>The module, loaded once for the app's life (the service lives as long as the page).</summary>
    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/import/pdf-parts.js");
}