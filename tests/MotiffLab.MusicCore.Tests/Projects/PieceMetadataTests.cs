using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>The title, composer, tags and playlists the player keeps for a piece in the library (version 0.2), and what a card shows.</summary>
public class PieceMetadataTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name));

    private static byte[] Midi() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Midi", "Fixtures", "two-hands-type1.mid"));

    private static Project FurElise() => Project.Create("p_fur", "fur-elise.musicxml", Fixture("fur-elise-original.musicxml"), s_now);

    private static byte[] Score(string header) => Encoding.UTF8.GetBytes(
        $"""<?xml version="1.0"?><score-partwise version="4.0">{header}<part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list><part id="P1"><measure number="1"/></part></score-partwise>""");

    [Fact]
    public void Tags_are_trimmed_kept_in_order_and_distinct_in_any_letter_case_and_empty_ones_are_dropped()
    {
        var metadata = new PieceMetadata("  Für   Elise \n", " ", [" Exam ", "exam", "", "  ", "Романтика", "РОМАНТИКА", "Two  words", null], [" Recital ", "recital", "", "Экзамен 2027"]);

        Assert.Equal("Für Elise", metadata.Title);
        Assert.Null(metadata.Composer);
        Assert.Equal(["Exam", "Романтика", "Two words"], metadata.Tags);
        Assert.Equal(["Recital", "Экзамен 2027"], metadata.Playlists);
        Assert.False(metadata.IsEmpty);
        Assert.True(new PieceMetadata("", null, ["", " "], [" "]).IsEmpty);
        Assert.False(new PieceMetadata(null, null, null, ["Recital"]).IsEmpty);
        Assert.Equal(metadata, new PieceMetadata("Für Elise", null, ["Exam", "Романтика", "Two words"], ["Recital", "Экзамен 2027"]));
        Assert.NotEqual(metadata, new PieceMetadata("Für Elise", null, ["Exam", "Романтика", "Two words"], ["Recital"]));
    }

    [Fact]
    public void Metadata_round_trips_through_the_project_file_and_the_same_project_gives_the_same_bytes()
    {
        Project project = FurElise() with { Metadata = new PieceMetadata("Für Elise", "L. van Beethoven", ["Exam", "Bagatelle"], ["Recital"]) };

        byte[] written = ProjectFile.Write(project);
        Project read = ProjectFile.Read(written);

        Assert.Equal(project.Metadata, read.Metadata);
        Assert.Equal(written, ProjectFile.Write(project));
        Assert.Equal(written, ProjectFile.Write(read));
        JsonObject file = JsonNode.Parse(written)!.AsObject();
        Assert.Equal(15, file["version"]!.GetValue<int>());
        Assert.Equal("""{"title":"Für Elise","composer":"L. van Beethoven","tags":["Exam","Bagatelle"],"playlists":["Recital"]}""", file["metadata"]!.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    [Fact]
    public void Only_the_fields_set_are_written_and_none_at_all_while_nothing_is_set()
    {
        JsonObject none = JsonNode.Parse(ProjectFile.Write(FurElise()))!.AsObject();
        Assert.False(none.ContainsKey("metadata"));

        JsonObject tagged = JsonNode.Parse(ProjectFile.Write(FurElise() with { Metadata = new PieceMetadata(null, null, ["Exam"], null) }))!.AsObject();
        JsonObject metadata = tagged["metadata"]!.AsObject();
        Assert.Equal(["tags"], metadata.Select(pair => pair.Key));

        JsonObject listed = JsonNode.Parse(ProjectFile.Write(FurElise() with { Metadata = new PieceMetadata(null, null, null, ["Recital"]) }))!.AsObject();
        Assert.Equal(["playlists"], listed["metadata"]!.AsObject().Select(pair => pair.Key));
    }

    [Fact]
    public void A_file_written_before_the_metadata_reads_with_none_and_is_written_again_without_it()
    {
        byte[] golden = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "v15-worked.motiff.json"));

        Project project = ProjectFile.Read(golden);

        Assert.Same(PieceMetadata.None, project.Metadata);
        Assert.False(JsonNode.Parse(ProjectFile.Write(project))!.AsObject().ContainsKey("metadata"));
    }

    [Theory]
    [InlineData("\"metadata\": \"Exam\"")]
    [InlineData("\"metadata\": {\"tags\": \"Exam\"}")]
    [InlineData("\"metadata\": {\"tags\": [1]}")]
    [InlineData("\"metadata\": {\"title\": 3}")]
    [InlineData("\"metadata\": {\"playlists\": \"Recital\"}")]
    [InlineData("\"metadata\": {\"playlists\": [null]}")]
    public void Metadata_of_the_wrong_shape_is_damaged(string field)
    {
        string file = Encoding.UTF8.GetString(ProjectFile.Write(FurElise())).Replace("\"source\":", field + ",\n  \"source\":", StringComparison.Ordinal);

        ProjectFileException damaged = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(file)));
        Assert.Contains("metadata", damaged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_with_untidy_metadata_reads_it_cleaned()
    {
        string file = Encoding.UTF8.GetString(ProjectFile.Write(FurElise())).Replace("\"source\":", "\"metadata\": {\"title\": \"  \", \"tags\": [\"a\", \"A\", \" b \"], \"playlists\": [\" c \", \"C\"]},\n  \"source\":", StringComparison.Ordinal);

        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(file));

        Assert.Null(project.Metadata.Title);
        Assert.Equal(["a", "b"], project.Metadata.Tags);
        Assert.Equal(["c"], project.Metadata.Playlists);
    }

    [Fact]
    public void A_file_with_tags_but_no_playlists_reads_as_on_no_playlist()
    {
        string file = Encoding.UTF8.GetString(ProjectFile.Write(FurElise())).Replace("\"source\":", "\"metadata\": {\"tags\": [\"Exam\"]},\n  \"source\":", StringComparison.Ordinal);

        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(file));

        Assert.Equal(["Exam"], project.Metadata.Tags);
        Assert.Empty(project.Metadata.Playlists);
        Assert.Empty(project.Details.Playlists);
    }

    [Fact]
    public void A_card_shows_the_score_title_and_composer_as_from_the_file_until_the_player_sets_their_own()
    {
        Project project = FurElise();

        PieceDetails fromFile = project.Details;
        Assert.Equal(("Fur Elise, opening", Provenance.Imported), (fromFile.Title.Value, fromFile.Title.Attribution.Provenance));
        Assert.Equal(("Ludwig van Beethoven", Provenance.Imported), (fromFile.Composer!.Value, fromFile.Composer.Attribution.Provenance));
        Assert.Empty(fromFile.Tags);
        Assert.Empty(fromFile.Playlists);
        Assert.True(project.Metadata.IsEmpty);

        PieceDetails own = (project with { Metadata = new PieceMetadata("Für Elise", null, ["Exam"], ["Recital"]) }).Details;
        Assert.Equal(("Für Elise", Provenance.UserEdited), (own.Title.Value, own.Title.Attribution.Provenance));
        Assert.Equal(Provenance.Imported, own.Composer!.Attribution.Provenance);
        Assert.Equal(["Exam"], own.Tags);
        Assert.Equal(["Recital"], own.Playlists);
        Assert.Equal((fromFile.Title, fromFile.Composer), (project.Summary.Details!.Title, project.Summary.Details.Composer));
    }

    [Fact]
    public void A_midi_file_a_scan_and_a_pdf_are_titled_by_their_file_name_with_no_composer()
    {
        PieceDetails midi = Project.Create("p_midi", "Two hands.mid", Midi(), s_now).Details;
        Assert.Equal(("Two hands", Provenance.Inferred), (midi.Title.Value, midi.Title.Attribution.Provenance));
        Assert.Null(midi.Composer);

        Project scanned = FurElise() with { FileName = "Scan of page 1.musicxml", Scan = ScanOrigin.Read("homr") };
        Assert.Equal(("Scan of page 1", Provenance.Inferred), (scanned.Details.Title.Value, scanned.Details.Title.Attribution.Provenance));
        Assert.Null(scanned.Details.Composer);

        Project pdf = Project.CreateDocument("p_pdf", "Nocturne.final.pdf", Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n"), s_now);
        Assert.Equal("Nocturne.final", pdf.Details.Title.Value);
        Assert.Equal("Nocturne", PieceDetails.TitleOf("Nocturne.motiff.json"));
        Assert.Equal(".mid", PieceDetails.TitleOf(".mid"));
    }

    [Theory]
    [InlineData("<work><work-title>Sonata</work-title></work><movement-title>Allegro</movement-title>", "Sonata", null)]
    [InlineData("<work><work-title> </work-title></work><movement-title>Allegro</movement-title><identification><creator type=\"lyricist\">Someone</creator><creator type=\"composer\">Clara  Schumann</creator></identification>", "Allegro", "Clara Schumann")]
    [InlineData("<credit page=\"1\"><credit-type>title</credit-type><credit-words>Nocturne</credit-words></credit><credit page=\"1\"><credit-type>composer</credit-type><credit-words>F. Chopin</credit-words></credit>", "Nocturne", "F. Chopin")]
    [InlineData("<identification><creator type=\"composer\">J. S. Bach</creator></identification><credit page=\"1\"><credit-type>composer</credit-type><credit-words>Bach</credit-words></credit>", null, "J. S. Bach")]
    [InlineData("", null, null)]
    public void The_header_is_read_from_work_movement_creator_and_credits(string header, string? title, string? composer)
    {
        Assert.Equal(new ScoreHeader(title, composer), ScoreHeader.Read(Score(header)));
    }

    [Fact]
    public void Bytes_that_are_not_a_score_have_no_header()
    {
        Assert.Equal(ScoreHeader.None, ScoreHeader.Read(Midi()));
        Assert.Equal(ScoreHeader.None, ScoreHeader.Read("<score-partwise><work><work-title>Broken"u8));
        Assert.Equal(ScoreHeader.None, ScoreHeader.Read("<html><title>No</title></html>"u8));
        Assert.Equal(ScoreHeader.None, ScoreHeader.Read([]));
    }
}