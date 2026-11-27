using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

using static MotiffLab.MusicCore.Tests.Projects.OriginalOperationsTests;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The operations on a piece's Notes (decision record 0030, section 4, and its amendment of 2026-11-14): recognising again
/// beside the working notes, removing Notes, and making other Notes the working ones, on the pieces
/// <see cref="OriginalOperationsTests"/> builds.
/// </summary>
public class NotesOperationsTests
{
    [Fact]
    public void Recognising_again_adds_notes_beside_the_working_ones_which_keep_their_corrections()
    {
        byte[] musicXml = Scanned();
        Project project = ScannedPdf();

        Project again = project.RecognisedAgain(PieceOriginal.DocumentId, musicXml, "homr 0.8.0", Pages(musicXml, 3), s_now.AddHours(1), IdSource.Numbered);

        OtherNotes notes = Assert.Single(again.Sources.Notes);
        Assert.Equal(("n_2", "Nocturne.musicxml", "homr 0.8.0"), (notes.Id, notes.FileName, notes.Provenance.Engine));
        Assert.Equal(PieceOriginal.Fingerprint(s_pdf), notes.Provenance.Original!.Sha256);
        Assert.Equal(3, notes.Pages!.Pages);
        Assert.True(notes.Edits.IsEmpty);
        Assert.Equal(project.Edits, again.Edits);
        Assert.Equal(project.Source, again.Source);
        Assert.Equal(notes, Assert.Single(RoundTrip(again).Sources.Notes));
        Assert.Throws<InvalidOperationException>(() => Project.CreateDocument("p_only", "a.pdf", s_pdf, s_now).RecognisedAgain(PieceOriginal.DocumentId, musicXml, "homr", null, s_now, IdSource.Numbered));
    }

    [Fact]
    public void Removing_notes_keeps_the_original_and_never_takes_the_working_notes()
    {
        Project project = ScannedPdf().RecognisedAgain(PieceOriginal.DocumentId, Scanned(), "homr 0.8.0", null, s_now, IdSource.Numbered).ViewHidden("n_2", hidden: true);

        Project removed = project.DeleteNotes("n_2");

        Assert.Empty(removed.Sources.Notes);
        Assert.Empty(removed.HiddenViews);
        Assert.Equal(s_pdf, removed.Document);
        Assert.Throws<InvalidOperationException>(() => project.DeleteNotes(project.NotesId));
        Assert.Throws<KeyNotFoundException>(() => project.DeleteNotes("n_7"));
    }

    [Fact]
    public void Other_notes_made_working_keep_the_old_working_notes_whole_and_the_motifs_wait_with_their_own_notes()
    {
        Project worked = ProjectFileTests.Worked();
        Project project = worked with { Marks = worked.Marks.AddNote(new MarginNote("n_0001", new MusicCore.Time.TickSpan(new MusicCore.Time.Tick(0), 480), "Breathe", s_now)) };
        var other = new OtherNotes(project.NextNotesId(IdSource.Numbered), "minuet.musicxml", Original(), new NotesProvenance(NotesProvenance.Import) { From = [new ProvenanceSource("src_fur")] });
        Project two = project.AddNotes(other);
        Assert.Throws<ArgumentException>(() => two.AddNotes(other));

        Project swapped = two.MakeWorking(other.Id, out _);

        Assert.Equal((other.Id, other.Music), (swapped.NotesId, swapped.Source));
        Assert.Equal(project.FileName, swapped.FileName);
        OtherNotes before = Assert.Single(swapped.Sources.Notes);
        Assert.Equal((project.NotesId, project.Source), (before.Id, before.Music));
        Assert.Equal(project.Marks, before.Marks);
        Assert.True(swapped.Marks.IsEmpty);
        Assert.All(swapped.Motifs, motif => Assert.Equal(project.NotesId, motif.Notes));
        OpenedProject opened = OpenedProject.Open(swapped);
        Assert.Empty(opened.Motifs);
        Assert.Equal(["m_0001", "m_0002", "v_0001", "v_0002"], opened.Hidden.Order(StringComparer.Ordinal));
        Assert.Equal(ProjectFile.Write(swapped), ProjectFile.Write(RoundTrip(swapped)));

        Project back = swapped.MakeWorking(project.NotesId, out _);
        Assert.Equal(ProjectFile.Write(two), ProjectFile.Write(back));
        Assert.Equal(2, OpenedProject.Open(back).Motifs.Count);
        Assert.Throws<KeyNotFoundException>(() => back.MakeWorking(back.NotesId, out _));
    }

    [Fact]
    public void Removing_notes_takes_the_motifs_made_on_them()
    {
        Project project = ProjectFileTests.Worked();
        var other = new OtherNotes("n_2", "fur-elise.musicxml", Original(), new NotesProvenance(NotesProvenance.Import));
        Project swapped = project.AddNotes(other).MakeWorking("n_2", out _);

        Project removed = swapped.DeleteNotes(project.NotesId);

        Assert.Empty(removed.Motifs);
        Assert.Empty(removed.Variations);
        Assert.Empty(removed.Sources.Notes);
    }
}