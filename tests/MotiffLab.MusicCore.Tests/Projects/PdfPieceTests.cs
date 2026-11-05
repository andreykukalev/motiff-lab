using System.Text;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// A PDF added to read as it is (0023; Andrei, 2026-11-04): kept in the project file exactly as picked, with no notes until it
/// is recognised, and refused when it is not a PDF or too large.
/// </summary>
public class PdfPieceTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    [Fact]
    public void A_pdf_piece_keeps_the_document_and_has_no_notes()
    {
        Project project = Project.CreateDocument("p_pdf", "Nocturne.pdf", s_pdf, s_now);

        Assert.False(project.HasNotes);
        Assert.Equal(s_pdf, project.Document);
        Assert.Equal(PieceOrigin.Pdf, project.Summary.Origin);
    }

    [Fact]
    public void The_document_survives_the_project_file_byte_for_byte()
    {
        Project project = Project.CreateDocument("p_pdf", "Nocturne.pdf", s_pdf, s_now);

        Project read = ProjectFile.Read(ProjectFile.Write(project));

        Assert.Equal(s_pdf, read.Document);
        Assert.Empty(read.Source);
        Assert.False(read.HasNotes);
        Assert.Equal("Nocturne.pdf", read.FileName);
    }

    [Fact]
    public void A_piece_of_notes_writes_no_document()
    {
        Project project = Project.Create("p_mid", "a.mid", [1, 2, 3], s_now);

        string json = Encoding.UTF8.GetString(ProjectFile.Write(project));

        Assert.DoesNotContain("\"document\"", json, StringComparison.Ordinal);
        Assert.Null(ProjectFile.Read(ProjectFile.Write(project)).Document);
    }

    [Fact]
    public void Bytes_that_are_not_a_pdf_are_refused()
    {
        Assert.False(PdfDocument.IsPdf("PK\u0003\u0004"u8));
        Assert.Throws<ArgumentException>(() => Project.CreateDocument("p", "a.pdf", [1, 2, 3], s_now));
    }

    [Fact]
    public void A_damaged_document_or_one_with_neither_notes_nor_pdf_is_refused()
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Project.CreateDocument("p_pdf", "a.pdf", s_pdf, s_now)));
        string notPdf = Convert.ToBase64String("hello"u8.ToArray());

        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json.Replace(Convert.ToBase64String(s_pdf), notPdf, StringComparison.Ordinal))));
        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json.Replace($"\"{Convert.ToBase64String(s_pdf)}\"", "12", StringComparison.Ordinal))));

        string empty = Encoding.UTF8.GetString(ProjectFile.Write(Project.Create("p", "a.mid", [], s_now)));
        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(empty)));
    }

    [Fact]
    public void A_pdf_piece_has_no_notes_to_open()
    {
        Project project = Project.CreateDocument("p_pdf", "a.pdf", s_pdf, s_now);

        Assert.Throws<ProjectFileException>(() => OpenedProject.Open(project));
    }

    [Fact]
    public void A_pdf_too_large_or_empty_is_refused_before_it_is_read()
    {
        Assert.Null(PdfDocument.CheckSize(3 * 1024 * 1024));
        Assert.Equal(ImportRejectionReason.TooLarge, PdfDocument.CheckSize(PdfDocument.MaxFileBytes + 1L)?.Reason);
        Assert.NotNull(PdfDocument.CheckSize(0));
        Assert.Contains("50", PdfDocument.CheckSize(PdfDocument.MaxFileBytes + 1L)!.Text(Words.Russian), StringComparison.Ordinal);
    }

    private static InkStroke Stroke(string id, int page) =>
        new(id, InkTool.Pen, InkColour.Carmine, 0.16, new Tick(page), 0.25, 30.5, [0.5, 0.25, 0.5, 1.0, 0.5, 0.6], 0.5);

    [Fact]
    public void Marks_on_the_pages_survive_the_project_file_apart_from_the_notes_marks()
    {
        DocumentMarks marks = DocumentMarks.Empty
            .WithInk([Stroke("k1", 1), Stroke("k2", 3)])
            .AddBookmark(3, "  Coda  ", s_now)
            .AddBookmark(1, null, s_now);
        Project project = Project.CreateDocument("p_pdf", "a.pdf", s_pdf, s_now) with { DocumentMarks = marks };

        Project read = ProjectFile.Read(ProjectFile.Write(project));

        Assert.Equal(marks, read.DocumentMarks);
        Assert.Equal([1, 3], read.DocumentMarks.Bookmarks.Select(bookmark => bookmark.Page));
        Assert.Equal("Coda", read.DocumentMarks.BookmarkOn(3)!.Name);
        Assert.Empty(read.Marks.Ink);
        Assert.DoesNotContain("documentMarks", Encoding.UTF8.GetString(ProjectFile.Write(Project.CreateDocument("p", "a.pdf", s_pdf, s_now))), StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_has_one_bookmark_which_can_be_named_and_removed()
    {
        DocumentMarks marks = DocumentMarks.Empty.AddBookmark(2, null, s_now);
        Assert.Same(marks, marks.AddBookmark(2, "again", s_now));

        PageBookmark bookmark = marks.BookmarkOn(2)!;
        Assert.Equal("Development", marks.RenameBookmark(bookmark.Id, " Development ").BookmarkOn(2)!.Name);
        Assert.Null(marks.RenameBookmark(bookmark.Id, "  ").BookmarkOn(2)!.Name);
        Assert.Equal(PageBookmark.MaxName, marks.RenameBookmark(bookmark.Id, new string('a', 200)).BookmarkOn(2)!.Name!.Length);
        Assert.True(marks.RemoveBookmark(bookmark.Id).IsEmpty);
        Assert.Equal("pb_0002", marks.AddBookmark(5, null, s_now).BookmarkOn(5)!.Id);
    }

    [Fact]
    public void A_stroke_must_name_a_page_and_damaged_page_marks_are_refused()
    {
        Assert.Throws<ArgumentException>(() => DocumentMarks.Empty.WithInk([Stroke("k1", 0)]));
        Assert.Throws<ArgumentException>(() => DocumentMarks.Empty.WithInk([Stroke("k1", 1), Stroke("k1", 2)]));

        Project project = Project.CreateDocument("p_pdf", "a.pdf", s_pdf, s_now) with { DocumentMarks = DocumentMarks.Empty.AddBookmark(2, null, s_now) };
        string json = Encoding.UTF8.GetString(ProjectFile.Write(project));
        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json.Replace("\"page\": 2", "\"page\": 0", StringComparison.Ordinal))));
    }

    private static byte[] Scanned() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));

    private static ImportedPiece Piece(byte[] musicXml) => PieceImporter.Import("a.musicxml", musicXml).Piece!;

    [Fact]
    public void Recognised_notes_become_a_separate_piece_and_the_pdf_piece_stays_as_it_was()
    {
        DocumentMarks marks = DocumentMarks.Empty.AddBookmark(2, "Trio", s_now);
        Project pdf = Project.CreateDocument("p_pdf", "Nocturne.pdf", s_pdf, s_now) with { DocumentMarks = marks };
        byte[] before = ProjectFile.Write(pdf);
        byte[] musicXml = Scanned();

        PageMap pages = PageMap.Of(Piece(musicXml), 2);
        Project notes = Project.ReadFromDocument("p_notes", pdf, musicXml, "homr 0.7.0", pages, s_now);

        Assert.Equal("p_notes", notes.Id);
        Assert.True(notes.HasNotes);
        Assert.Null(notes.Document);
        Assert.Equal("Nocturne.musicxml", notes.FileName);
        Assert.Equal(PieceOrigin.Scan, notes.Summary.Origin);
        Assert.Equal("homr 0.7.0", notes.Scan!.Engine);
        Assert.Equal(DocumentMarks.Empty, notes.DocumentMarks);
        Assert.NotEmpty(OpenedProject.Open(notes).Piece.Notes);
        Assert.Equal(before, ProjectFile.Write(pdf));
        Assert.False(pdf.HasNotes);
        Assert.Throws<InvalidOperationException>(() => Project.ReadFromDocument("p", Project.Create("p_mid", "a.mid", [1, 2, 3], s_now), musicXml, "homr", pages, s_now));
        Assert.Throws<ArgumentException>(() => Project.ReadFromDocument("p", pdf, [], "homr", pages, s_now));

        // The notes remember the PDF piece and where its pages start, for PDF | Notes; the PDF piece is found from them.
        Assert.Equal(new ReadFromPdf("p_pdf", pages), notes.ReadFrom);
        Assert.Equal("p_pdf", notes.Summary.ReadFrom);
        Assert.Null(pdf.Summary.ReadFrom);
        Project read = ProjectFile.Read(ProjectFile.Write(notes));
        Assert.Equal(musicXml, read.Source);
        Assert.Equal(notes.ReadFrom, read.ReadFrom);
        Assert.Equal(ProjectFile.Write(notes), ProjectFile.Write(read));

        string json = Encoding.UTF8.GetString(ProjectFile.Write(notes));
        Assert.Contains("\"readFrom\":{\"project\":\"p_pdf\",\"pages\":[0,", json.Replace(" ", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json.Replace("\"p_pdf\"", "\"\"", StringComparison.Ordinal))));
        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json.Replace("\"pages\"", "\"pagez\"", StringComparison.Ordinal))));
    }

    [Fact]
    public void A_pdf_recognised_into_the_same_project_before_still_opens_with_its_pages()
    {
        byte[] musicXml = Scanned();
        PageMap pages = PageMap.Of(Piece(musicXml), 2);
        Project recognised = Project.CreateDocument("p_pdf", "Nocturne.pdf", s_pdf, s_now) with
        {
            FileName = "Nocturne.musicxml",
            Source = musicXml,
            Scan = ScanOrigin.Read("homr 0.7.0"),
            DocumentPages = pages,
        };

        Project read = ProjectFile.Read(ProjectFile.Write(recognised));
        Assert.Equal(pages, read.DocumentPages);
        Assert.Equal(s_pdf, read.Document);
        Assert.Equal(musicXml, read.Source);
        Assert.Equal("Nocturne.pdf", read.DocumentFileName);
        Assert.NotEmpty(OpenedProject.Open(read).Piece.Notes);
    }

    [Fact]
    public void Pages_start_where_the_scan_marked_them_or_else_share_the_bars_evenly()
    {
        byte[] plain = Scanned();
        ImportedPiece piece = Piece(plain);
        IReadOnlyList<Tick> bars = [.. piece.MusicXml!.Measures.Select(measure => measure.Span.Onset)];

        // No marks: 32 bars over 2 pages, 16 to a page.
        PageMap even = PageMap.Of(piece, 2);
        Assert.Equal([Tick.Zero, bars[16]], even.Starts);
        Assert.Equal(1, even.PageOf(bars[15]));
        Assert.Equal(2, even.PageOf(bars[16]));
        Assert.Equal(bars[16], even.StartOf(2));
        Assert.Equal(bars[16], even.StartOf(9));
        Assert.Single(PageMap.Of(piece, 1).Starts);

        // The scan server marks the first bar of page 2 (services/omr/app/merge.py).
        string text = Encoding.UTF8.GetString(plain);
        int at = text.IndexOf("<measure number=\"10\">", StringComparison.Ordinal);
        int end = text.IndexOf('>', at) + 1;
        byte[] marked = Encoding.UTF8.GetBytes(text.Insert(end, "<print new-page=\"yes\"/>"));
        PageMap map = PageMap.Of(Piece(marked), 2);
        Assert.Equal([Tick.Zero, bars[9]], map.Starts);

        // Marks that do not match the PDF's pages are not trusted.
        Assert.Equal(3, PageMap.Of(Piece(marked), 3).Pages);
        Assert.Equal(bars[10], PageMap.Of(Piece(marked), 3).StartOf(2));
    }

    [Fact]
    public void A_page_map_starts_at_the_beginning_and_runs_forward_and_a_damaged_one_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new PageMap([]));
        Assert.Throws<ArgumentException>(() => new PageMap([new Tick(480)]));
        Assert.Throws<ArgumentException>(() => new PageMap([Tick.Zero, new Tick(960), new Tick(960)]));

        byte[] musicXml = Scanned();
        Project recognised = Project.CreateDocument("p_pdf", "a.pdf", s_pdf, s_now) with
        {
            FileName = "a.musicxml",
            Source = musicXml,
            Scan = ScanOrigin.Read("homr 0.7.0"),
            DocumentPages = new PageMap([Tick.Zero, new Tick(15360)]),
        };
        string json = Encoding.UTF8.GetString(ProjectFile.Write(recognised));
        Assert.Contains("\"documentPages\"", json, StringComparison.Ordinal);
        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json.Replace("15360", "-1", StringComparison.Ordinal))));
        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json.Replace("15360", "0", StringComparison.Ordinal))));
    }
}