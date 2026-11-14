using System.Text;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The library's source icons (v0.5.6, #533 section 10): only the sources a piece really has, from its summary, in a fixed
/// order (PDF, notes, recording, then the others), each once.
/// </summary>
public class LibrarySourcesTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private static OriginalSummary Original(string id, OriginalKind kind) => new(id, kind, OriginalKept.File, true, []);

    private static ProjectSummary Summary(PieceOrigin origin, params OriginalSummary[] originals) =>
        new("p_1", "Nocturne.pdf", 0, 0, s_now) { Origin = origin, Originals = originals };

    [Fact]
    public void A_pdf_only_shows_the_pdf()
    {
        Assert.Equal([LibrarySource.Pdf], LibrarySources.Of(Project.CreateDocument("p_pdf", "Nocturne.pdf", s_pdf, s_now).Summary));
    }

    [Fact]
    public void A_pdf_with_its_notes_read_shows_the_pdf_then_the_notes()
    {
        ProjectSummary summary = Summary(PieceOrigin.Scan, Original(PieceOriginal.DocumentId, OriginalKind.Pdf));

        Assert.Equal([LibrarySource.Pdf, LibrarySource.Notes], LibrarySources.Of(summary));
    }

    [Fact]
    public void A_recording_kept_as_a_version_comes_after_the_pdf_and_the_notes_whatever_the_order_kept()
    {
        ProjectSummary summary = Summary(PieceOrigin.Scan, Original("v_1", OriginalKind.Audio), Original(PieceOriginal.DocumentId, OriginalKind.Pdf), Original("v_2", OriginalKind.Audio));

        Assert.Equal([LibrarySource.Pdf, LibrarySource.Notes, LibrarySource.Recording], LibrarySources.Of(summary));
    }

    [Fact]
    public void A_file_of_notes_is_the_notes_and_another_midi_file_is_a_version_of_its_own()
    {
        Assert.Equal([LibrarySource.Notes], LibrarySources.Of(Summary(PieceOrigin.File, Original(PieceOriginal.SourceFileId, OriginalKind.Midi))));
        Assert.Equal([LibrarySource.Notes], LibrarySources.Of(Summary(PieceOrigin.File, Original(PieceOriginal.SourceFileId, OriginalKind.MusicXml))));
        Assert.Equal(
            [LibrarySource.Notes, LibrarySource.Midi],
            LibrarySources.Of(Summary(PieceOrigin.File, Original(PieceOriginal.SourceFileId, OriginalKind.MusicXml), Original("v_1", OriginalKind.Midi))));
    }

    [Fact]
    public void Photos_of_pages_show_as_photos_and_a_heard_recording_keeps_no_sound()
    {
        Assert.Equal([LibrarySource.Photos], LibrarySources.Of(Summary(PieceOrigin.Pdf, Original(PieceOriginal.DocumentId, OriginalKind.Images))));
        Assert.Equal([LibrarySource.Notes], LibrarySources.Of(Summary(PieceOrigin.Recording)));
    }

    [Fact]
    public void Notes_read_from_a_pdf_piece_show_its_pdf_too()
    {
        ProjectSummary summary = Summary(PieceOrigin.Scan) with { ReadFrom = "p_pdf" };

        Assert.Equal([LibrarySource.Pdf, LibrarySource.Notes], LibrarySources.Of(summary));
    }
}