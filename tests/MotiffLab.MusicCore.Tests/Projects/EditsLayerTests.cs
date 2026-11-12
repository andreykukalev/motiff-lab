using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>A piece opened with the corrections laid one by one or left out (version 0.5, «Мои правки: вкл / выкл»).</summary>
public class EditsLayerTests
{
    private static readonly DateTimeOffset s_created = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] s_source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "importer-grand-staff.musicxml"));

    private static SavedStep HalfTime() => new(RhythmReshape.OperationId, new RhythmReshapeParameters(RhythmReshapeKind.HalfTime));

    /// <summary>
    /// The minuet with the first right-hand note of bar 1 taken out, a motif on the right hand of bars 1–2 with a variation,
    /// and an idea on bars 1–2, all made over the corrected notes; with <paramref name="stale"/>, also a correction of a note
    /// the piece does not have.
    /// </summary>
    private static Project Worked(bool stale)
    {
        ImportedPiece piece = PieceImporter.Import("minuet.musicxml", s_source).Piece!;
        NoteEditLog edits = Corrections(piece, stale);
        IReadOnlyList<Note> notes = edits.ApplyEach(piece.Notes).Notes;
        MotifSelection bars = MotifSelector.Select(notes, piece.Meter, MotifSelector.Bars(piece.Meter, 1, 2, piece.End), SelectionFilter.ForHand(Hand.Right));
        Motif motif = Motif.Create("m_0001", 1, bars, MotifSelectionKind.Range, MotifMode.Discovery, notes) with { Name = "Opening" };
        return Project.Create("p_layer", "minuet.musicxml", s_source, s_created)
            .Keep(Idea(notes, bars)) with
        {
            Edits = edits,
            Motifs = [SavedMotif.From(motif)],
            Variations = [new SavedVariation("v_0001", "m_0001", [HalfTime()], null, [KeepProperty.Pitches])],
        };
    }

    /// <summary>The first right-hand note of bar 1 taken out; with <paramref name="stale"/>, also a note the piece does not have.</summary>
    private static NoteEditLog Corrections(ImportedPiece piece, bool stale)
    {
        string id = MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, 1, 1, piece.End), SelectionFilter.ForHand(Hand.Right)).NoteIds[0];
        Note first = piece.Notes.Single(note => note.Id == id);
        NoteEditLog edits = NoteEditLog.Empty.Remove(piece.Notes, first.Id);
        return stale ? new NoteEditLog([.. edits.Edits, new NoteEdit("n_9999", NoteValues.Of(first), null)]) : edits;
    }

    /// <summary>An idea on the passage: its notes in half time.</summary>
    private static Observation Idea(IReadOnlyList<Note> notes, MotifSelection bars)
    {
        VariationChain chain = VariationChain.Start(VariationFacts.MotifNotes(notes, bars), VariationContext.For(bars, new HashSet<KeepProperty> { KeepProperty.Pitches }));
        chain = chain.Propose(RhythmReshape.Instance, new RhythmReshapeParameters(RhythmReshapeKind.HalfTime)).Accept();
        return Observation.Create("o_0001", SavedPassage.Of(bars), KeptVariation.Create(chain, bars.Filter), "Half time", null, s_created);
    }

    [Fact]
    public void Strictly_a_correction_that_no_longer_fits_stops_the_piece_and_one_by_one_it_does_not()
    {
        Project project = Worked(stale: true);

        Assert.Throws<ProjectFileException>(() => OpenedProject.Open(project));
        OpenedProject opened = OpenedProject.Open(project, EditsLayer.Lenient);

        Assert.Equal(EditMisfit.Missing, Assert.Single(opened.Edits.Unfit).Reason);
        Assert.Equal([0], opened.Edits.Applied);
        Assert.Equal(["m_0001"], opened.Motifs.Select(motif => motif.Id));
        Assert.Single(opened.Variations);
        Assert.NotNull(Assert.Single(opened.Observations).Chain);
        Assert.Empty(opened.Hidden);
        Assert.Same(project.Edits, opened.Project.Edits);
    }

    [Fact]
    public void With_the_layer_off_the_notes_are_as_imported_and_what_no_longer_fits_is_hidden_but_kept()
    {
        Project project = Worked(stale: false);
        ImportedPiece piece = PieceImporter.Import(project.FileName, project.Source).Piece!;

        OpenedProject off = OpenedProject.Open(project, EditsLayer.Off);

        Assert.Equal(piece.Notes, off.Notes);
        Assert.Empty(off.Motifs);
        Assert.Empty(off.Variations);
        Assert.Empty(off.Observations);
        Assert.Equal(["m_0001", "v_0001", "o_0001"], off.Hidden);
        Assert.Equal(project.Motifs, off.Project.Motifs);
        Assert.Equal(project.Edits, off.Project.Edits);
    }

    [Fact]
    public void Off_then_saved_then_on_gives_back_every_motif_variation_idea_and_correction()
    {
        Project project = Worked(stale: false);

        OpenedProject off = OpenedProject.Open(project, EditsLayer.Off);
        Project saved = ProjectFile.Read(ProjectFile.Write(off.Project));
        OpenedProject on = OpenedProject.Open(saved, EditsLayer.Lenient);
        OpenedProject strict = OpenedProject.Open(saved);

        Assert.Equal(project.Edits, saved.Edits);
        foreach (OpenedProject opened in new[] { on, strict })
        {
            Assert.Equal(OpenedProject.Open(project).Notes, opened.Notes);
            Assert.Equal(["m_0001"], opened.Motifs.Select(motif => motif.Id));
            Assert.Equal(["v_0001"], opened.Variations.Select(variation => variation.Saved.Id));
            Assert.Equal(["o_0001"], opened.Observations.Select(observation => observation.Saved.Id));
            Assert.Empty(opened.Hidden);
        }
    }

    [Fact]
    public void A_strict_open_is_as_before_when_everything_fits()
    {
        Project project = Worked(stale: false);

        OpenedProject strict = OpenedProject.Open(project);
        OpenedProject lenient = OpenedProject.Open(project, EditsLayer.Lenient);

        Assert.Equal(strict.Notes, lenient.Notes);
        Assert.True(strict.Edits.AllFit);
        Assert.Equal(EditsLayer.Strict, strict.Layer);
        Assert.Equal(strict.Motifs.Select(motif => string.Join(",", motif.NoteIds)), lenient.Motifs.Select(motif => string.Join(",", motif.NoteIds)));
    }
}