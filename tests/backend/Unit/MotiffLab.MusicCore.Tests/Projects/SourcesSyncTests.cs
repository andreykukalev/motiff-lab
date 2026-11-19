using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The sync of originals and Notes (decision record 0030, section 5; 0026 S4, S9): a removal travels as its tombstone and
/// gives way to the same file added again later; Notes merge by id; corrections of different notes are concatenated, the
/// same note corrected on both sides is the winner's; with a base, what one side undid or removed goes through.
/// </summary>
public class SourcesSyncTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private static readonly byte[] s_pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");

    private static readonly byte[] s_photo = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private const string CopyTitle = "{0} (previous copy)";

    private static byte[] Music() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", "fur-elise-original.musicxml"));

    private static Project FurElise() => Project.Create("p_fur", "fur-elise.musicxml", Music(), s_now);

    private static IReadOnlyList<Note> Notes(Project project) => PieceImporter.Import(project.FileName, project.Source).Piece!.Notes;

    /// <summary>The correction that raises note <paramref name="index"/> of the piece by <paramref name="semitones"/>.</summary>
    private static NoteEdit Raised(Project project, int index, int semitones = 1)
    {
        NoteValues before = NoteValues.Of(Notes(project)[index]);
        return new NoteEdit(Notes(project)[index].Id, before, before with { Pitch = new Pitch(before.Pitch.MidiNumber + semitones) });
    }

    private static Project Corrected(Project project, params NoteEdit[] edits) => project with { Edits = new NoteEditLog([.. project.Edits.Edits, .. edits]) };

    private static Project WithPdf(Project project) => project.WithDocument(s_pdf, PageMap.Even(PieceImporter.Import(project.FileName, project.Source).Piece!.Meter, new Tick(7680), 1));

    [Fact]
    public void A_removed_pdf_stays_removed_on_the_device_that_still_has_it()
    {
        Project both = WithPdf(FurElise());
        Project removed = both.RemoveOriginal(PieceOriginal.DocumentId, s_now.AddHours(1));

        Project pulled = ProjectSync.Pulled(ProjectFile.Read(ProjectFile.Write(removed.ForOnline()!)), both);
        Project pushed = ProjectSync.Pulled(both.ForOnline()!, removed);

        Assert.Null(pulled.Document);
        Assert.Null(pulled.FindOriginal(PieceOriginal.DocumentId));
        Assert.Single(pulled.Sources.Removed);
        Assert.Null(pushed.Document);
        Assert.Single(pushed.Sources.Removed);
    }

    [Fact]
    public void The_same_file_added_again_after_the_removal_revives_it_on_every_device()
    {
        Project removed = WithPdf(FurElise()).RemoveOriginal(PieceOriginal.DocumentId, s_now.AddHours(1));
        PieceOriginal again = new(PieceOriginal.DocumentId, OriginalKind.Pdf, "fur-elise.pdf", OriginalKept.File, s_now.AddHours(2));
        Project revived = removed.AddOriginal(again, s_pdf, removed.DocumentPages ?? PageMap.Even(PieceImporter.Import("a.musicxml", Music()).Piece!.Meter, new Tick(7680), 1));

        Project pulled = ProjectSync.Pulled(revived.ForOnline()!, removed);

        Assert.Empty(pulled.Sources.Removed);
        Assert.NotNull(pulled.FindOriginal(PieceOriginal.DocumentId));
    }

    [Fact]
    public void The_bytes_of_other_originals_stay_on_the_device_and_come_back_from_it_on_a_pull()
    {
        Project project = FurElise().AddOriginal(new PieceOriginal("src_photo", OriginalKind.Images, "page.png", OriginalKept.File, s_now), s_photo);

        Project online = ProjectFile.Read(ProjectFile.Write(project.ForOnline()!));

        Assert.Empty(online.Sources.Data);
        Assert.False(online.HasInFile(online.FindOriginal("src_photo")!));
        Assert.Equal(s_photo, ProjectSync.Pulled(online, project).Sources.Data["src_photo"]);
    }

    [Fact]
    public void Corrections_of_different_notes_on_two_devices_are_both_kept_and_the_same_note_is_the_last_save()
    {
        Project piece = FurElise();
        Project here = Corrected(piece, Raised(piece, 0), Raised(piece, 2));
        Project online = Corrected(piece, Raised(piece, 1), Raised(piece, 2, semitones: 2));

        SyncResolution resolved = ProjectSync.Resolve(here, s_now.AddMinutes(5), online, s_now, CopyTitle, "p_copy");

        Assert.Equal([Raised(piece, 0), Raised(piece, 2), Raised(piece, 1)], resolved.Kept.Edits.Edits);
        Assert.True(resolved.Upload);
        Assert.NotNull(resolved.PreviousCopy);
        _ = resolved.Kept.Edits.Apply(Notes(piece));

        SyncResolution theirs = ProjectSync.Resolve(here, s_now, online, s_now.AddMinutes(5), CopyTitle, "p_copy");
        Assert.Equal([Raised(piece, 1), Raised(piece, 2, semitones: 2), Raised(piece, 0)], theirs.Kept.Edits.Edits);
        Assert.True(theirs.Upload);
    }

    [Fact]
    public void Without_a_base_nothing_is_taken_away_and_with_one_an_undone_correction_stays_undone()
    {
        Project piece = FurElise();
        Project @base = Corrected(piece, Raised(piece, 0));
        Project here = @base with { Edits = NoteEditLog.Empty };
        Project online = Corrected(@base, Raised(piece, 3));

        Assert.Equal([Raised(piece, 0), Raised(piece, 3)], ProjectSync.Resolve(here, s_now, online, s_now.AddMinutes(1), CopyTitle, "p_copy").Kept.Edits.Edits);
        Assert.Equal([Raised(piece, 3)], ProjectSync.Resolve(here, s_now, online, s_now.AddMinutes(1), CopyTitle, "p_copy", null, @base).Kept.Edits.Edits);
    }

    [Fact]
    public void Corrections_are_not_merged_across_different_notes_or_music()
    {
        Project piece = FurElise();
        Project here = Corrected(piece, Raised(piece, 0));
        Project online = Corrected(piece with { NotesId = "n_2" }, Raised(piece, 1));

        Assert.Equal([Raised(piece, 0)], ProjectSync.Resolve(here, s_now.AddMinutes(1), online, s_now, CopyTitle, "p_copy").Kept.Edits.Edits);
    }

    [Fact]
    public void Notes_merge_by_id_and_notes_removed_since_the_base_stay_removed()
    {
        Project piece = FurElise();
        var mine = new OtherNotes("n_2", piece.FileName, Music(), new NotesProvenance(NotesProvenance.Manual));
        var theirs = new OtherNotes("n_3", piece.FileName, Music(), new NotesProvenance(NotesProvenance.Import));
        Project here = piece.AddNotes(mine);
        Project online = piece.AddNotes(theirs);

        SyncResolution resolved = ProjectSync.Resolve(here, s_now.AddMinutes(1), online, s_now, CopyTitle, "p_copy");
        Assert.Equal(["n_2", "n_3"], resolved.Kept.Sources.Notes.Select(notes => notes.Id));

        Project @base = piece.AddNotes(mine);
        Project removedOnline = piece.AddNotes(theirs);
        Project withBase = ProjectSync.Resolve(here, s_now.AddMinutes(1), removedOnline, s_now, CopyTitle, "p_copy", null, @base).Kept;
        Assert.Equal(["n_3"], withBase.Sources.Notes.Select(notes => notes.Id));
    }

    [Fact]
    public void Corrections_inside_other_notes_merge_note_by_note()
    {
        Project piece = FurElise();
        var notes = new OtherNotes("n_2", piece.FileName, Music(), new NotesProvenance(NotesProvenance.Manual));
        Project here = piece.AddNotes(notes with { Edits = new NoteEditLog([Raised(piece, 0)]) });
        Project online = piece.AddNotes(notes with { Edits = new NoteEditLog([Raised(piece, 1)]) });

        Project kept = ProjectSync.Resolve(here, s_now, online, s_now.AddMinutes(1), CopyTitle, "p_copy").Kept;

        Assert.Equal([Raised(piece, 1), Raised(piece, 0)], Assert.Single(kept.Sources.Notes).Edits.Edits);
    }
}