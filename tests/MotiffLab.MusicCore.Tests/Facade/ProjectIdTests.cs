using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Facade;

/// <summary>
/// Project ids (task #291, V8): a new project gets "p_" and a GUID, and an imported project file never overwrites a
/// different piece that carries the same id, while the player's own export still replaces itself (AC-S1-20).
/// </summary>
public partial class ProjectIdTests
{
    private const string FurElise = "fur-elise-original.musicxml";
    private const string OdeToJoy = "ode-to-joy.musicxml";

    [GeneratedRegex("^p_[0-9a-f]{32}$")]
    private static partial Regex GuidId();

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name));

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    private static JsonElement Call(MusicCoreFacade facade, string name, string? request = null)
    {
        JsonElement answer = Json(facade.Call(1, name, request));
        Assert.False(answer.TryGetProperty("error", out JsonElement error), $"{name}: {error} {answer}");
        return answer;
    }

    private static string OpenedId(MusicCoreFacade facade, string fileName, byte[] data, string? request = null)
    {
        JsonElement opened = Json(facade.Open(fileName, data, request));
        Assert.False(opened.TryGetProperty("error", out JsonElement error), error.ToString());
        return opened.GetProperty("project").GetProperty("id").GetString()!;
    }

    /// <summary>A project file with a motif, a variation tried from a chip (with its Keep preset) and a phrase edit, as the player saves it.</summary>
    private static byte[] Worked(string fileName, string request, string now)
    {
        var facade = new MusicCoreFacade();
        OpenedId(facade, fileName, Fixture(fileName), request);
        Call(facade, "select", """{"bars": {"first": 1, "last": 2}}""");
        Call(facade, "try", """{"chip": "v1.halfTime"}""");
        JsonElement firstPhrase = Call(facade, "phrases").GetProperty("units")[0];
        Call(facade, "phrases.edit", $$$"""{"joinWithNext": {{{firstPhrase.GetProperty("number").GetInt32()}}}}""");
        return Encoding.UTF8.GetBytes(facade.Call(1, "project.save", $$"""{"now": "{{now}}"}"""));
    }

    /// <summary>
    /// The web app's import path (Home's ImportProjectAsync) over the device's store and the online queue, both keyed by
    /// project id as <c>ProjectStore.SaveAsync</c> and <c>OnlineProjects.QueueAsync</c> key them.
    /// </summary>
    private sealed class Device
    {
        public Dictionary<string, byte[]> Saved { get; } = [];

        public Dictionary<string, byte[]> Queued { get; } = [];

        public void Save(Project project)
        {
            byte[] file = ProjectFile.Write(project);
            Saved[project.Id] = file;
            Queued[project.Id] = file;
        }

        public Project Import(byte[] file)
        {
            Project read = ProjectFile.Read(file);
            Project? saved = null;
            bool unreadable = false;
            if (Saved.TryGetValue(read.Id, out byte[]? bytes))
            {
                try
                {
                    saved = ProjectFile.Read(bytes);
                }
                catch (ProjectFileException)
                {
                    unreadable = true;
                }
            }

            Project project = MusicCoreFacade.Imported(read, saved, unreadable);
            Save(project);
            return project;
        }
    }

    [Fact]
    public void A_new_project_without_an_id_gets_p_and_a_guid_different_in_every_call_and_every_run()
    {
        var run = new MusicCoreFacade();
        string first = OpenedId(run, FurElise, Fixture(FurElise));
        string second = OpenedId(run, OdeToJoy, Fixture(OdeToJoy));
        // A new facade is a new launch of the app: the old counter started again at p_0001 there.
        string nextRun = OpenedId(new MusicCoreFacade(), FurElise, Fixture(FurElise));

        Assert.Matches(GuidId(), first);
        Assert.Matches(GuidId(), second);
        Assert.Matches(GuidId(), nextRun);
        Assert.Equal(3, new HashSet<string> { first, second, nextRun }.Count);
        Assert.Matches(GuidId(), MusicCoreFacade.NewProjectId());
        Assert.NotEqual(MusicCoreFacade.NewProjectId(), MusicCoreFacade.NewProjectId());

        // A PDF added to read as it is takes the same kind of id.
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n");
        Assert.Matches(GuidId(), OpenedId(new MusicCoreFacade(), "score.pdf", pdf));

        // An id the app passes is kept as it is.
        Assert.Equal("p_fur", OpenedId(new MusicCoreFacade(), FurElise, Fixture(FurElise), """{"id": "p_fur"}"""));
    }

    [Fact]
    public void A_different_piece_with_the_same_id_is_saved_as_a_new_project_and_the_saved_one_is_unchanged()
    {
        // Two iPad pieces made in two launches, both numbered p_0001 by the old counter.
        byte[] mine = Worked(FurElise, """{"id": "p_0001", "now": "2026-11-01T10:00:00Z"}""", "2026-11-01T11:00:00Z");
        byte[] other = Worked(OdeToJoy, """{"id": "p_0001", "now": "2026-11-05T09:30:00Z"}""", "2026-11-05T10:00:00Z");
        var device = new Device();
        device.Import(mine);
        Assert.Equal(mine, device.Saved["p_0001"]);

        Project imported = device.Import(other);

        Assert.Matches(GuidId(), imported.Id);
        Assert.Equal(2, device.Saved.Count);
        Assert.Equal(mine, device.Saved["p_0001"]);
        Assert.Equal(mine, device.Queued["p_0001"]);
        Assert.Equal(device.Saved[imported.Id], device.Queued[imported.Id]);

        // Only the id changed: the new project is the imported file with its work, under a new id.
        Project read = ProjectFile.Read(other);
        Assert.Equal(ProjectFile.Write(read with { Id = imported.Id }), device.Saved[imported.Id]);
        Assert.Equal(read.CreatedAt, imported.CreatedAt);
        Assert.Equal("ode-to-joy.musicxml", imported.FileName);

        // Importing that file once more does not touch either saved project.
        Project again = device.Import(other);
        Assert.NotEqual(imported.Id, again.Id);
        Assert.Equal(mine, device.Saved["p_0001"]);
        Assert.Equal(ProjectFile.Write(read with { Id = imported.Id }), device.Saved[imported.Id]);
    }

    [Fact]
    public void The_players_own_export_imported_again_replaces_itself_with_the_same_work()
    {
        byte[] export = Worked(FurElise, """{"id": "p_0001", "now": "2026-11-01T10:00:00Z"}""", "2026-11-01T11:00:00Z");
        var device = new Device();
        device.Import(export);

        Project back = device.Import(export);

        Assert.Equal("p_0001", back.Id);
        Assert.Single(device.Saved);
        Assert.Equal(export, device.Saved["p_0001"]);
        Assert.Equal(export, device.Queued["p_0001"]);

        // The work opens as it was: the motif, the variation with its Keep choices, and the timing.
        Project saved = ProjectFile.Read(export);
        Assert.Same(back, MusicCoreFacade.Imported(back, saved));
        Assert.Equal(saved.Motifs.Count, back.Motifs.Count);
        Assert.Equal(saved.Variations.Count, back.Variations.Count);
        var reopened = new MusicCoreFacade();
        OpenedId(reopened, "fur-elise" + ProjectFile.Extension, device.Saved["p_0001"]);
        JsonElement chain = Call(reopened, "variation");
        Assert.Equal(["pitchOrder"], chain.GetProperty("keep").EnumerateArray().Select(keep => keep.GetString()!).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("V1", Assert.Single(chain.GetProperty("steps").EnumerateArray()).GetProperty("operation").GetString());
    }

    [Fact]
    public void The_same_project_saved_later_still_replaces_itself_and_the_same_instant_in_another_offset_counts_as_the_same()
    {
        byte[] export = Worked(FurElise, """{"id": "p_0001", "now": "2026-11-01T10:00:00Z"}""", "2026-11-01T11:00:00Z");
        Project saved = ProjectFile.Read(export);
        Project later = saved with { LastOpened = saved.LastOpened.AddDays(3), CreatedAt = saved.CreatedAt.ToOffset(TimeSpan.FromHours(3)) };

        Assert.Same(later, MusicCoreFacade.Imported(later, saved));
        Assert.Matches(GuidId(), MusicCoreFacade.Imported(saved with { CreatedAt = saved.CreatedAt.AddTicks(1) }, saved).Id);
    }

    [Fact]
    public void A_saved_project_that_cannot_be_read_is_not_overwritten()
    {
        byte[] export = Worked(FurElise, """{"id": "p_0001", "now": "2026-11-01T10:00:00Z"}""", "2026-11-01T11:00:00Z");
        var device = new Device();
        byte[] damaged = Encoding.UTF8.GetBytes("{\"format\": \"broken");
        device.Saved["p_0001"] = damaged;

        Project imported = device.Import(export);

        Assert.Matches(GuidId(), imported.Id);
        Assert.Equal(damaged, device.Saved["p_0001"]);
    }

    [Theory]
    [InlineData("p_0001")]
    [InlineData("p_3f9c0a1b2c3d4e5f60718293a4b5c6d7")]
    public void Project_files_with_existing_ids_open_as_before_in_the_same_format(string id)
    {
        byte[] file = Worked(FurElise, $$"""{"id": "{{id}}", "now": "2026-11-01T10:00:00Z"}""", "2026-11-01T11:00:00Z");
        JsonElement root = Json(Encoding.UTF8.GetString(file));
        Assert.Equal(15, ProjectFile.Version);
        Assert.Equal(15, root.GetProperty("version").GetInt32());
        Assert.Equal(id, root.GetProperty("id").GetString());

        var facade = new MusicCoreFacade();
        Assert.Equal(id, OpenedId(facade, "fur-elise" + ProjectFile.Extension, file));
        Assert.Equal(file, Encoding.UTF8.GetBytes(facade.Call(1, "project.save", """{"now": "2026-11-01T11:00:00Z"}""")));

        // With nothing saved under its id, an import keeps the file's id.
        var device = new Device();
        Assert.Equal(id, device.Import(file).Id);
        Assert.Equal(file, device.Saved[id]);
    }
}