using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Playlists;

namespace MotiffLab.MusicCore.Tests.Projects.Playlists;

/// <summary>The playlists file (docs/playlists-file.md): its layout, its golden files, the tolerant reader and a later version read-only.</summary>
public class PlaylistsFileTests
{
    /// <summary>The playlists of the golden file: two with pieces, one empty.</summary>
    private static LibraryPlaylists Worked() =>
        LibraryPlaylistsTests.Two().Created("Для рояля", [], IdSource.Numbered).Playlists.Added("list_0002", ["p_a"]);

    private static LibraryPlaylists Read(JsonObject file) => PlaylistsFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString()));

    private static JsonObject Kept(string name) => JsonNode.Parse(Golden(name))!.AsObject();

    private static byte[] Golden(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", name));

    [Fact]
    public void The_writer_makes_the_golden_file_and_reads_it_back_the_same()
    {
        byte[] written = PlaylistsFile.Write(Worked());
        if (Environment.GetEnvironmentVariable("MOTIFF_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(SourcePath()))!)!, "Golden", "v1-worked.playlists.json"), written);
            return;
        }

        Assert.Equal(GoldenFiles.Text(Golden("v1-worked.playlists.json")), GoldenFiles.Text(written));
        Assert.Equal(Worked(), PlaylistsFile.Read(written));
        Assert.Equal(written, PlaylistsFile.Save(PlaylistsFile.Read(written)));
    }

    /// <summary>
    /// The future file: the golden with version 2 and unknown fields at the top and in a playlist. A test checks it is exactly
    /// that; it reads read-only, its unknown fields kept, and is written back as it came, but never saved.
    /// </summary>
    [Fact]
    public void A_later_version_this_build_may_read_opens_read_only_and_is_written_back_whole_but_never_saved()
    {
        JsonObject expected = Kept("v1-worked.playlists.json");
        expected["version"] = 2;
        expected["playlists"]![0]!["future"] = new JsonObject { ["colour"] = "plum" };
        expected["future"] = new JsonArray(1, 2);
        Assert.Equal(expected.ToJsonString(), Kept("v2-future.playlists.json").ToJsonString());

        LibraryPlaylists future = PlaylistsFile.Read(Golden("v2-future.playlists.json"));

        Assert.True(future.IsReadOnly);
        Assert.Equal(new NewerFile(2, 1), future.Newer);
        Assert.Equal(GoldenFiles.Text(Golden("v2-future.playlists.json")), GoldenFiles.Text(PlaylistsFile.Write(future)));
        Assert.True(Assert.Throws<ProjectFileException>(() => PlaylistsFile.Save(future)).ReadOnly);
    }

    [Fact]
    public void Unknown_fields_of_this_version_are_kept_and_the_playlists_saved()
    {
        JsonObject file = Kept("v1-worked.playlists.json");
        file["playlists"]![1]!["future"] = true;
        file["future"] = "later";

        LibraryPlaylists read = Read(file);

        Assert.False(read.IsReadOnly);
        Assert.Equal([new UnknownField("future", "true")], read.Find("list_0002")!.Unknown);
        Assert.Equal(file.ToJsonString(), JsonNode.Parse(PlaylistsFile.Save(read))!.ToJsonString());
    }

    [Fact]
    public void A_file_a_later_build_may_not_read_is_refused()
    {
        JsonObject file = Kept("v1-worked.playlists.json");
        file["version"] = 3;
        file["minReader"] = 2;

        Assert.Contains("newer version", Assert.Throws<ProjectFileException>(() => Read(file)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"format":"motiff-lab-project","version":1,"minReader":1,"playlists":[]}""", "not a Motiff Lab playlists file")]
    [InlineData("""{"format":"motiff-lab-playlists","version":1,"playlists":[]}""", "minReader")]
    [InlineData("""{"format":"motiff-lab-playlists","version":0,"minReader":1,"playlists":[]}""", "version")]
    [InlineData("""{"format":"motiff-lab-playlists","version":1,"minReader":1}""", "playlists")]
    [InlineData("""{"format":"motiff-lab-playlists","version":1,"minReader":1,"playlists":[{"id":"list_1","name":"  ","pieces":[]}]}""", "names")]
    [InlineData("""{"format":"motiff-lab-playlists","version":1,"minReader":1,"playlists":[{"id":"list_1","name":"Бах","pieces":[3]}]}""", "pieces")]
    [InlineData("""{"format":"motiff-lab-playlists","version":1,"minReader":1,"playlists":[{"id":"list_1","name":"Бах","pieces":[]},{"id":"list_1","name":"Шопен","pieces":[]}]}""", "ids")]
    [InlineData("not json", "not valid JSON")]
    public void A_damaged_file_is_refused_with_what_cannot_be_read(string text, string reason)
    {
        Assert.Contains(reason, Assert.Throws<ProjectFileException>(() => PlaylistsFile.Read(Encoding.UTF8.GetBytes(text))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_another_writer_made_reads_by_the_rules_one_line_names_each_piece_once_one_playlist_a_name()
    {
        const string Text = """{"format":"motiff-lab-playlists","version":1,"minReader":1,"playlists":[{"id":"list_1","name":" Бах  и  я ","pieces":["p_a","p_a","p_b"]},{"id":"list_2","name":"бах и я","pieces":["p_c"]}]}""";

        Assert.Equal([new Playlist("list_1", "Бах и я", ["p_a", "p_b", "p_c"])], PlaylistsFile.Read(Encoding.UTF8.GetBytes(Text)).All);
    }

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string source = "") => source;
}