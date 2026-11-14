using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// A linked original found again in the player's folder (version 0.5, «Проверить папку»): moved or renamed with the same
/// content, or a changed file the player said is this piece; and the linked PDF read from the folder while the piece is open.
/// </summary>
public class LinkedRelinkTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 21, 0, 0, TimeSpan.Zero);
    private static readonly byte[] s_pdf = "%PDF-1.4 Prelude in C"u8.ToArray();
    private static readonly byte[] s_other = "%PDF-1.4 Prelude in C, another edition"u8.ToArray();

    private static Project Linked() => Project.CreateLinkedDocument("p_linked", "Prelude.pdf", "Бах/ХТК", PieceOriginal.Fingerprint(s_pdf), s_pdf.Length, 2, HoldingDevice.Web, s_now);

    [Fact]
    public void A_moved_file_changes_only_the_originals_place()
    {
        Project moved = Linked().RelinkedAt(PieceOriginal.DocumentId, "Бах", "Prelude in C.pdf");

        PieceOriginal pdf = moved.FindOriginal(PieceOriginal.DocumentId)!;
        Assert.Equal(("Бах", "Prelude in C.pdf"), (pdf.Folder, pdf.FileName));
        Assert.Equal((PieceOriginal.Fingerprint(s_pdf), (long?)s_pdf.Length, (int?)2), (pdf.Sha256, pdf.Bytes, pdf.Pages));
        Assert.True(pdf.IsHeldBy(HoldingDevice.Web));
        Assert.Equal(OriginalKept.Link, pdf.Kept);
    }

    [Fact]
    public void The_same_place_is_the_same_project()
    {
        Project linked = Linked();

        Assert.Same(linked, linked.RelinkedAt(PieceOriginal.DocumentId, "Бах/ХТК", "Prelude.pdf"));
    }

    [Fact]
    public void A_changed_file_the_player_confirmed_takes_its_fingerprint()
    {
        Project changed = Linked().RelinkedAt(PieceOriginal.DocumentId, "Бах/ХТК", "Prelude.pdf", PieceOriginal.Fingerprint(s_other), s_other.Length, 3);

        PieceOriginal pdf = changed.FindOriginal(PieceOriginal.DocumentId)!;
        Assert.Equal((PieceOriginal.Fingerprint(s_other), (long?)s_other.Length, (int?)3), (pdf.Sha256, pdf.Bytes, pdf.Pages));
        Assert.NotNull(changed.WithLinkedDocument(s_other));
        Assert.Null(changed.WithLinkedDocument(s_pdf));
    }

    [Theory]
    [InlineData("../Бах", "a.pdf")]
    [InlineData("Бах", "ХТК/a.pdf")]
    [InlineData("Бах", " ")]
    public void A_place_outside_the_folder_is_refused(string folder, string fileName) =>
        Assert.ThrowsAny<ArgumentException>(() => Linked().RelinkedAt(PieceOriginal.DocumentId, folder, fileName));

    [Fact]
    public void The_linked_pdf_read_from_the_folder_opens_and_never_goes_into_the_file()
    {
        Project open = Linked().WithLinkedDocument(s_pdf)!;

        Assert.Equal(s_pdf, open.Document);
        Assert.Null(ProjectFile.Read(ProjectFile.Write(open)).Document);
        Assert.Null(Linked().WithLinkedDocument(s_other));
        Assert.Throws<InvalidOperationException>(() => open.WithLinkedDocument(s_pdf));
    }
}