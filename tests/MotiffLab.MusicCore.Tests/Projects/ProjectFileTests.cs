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
                SavedVariation.Create("v_0001", "m_0001", new(RhythmReshapeKind.HalfTime), [KeepProperty.PitchOrder, KeepProperty.Pitches]),
                SavedVariation.Create("v_0002", "m_0002", new(RhythmReshapeKind.Displace, 2), [KeepProperty.Pitches]),
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
            Assert.Equal(project.Variations[i].Parameters, reread.Variations[i].Parameters);
            Assert.Equal(before.Variations[i].Variation.Notes, after.Variations[i].Variation.Notes);
            Assert.Equal(before.Variations[i].Variation.Checks, after.Variations[i].Variation.Checks);
            Assert.Equal(before.Variations[i].Variation.Explanation, after.Variations[i].Variation.Explanation);
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
        Assert.Equal(RhythmReshape.OperationId, opened.Variations[0].Variation.Operation);
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
    [InlineData("{\"format\":\"motiff-lab-project\",\"version\":2}", "newer version")]
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
}