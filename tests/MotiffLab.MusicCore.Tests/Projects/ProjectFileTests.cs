using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>Projects saved on the device and exported as one file (S1-P1, AC-S1-20).</summary>
public class ProjectFileTests
{
    private static readonly DateTimeOffset s_created = new(2026, 10, 23, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_opened = new(2026, 10, 24, 8, 30, 15, TimeSpan.FromHours(2));

    private static byte[] Fixture(string folder, string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", folder, "Fixtures", name));

    private static SavedStep Step(RhythmReshapeKind kind, int eighths = 1) => new(RhythmReshape.OperationId, new RhythmReshapeParameters(kind, eighths));

    /// <summary>A grand-staff piece with a bar motif in the right hand, a picked-notes motif, and a variation of each.</summary>
    private static Project Worked()
    {
        byte[] source = Fixture("MusicXml", "importer-grand-staff.musicxml");
        ImportedPiece piece = PieceImporter.Import("minuet.musicxml", source).Piece!;
        MotifSelection bars = MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, 1, 2, piece.End), SelectionFilter.ForHand(Hand.Right));
        Motif first = Motif.Create("m_0001", 1, bars, MotifSelectionKind.Range, MotifMode.Discovery, piece.Notes) with { Name = "Opening turn" };
        MotifSelection picked = MotifSelector.SelectNotes(piece.Notes, piece.Meter, piece.Notes.Take(3).Select(note => note.Id));
        Motif second = Motif.Create("m_0002", 2, picked, MotifSelectionKind.Notes, MotifMode.Analysis, piece.Notes);

        return Project.Create("p_test", "minuet.musicxml", source, s_created) with
        {
            LastOpened = s_opened,
            Motifs = [SavedMotif.From(first), SavedMotif.From(second)],
            Variations =
            [
                new SavedVariation("v_0001", "m_0001", [Step(RhythmReshapeKind.HalfTime), Step(RhythmReshapeKind.Displace, 1)], Step(RhythmReshapeKind.Displace, 3), [KeepProperty.Pitches, KeepProperty.PitchOrder]),
                new SavedVariation("v_0002", "m_0002", [], Step(RhythmReshapeKind.Displace, 2), [KeepProperty.Pitches]),
            ],
        };
    }

