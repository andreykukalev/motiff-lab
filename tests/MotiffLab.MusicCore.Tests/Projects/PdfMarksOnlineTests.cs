using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The marks on a PDF's pages online (version 0.4; Andrei, 2026-11-12, v0.4-data.md question 2): they go with the online
/// copy, the PDF never does; only a device that has the PDF writes them, the iPad when both have it, and another device
/// shows them as the online copy brings them.
/// </summary>
public class PdfMarksOnlineTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private const string CopyTitle = "{0} (previous copy)";

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name));

    private static InkStroke Stroke(string id, int page) => new(id, InkTool.Pen, InkColour.Graphite, 0.16, new Tick(page), 0.25, 3, [0, 0, 0.5, 1, 1, 0.5], 0.5);

    /// <summary>A piece read from its PDF on the iPad, with strokes on its pages, as the iPad keeps it.</summary>
    private static Project OnIpad(params InkStroke[] ink) =>
        Project.CreateDocument("p_pdfmarks", "book.pdf", s_pdf, s_now)
            .Recognised(Fixture("fur-elise-original.musicxml"), "homr 0.7.0", new PageMap([Tick.Zero]))
            .MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Ipad, held: true, s_now) with
        { DocumentMarks = DocumentMarks.Empty.WithInk(ink) };

    private static Project RoundTrip(Project project) => ProjectFile.Read(ProjectFile.Write(project));

    /// <summary>The same piece in the browser, its PDF linked again from the same file («Привязать оригинал»).</summary>
    private static Project LinkedInBrowser(Project online) =>
        online.RelinkDocument(s_pdf, new PageMap([Tick.Zero]))!.MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Web, held: true, s_now);

    [Fact]
    public void The_online_copy_carries_the_page_marks_and_never_the_pdf()
    {
        Project ipad = OnIpad() with { DocumentMarks = DocumentMarks.Empty.WithInk([Stroke("k1", 1)]).AddBookmark(1, "Coda", s_now) };
        Project online = RoundTrip(ipad.ForOnline()!);

        Assert.Null(online.Document);
        Assert.Null(online.DocumentPages);
        Assert.Equal(ipad.DocumentMarks, online.DocumentMarks);
        Assert.DoesNotContain("\"document\"", Encoding.UTF8.GetString(ProjectFile.Write(online)), StringComparison.Ordinal);

        // A piece that is only a PDF still stays on the device, marks and all.
        Assert.Null((Project.CreateDocument("p_only", "a.pdf", s_pdf, s_now) with { DocumentMarks = ipad.DocumentMarks }).ForOnline());
    }

    [Fact]
    public void Only_a_device_that_has_the_pdf_writes_the_marks_and_the_ipad_when_both_have_it()
    {
        Project ipad = OnIpad();
        Project online = RoundTrip(ipad.ForOnline()!);
        Project browser = LinkedInBrowser(online);

        Assert.True(ipad.WritesDocumentMarks(HoldingDevice.Ipad));
        Assert.True(ipad.WritesDocumentMarks(null));
        Assert.False(online.WritesDocumentMarks(HoldingDevice.Ipad));
        Assert.False(online.WritesDocumentMarks(HoldingDevice.Web));
        Assert.False(browser.WritesDocumentMarks(HoldingDevice.Web));

        // A PDF only this browser holds is this browser's to mark, as before.
        Project mine = browser.MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Ipad, held: false, s_now);
        Assert.True(mine.WritesDocumentMarks(HoldingDevice.Web));
    }

    [Fact]
    public void A_pull_keeps_the_writers_own_marks_and_brings_the_others_the_online_ones()
    {
        Project before = OnIpad(Stroke("k1", 1));
        Project browser = LinkedInBrowser(RoundTrip(before.ForOnline()!));
        Assert.Equal(before.DocumentMarks, browser.DocumentMarks);

        // The iPad writes another stroke and sends the piece: the browser, which only shows them, takes them.
        Project after = before with { DocumentMarks = before.DocumentMarks.WithInk([Stroke("k1", 1), Stroke("k2", 1)]) };
        Project online = RoundTrip(after.ForOnline()!);
        Project pulled = ProjectSync.Pulled(online, browser, HoldingDevice.Web);
        Assert.Equal(after.DocumentMarks, pulled.DocumentMarks);
        Assert.Equal(s_pdf, pulled.Document);
        Assert.False(pulled.WritesDocumentMarks(HoldingDevice.Web));

        // A device without the PDF keeps them too, to show once it links the PDF.
        Assert.Equal(after.DocumentMarks, ProjectSync.Pulled(online, RoundTrip(before.ForOnline()!), HoldingDevice.Web).DocumentMarks);

        // The iPad keeps its own, whatever the online copy says.
        Project stale = RoundTrip(before.ForOnline()!);
        Assert.Equal(after.DocumentMarks, ProjectSync.Pulled(stale, after, HoldingDevice.Ipad).DocumentMarks);
    }

    [Fact]
    public void An_online_copy_from_a_build_before_0_4_never_clears_the_marks()
    {
        // Builds 0.2 and 0.3 leave documentMarks out of the copy they send.
        Project ipad = OnIpad(Stroke("k1", 1));
        Project browser = LinkedInBrowser(RoundTrip(ipad.ForOnline()!));
        Project fromOldBuild = RoundTrip(ipad.ForOnline()! with { DocumentMarks = DocumentMarks.Empty });

        Assert.Equal(ipad.DocumentMarks, ProjectSync.Pulled(fromOldBuild, browser, HoldingDevice.Web).DocumentMarks);
        Assert.Equal(ipad.DocumentMarks, ProjectSync.Pulled(fromOldBuild, ipad, HoldingDevice.Ipad).DocumentMarks);
    }

    [Fact]
    public void A_browser_copy_that_wins_sends_the_marks_the_ipad_wrote_last()
    {
        Project before = OnIpad(Stroke("k1", 1));
        Project browser = LinkedInBrowser(RoundTrip(before.ForOnline()!)) with { Metadata = new PieceMetadata("Recital", null, [], []) };
        Project after = before with { DocumentMarks = before.DocumentMarks.WithInk([Stroke("k2", 1)]) };
        Project online = RoundTrip(after.ForOnline()!);

        SyncResolution resolved = ProjectSync.Resolve(browser, s_now.AddHours(2), online, s_now.AddHours(1), CopyTitle, "p_copy", HoldingDevice.Web);
        Assert.True(resolved.Upload);
        Assert.Equal("Recital", resolved.Kept.Metadata.Title);
        Assert.Equal(after.DocumentMarks, resolved.Kept.DocumentMarks);
        Assert.Equal(after.DocumentMarks, resolved.Kept.ForOnline()!.DocumentMarks);

        // The iPad's copy that wins sends its own.
        Project ipadNewer = after with { Metadata = new PieceMetadata("Exam", null, [], []) };
        SyncResolution ipad = ProjectSync.Resolve(ipadNewer, s_now.AddHours(2), RoundTrip(before.ForOnline()!), s_now.AddHours(1), CopyTitle, "p_copy", HoldingDevice.Ipad);
        Assert.Same(ipadNewer, ipad.Kept);
    }

    [Fact]
    public void The_ipad_sends_its_page_marks_through_the_facade_and_keeps_them_on_a_pull()
    {
        var facade = new MusicCoreFacade();
        string Text(Project project) => Encoding.UTF8.GetString(ProjectFile.Write(project));
        string File(string name, object request)
        {
            JsonElement answer = JsonDocument.Parse(facade.Call(0, name, JsonSerializer.Serialize(request))).RootElement;
            Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{name}: {error}");
            return answer.GetProperty("file").GetString()!;
        }

        Project ipad = OnIpad(Stroke("k1", 2));
        Project sent = ProjectFile.Read(Encoding.UTF8.GetBytes(File("project.online", new { file = Text(ipad), now = "2026-11-12T10:00:00Z" })));
        Assert.Null(sent.Document);
        Assert.Equal(ipad.DocumentMarks, sent.DocumentMarks);

        Project other = RoundTrip(ipad.ForOnline()! with { DocumentMarks = DocumentMarks.Empty.WithInk([Stroke("w1", 1)]) });
        Project back = ProjectFile.Read(Encoding.UTF8.GetBytes(File("project.pulled", new { file = Text(other), here = Text(ipad) })));
        Assert.Equal(ipad.DocumentMarks, back.DocumentMarks);
        Assert.Equal(s_pdf, back.Document);
    }
}