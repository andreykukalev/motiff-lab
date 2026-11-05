using System.Text;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;

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
}