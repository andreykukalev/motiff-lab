using System.Text;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

namespace MotiffLab.MusicCore.Tests.Facade.Sessions;

/// <summary>
/// The sources session typed (audit W1.7): the drafts kept for drafts.undo, the PDF kept in the piece and the notes read
/// from it, and the iPad's mark, with no request and no JSON. What the iPad reads of the same calls is pinned by
/// tests/contracts/facade-answers (FacadeAnswersContractTests).
/// </summary>
public class SourcesSessionTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private static OpenPiece Example() => OpenPiece.Import(
        "p_session",
        "FurElise.musicxml",
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml")),
        Words.English,
        s_now);

    private static byte[] Scanned() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));

    [Fact]
    public void A_draft_kept_before_a_change_of_its_structure_is_taken_back_once_and_forgotten_when_it_changes_otherwise()
    {
        var session = new SourcesSession();
        var before = new Draft("d_1", "Coda", s_now, [], [], [], Next: 2);
        var now = new Draft("d_1", "Placed", s_now, [], [], [], Next: 4);

        session.KeepDraftBefore(before);
        Assert.Equal(before with { Next = 4 }, session.TakeDraftBefore(now));
        Assert.Null(session.TakeDraftBefore(now));

        session.KeepDraftBefore(before);
        session.ForgetDraftsBefore("d_1");
        Assert.Null(session.TakeDraftBefore(now));
    }

    [Fact]
    public void The_pdf_kept_in_the_piece_shares_its_pages_over_the_bars_until_the_notes_read_from_it_link_them()
    {
        SessionPiece piece = Example().State;
        Assert.Equal("unavailable", Assert.Throws<FacadeException>(() => SourcesSession.Document(piece)).Code);
        Assert.Equal("unavailable", Assert.Throws<FacadeException>(() => SourcesSession.RefuseWithoutDocument(piece)).Code);

        SessionPiece kept = piece with { Project = SourcesSession.WithDocument(piece, s_pdf, pages: 2) };
        Assert.Equal(2, kept.Project.DocumentPages!.Pages);
        Assert.Equal(s_pdf, SourcesSession.Document(kept).Bytes);
        Assert.Equal("badRequest", Assert.Throws<FacadeException>(() => SourcesSession.WithDocument(piece, [1, 2, 3], pages: 1)).Code);

        DocumentVersion read = SourcesSession.ReadDocument(kept, Scanned());
        Assert.NotEmpty(read.Midi);
        Assert.Equal(1, SourcesSession.WithPagesLinked(kept, read.Read, pages: 1).DocumentPages!.Pages);
        Assert.Equal(Path.GetFileNameWithoutExtension(kept.Project.DocumentFileName), SourcesSession.VersionName(kept, ""));
        Assert.Equal("Henle", SourcesSession.VersionName(kept, "Henle"));
        Assert.Equal("import", Assert.Throws<FacadeException>(() => SourcesSession.Read("notes.musicxml", "plain text"u8.ToArray(), Words.English)).Code);
    }

    [Fact]
    public void Only_the_ipad_mark_on_a_known_original_changes_and_only_a_pdf_on_another_device_links_again()
    {
        SessionPiece piece = Example().State;
        Assert.Equal("unavailable", Assert.Throws<FacadeException>(() => SourcesSession.RefuseRelink(piece)).Code);
        Assert.Equal("badRequest", Assert.Throws<FacadeException>(() => SourcesSession.HeldOnIpad(piece.Project, "src_none", held: true, s_now)).Code);

        Project kept = SourcesSession.WithDocument(piece, s_pdf, pages: 1);
        Project held = SourcesSession.HeldOnIpad(kept, PieceOriginal.DocumentId, held: true, s_now);
        Assert.Same(held, SourcesSession.HeldOnIpad(held, PieceOriginal.DocumentId, held: true, s_now.AddDays(1)));
    }
}