    [Fact]
    public void Export_and_reimport_gives_identical_motifs_variations_and_keep_choices()
    {
        Project project = Worked();
        OpenedProject before = OpenedProject.Open(project);

        Project reread = ProjectFile.Read(ProjectFile.Write(project));
        OpenedProject after = OpenedProject.Open(reread);

        Assert.Equal((project.Id, project.FileName, project.CreatedAt, project.LastOpened), (reread.Id, reread.FileName, reread.CreatedAt, reread.LastOpened));
        Assert.Equal(project.Source, reread.Source);
        Assert.Equal(before.Motifs.Select(motif => (motif.Id, motif.Name, motif.Kind, motif.Mode, motif.Span, motif.Filter, motif.Source)), after.Motifs.Select(motif => (motif.Id, motif.Name, motif.Kind, motif.Mode, motif.Span, motif.Filter, motif.Source)));
        Assert.Equal(before.Motifs.Select(motif => string.Join(",", motif.NoteIds)), after.Motifs.Select(motif => string.Join(",", motif.NoteIds)));
        Assert.Equal(2, after.Variations.Count);
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(project.Variations[i].Keep, reread.Variations[i].Keep);
            Assert.Equal(project.Variations[i].Steps, reread.Variations[i].Steps);
            Assert.Equal(project.Variations[i].Pending, reread.Variations[i].Pending);
            Assert.Equal(before.Variations[i].Chain.Result!.Notes, after.Variations[i].Chain.Result!.Notes);
            Assert.Equal(before.Variations[i].Chain.Result!.Checks, after.Variations[i].Chain.Result!.Checks);
            Assert.Equal(before.Variations[i].Chain.Result!.Explanation, after.Variations[i].Chain.Result!.Explanation);
        }
    }

    [Fact]
    public void Writing_is_deterministic_and_round_trips_byte_for_byte()
    {
        byte[] written = ProjectFile.Write(Worked());

        Assert.Equal(written, ProjectFile.Write(Worked()));
        Assert.Equal(written, ProjectFile.Write(ProjectFile.Read(written)));
    }

    [Fact]
    public void Opening_rebuilds_names_and_keeps_keep_choices_in_table_order()
    {
        OpenedProject opened = OpenedProject.Open(Worked());

        Assert.Equal(["Opening turn", "Motif 2 · bars 0–1"], opened.Motifs.Select(motif => motif.Name));
        Assert.Equal([KeepProperty.Pitches, KeepProperty.PitchOrder], opened.Project.Variations[0].Keep);
        Assert.Equal(2, opened.Variations[0].Chain.Steps.Count);
        Assert.NotNull(opened.Variations[0].Chain.Pending);
        Assert.Equal("V1 › V1 › V1", opened.Variations[0].Chain.Result!.Operation);
    }

    [Fact]
    public void The_summary_counts_motifs_and_variations()
    {
        Assert.Equal(new ProjectSummary("p_test", "minuet.musicxml", 2, 2, s_opened), Worked().Summary);
        Assert.Equal(0, Project.Create("p", "a.mid", [1], s_created).Summary.MotifCount);
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("{\"format\":\"something-else\"}", "not a Motiff Lab project file")]
    [InlineData("{\"format\":\"motiff-lab-project\",\"version\":4}", "newer version")]
    [InlineData("{\"format\":\"motiff-lab-project\",\"version\":1,\"source\":\"!!\"}", "source")]
    public void Unreadable_files_get_a_plain_reason(string json, string reason)
    {
        var exception = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json)));

        Assert.Contains(reason, exception.Message);
    }

    [Theory]
    [InlineData("\"kind\": \"Range\"", "\"kind\": \"7\"")]
    [InlineData("\"eighths\": 2", "\"eighths\": 9")]
    [InlineData("\"Pitches\"\n", "\"Tempo\"\n")]
    [InlineData("\"hand\": \"Right\"", "\"hand\": \"Right\", \"track\": 0")]
    public void Damaged_fields_are_rejected(string field, string damaged)
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Worked()));
        Assert.Contains(field, json);

        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json.Replace(field, damaged, StringComparison.Ordinal))));
    }

    [Fact]
    public void A_motif_that_no_longer_matches_the_source_is_reported()
    {
        Project project = Worked();
        Project broken = project with { Motifs = [project.Motifs[0] with { NoteIds = ["n_9999"] }], Variations = [] };

        var exception = Assert.Throws<ProjectFileException>(() => OpenedProject.Open(broken));

        Assert.Contains("Opening turn", exception.Message);
    }

    [Fact]
    public void A_variation_without_its_motif_is_reported()
    {
        Project project = Worked() with { Motifs = [] };

        Assert.Throws<ProjectFileException>(() => OpenedProject.Open(project));
    }

    [Fact]
    public void A_version_1_file_opens_with_its_variation_as_one_step()
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Worked() with { Variations = [] }))
            .Replace("\"version\": 3", "\"version\": 1", StringComparison.Ordinal)
            .Replace(
                "\"variations\": []",
                "\"variations\": [{\"id\": \"v_0001\", \"motifId\": \"m_0001\", \"operation\": \"V1\", \"parameters\": {\"kind\": \"HalfTime\", \"eighths\": 1}, \"keep\": [\"Pitches\"]}]",
                StringComparison.Ordinal);

        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(json));

        Assert.Equal([Step(RhythmReshapeKind.HalfTime)], project.Variations[0].Steps);
        Assert.Null(project.Variations[0].Pending);
        Assert.Single(OpenedProject.Open(project).Variations[0].Chain.Steps);
    }

    [Fact]
    public void An_unknown_operation_is_reported()
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Worked())).Replace("\"V1\"", "\"V99\"", StringComparison.Ordinal);

        var exception = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("V99", exception.Message);
    }

    /// <summary>The worked project with the first note of the right-hand motif a semitone higher and louder, and its variation.</summary>
    private static Project Corrected()
    {
        Project project = Worked();
        ImportedPiece piece = PieceImporter.Import(project.FileName, project.Source).Piece!;
        Note first = piece.Notes.First(note => note.Id == project.Motifs[0].NoteIds[0]);
        NoteEditLog edits = NoteEditLog.Empty
            .Change(piece.Notes, first.Id, NoteValues.Of(first) with { Pitch = new Pitch(first.Pitch.MidiNumber + 1), Velocity = 100 });
        return project with { Edits = edits, Variations = [project.Variations[0]] };
    }

    [Fact]
    public void Note_corrections_are_saved_and_applied_on_open()
    {
        Project project = Corrected();

        Project reread = ProjectFile.Read(ProjectFile.Write(project));
        OpenedProject opened = OpenedProject.Open(reread);

        Assert.Equal(project.Edits, reread.Edits);
        NoteEdit edit = Assert.Single(reread.Edits.Edits);
        Note corrected = opened.Notes.Single(note => note.Id == edit.NoteId);
        Assert.Equal(edit.After, NoteValues.Of(corrected));
        Assert.Equal(Provenance.UserEdited, corrected.Attribution.Provenance);
        Assert.Equal(edit.Before, NoteValues.Of(opened.Piece.Notes.Single(note => note.Id == edit.NoteId)));
        Assert.Contains(opened.Variations[0].Chain.Original, note => note.Id == edit.NoteId && note.Pitch == edit.After!.Value.Pitch);
        Assert.Equal(ProjectFile.Write(project), ProjectFile.Write(reread));
    }

    [Fact]
    public void A_removed_note_is_saved_without_values_after()
    {
        Project project = Worked() with { Motifs = [], Variations = [] };
        ImportedPiece piece = PieceImporter.Import(project.FileName, project.Source).Piece!;
        project = project with { Edits = NoteEditLog.Empty.Remove(piece.Notes, piece.Notes[2].Id) };

        string json = Encoding.UTF8.GetString(ProjectFile.Write(project));
        OpenedProject opened = OpenedProject.Open(ProjectFile.Read(Encoding.UTF8.GetBytes(json)));

        Assert.DoesNotContain("\"after\"", json);
        Assert.Equal(piece.Notes.Count - 1, opened.Notes.Count);
        Assert.DoesNotContain(opened.Notes, note => note.Id == piece.Notes[2].Id);
    }

    [Fact]
    public void A_version_2_file_opens_with_no_corrections()
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Worked()))
            .Replace("\"version\": 3", "\"version\": 2", StringComparison.Ordinal)
            .Replace(",\n  \"edits\": []", "", StringComparison.Ordinal);
        Assert.DoesNotContain("edits", json);

        Assert.True(ProjectFile.Read(Encoding.UTF8.GetBytes(json)).Edits.IsEmpty);
    }

    [Fact]
    public void Corrections_that_no_longer_fit_the_piece_are_reported()
    {
        Project project = Corrected();
        NoteEdit edit = project.Edits.Edits[0];
        Project wrong = project with { Edits = new NoteEditLog([edit with { Before = edit.Before with { Velocity = 1 } }]) };

        var exception = Assert.Throws<ProjectFileException>(() => OpenedProject.Open(wrong));

        Assert.Contains("note corrections", exception.Message);
    }

    [Theory]
    [InlineData("\"velocity\": 100", "\"velocity\": 0")]
    [InlineData("\"noteId\": \"", "\"noteId\": 7, \"x\": \"")]
    public void Damaged_corrections_are_rejected(string field, string damaged)
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Corrected()));
        Assert.Contains(field, json);

        Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json.Replace(field, damaged, StringComparison.Ordinal))));
    }
}