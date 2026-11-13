using Microsoft.JSInterop;

using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.Web.Import;
using MotiffLab.Web.Library;
using MotiffLab.Web.Localization;

namespace MotiffLab.Web.Pdf;

/// <summary>
/// How far the notes of one score are read (v0.5, issue #479 item 10): its pages, the parts it goes in, the part being read
/// and since when. The time is a rough guess, half a minute a page as the scan server reads (<see cref="HostedPages"/>), so
/// the bar moves while a part is read and never claims a part done before its notes are back.
/// </summary>
public sealed record ScanProgress(int Pages, IReadOnlyList<PageRange> Parts, int Part, DateTimeOffset Sent)
{
    /// <summary>The pages of the parts already read.</summary>
    public int PagesDone => Parts.Take(Part - 1).Sum(static part => part.Count);

    /// <summary>The share done by now, in percent: the parts read and most of the part being read, never 100.</summary>
    public int Percent(DateTimeOffset now) => Math.Clamp((int)(SecondsDone(now) * 100 / (Math.Max(1, Pages) * HostedPages.SecondsPerPage)), 0, 99);

    /// <summary>About how many minutes are left, at least one.</summary>
    public int MinutesLeft(DateTimeOffset now) => Math.Max(1, (int)Math.Ceiling(((Pages * HostedPages.SecondsPerPage) - SecondsDone(now)) / 60));

    /// <summary>"Part 2 of 3 · pages 5–8", or "6 pages" for a PDF read in one request.</summary>
    public string Line(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        PageRange part = Parts[Part - 1];
        return Parts.Count > 1 ? text.Format("scan.progressPart", Part, Parts.Count, part.First, part.Last) : text.Count("recognition.pages", Pages);
    }

    private double SecondsDone(DateTimeOffset now)
    {
        double reading = Math.Max(0, (now - Sent).TotalSeconds);
        return (PagesDone * HostedPages.SecondsPerPage) + Math.Min(reading, Parts[Part - 1].Count * HostedPages.SecondsPerPage * 0.95);
    }
}

/// <summary>
/// «Распознать ноты» on one score (0011, 0023; v0.5, issue #479 item 16): a PDF the scan server reads in one request goes
/// whole, as the player added it; a longer one, up to <see cref="ScanParts.MostPages"/> pages, goes in consecutive parts of
/// as many pages as the server reads at a time (<see cref="ScanService.MaxPagesAsync"/>), each part a PDF of those pages
/// cut out by pdf.js (<c>Scripts/import/pdf-parts.ts</c>), sent one after another to the same server, and music-core joins
/// what they read (<see cref="ScanJoin"/>), so the pages start in the notes where they would have. Only the PDF's pages go,
/// only to the server set in Settings; the server is not changed.
/// </summary>
public sealed class ScoreScan(IJSRuntime js, ScanService scans, UiText text)
{
    private IJSObjectReference? _module;

    /// <summary>The notes read from the PDF, or why there are none; <paramref name="progress"/> hears as each part is sent.</summary>
    public async Task<ScanResult> ReadAsync(string fileName, byte[] pdf, Action<ScanProgress> progress)
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

            // A PDF this browser cannot read goes whole, as before: the server says what is wrong with it.
            int most = pages > ScanParts.SafePages ? await scans.MaxPagesAsync() : ScanParts.SafePages;
            IReadOnlyList<PageRange> plan = ScanParts.Plan(Math.Max(1, pages), most);
            if (plan.Count == 1)
            {
                progress(new ScanProgress(Math.Max(1, pages), plan, 1, DateTimeOffset.Now));
                return await scans.RecogniseAsync(fileName, pdf);
            }

            return await PartsAsync(fileName, pages, plan, progress);
        }
        finally
        {
            await (await ModuleAsync()).InvokeVoidAsync("closeParts");
        }
    }

    private async Task<ScanResult> PartsAsync(string fileName, int pages, IReadOnlyList<PageRange> plan, Action<ScanProgress> progress)
    {
        List<byte[]> read = [];
        List<PageReport> reports = [];
        bool reported = true;
        string engine = "";
        string name = Path.GetFileNameWithoutExtension(fileName);
        for (int part = 1; part <= plan.Count; part++)
        {
            PageRange range = plan[part - 1];
            progress(new ScanProgress(pages, plan, part, DateTimeOffset.Now));
            if (await (await ModuleAsync()).InvokeAsync<byte[]?>("partOf", range.First, range.Last) is not { } bytes)
            {
                return ScanResult.Failed(text["pdf.unreadable"]);
            }

            ScanResult result = await scans.RecogniseAsync($"{name} {range.First}-{range.Last}.pdf", bytes);
            if (result.MusicXml is not { } musicXml)
            {
                return result with { Problem = text.Format("scan.partFailed", range.First, range.Last, result.Problem ?? text["pdf.readNone"]) };
            }

            read.Add(musicXml);
            engine = result.Engine;
            reported &= result.Reports.Count == range.Count;
            reports.AddRange(result.Reports);
        }

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