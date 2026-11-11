using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Golden;

/// <summary>
/// What today's reader and writer do with project files of earlier, current and later versions (task #293; basis rule
/// S6 and pre-build check 1 of the project-continuity council). The files are in this folder; README.md says where each
/// came from. A file of an earlier version is never changed: the writer's answer to it is the golden that changes, on
/// purpose, when the format does (run the tests with MOTIFF_UPDATE_SNAPSHOTS=1 to write it again).
/// </summary>
public class ProjectFileGoldenTests
{
    private const string UpdateVariable = "MOTIFF_UPDATE_SNAPSHOTS";

    private const string Paterlini = "v12-paterlini-all-my-joy";
    private const string Einaudi = "v12-einaudi-santiago";
    private const string Worked = "v15-worked.motiff.json";
    private const string Future = "v16-future.motiff.json";

    /// <summary>
    /// Where the future file puts an unknown value of each enum type the reader names (council fact V28: 24 types behind
    /// the 33 uses of Name and LowerName in ProjectFile*.cs), as a path into the version 15 file.
    /// </summary>
    public static TheoryData<string, string> EnumValues => new()
    {
        { "AccentPattern", "variations/3/steps/0/parameters/pattern" },
        { "AccompanimentPatternKind", "variations/5/steps/0/parameters/kind" },
        { "ArticulationKind", "variations/1/steps/0/parameters/kind" },
        { "BassPatternKind", "variations/7/steps/0/parameters/kind" },
        { "CheckReason", "check/0/reason" },
        { "CheckSource", "check/0/source" },
        { "DensityKind", "variations/6/steps/0/parameters/kind" },
        { "Hand", "motifs/0/filter/hand" },
        { "HandEditKind", "variations/10/pending/parameters/kind" },
        { "InkColour", "marks/highlights/0/colour" },
        { "InkTool", "marks/ink/0/tool" },
        { "KeyMode", "variations/2/key/mode" },
        { "MergeChoice", "performances/0/merge/bars/0/choice" },
        { "MotifMode", "motifs/0/mode" },
        { "MotifSelectionKind", "motifs/0/kind" },
        { "OperationScope", "variations/8/steps/0/parameters/scope" },
        { "OrnamentKind", "variations/2/steps/0/parameters/kind" },
        { "PassingNoteKind", "variations/4/steps/0/parameters/kind" },
        { "PatternUnit", "variations/6/steps/0/parameters/unit" },
        { "PerformanceSource", "performances/0/source" },
        { "Provenance", "assembly/applied/notes/0/attribution/provenance" },
        { "ReharmoniseKind", "variations/9/steps/0/parameters/kind" },
        { "RhythmReshapeKind", "variations/0/steps/0/parameters/kind" },
        { "VoicingKind", "variations/8/steps/0/parameters/kind" },
    };

    private const string UnknownValue = "FutureValue";

    /// <summary>
    /// The enum types whose only place in the file is a note correction, a performance or the assembly, which change the
    /// notes that sound: an unknown value there still makes the file damaged (Andrei's decision on #313, option A; read-only
    /// opening takes them up).
    /// </summary>
    private static readonly HashSet<string> s_onNotesThatSound = ["PerformanceSource", "MergeChoice", "Provenance"];

    /// <summary>The rows of <see cref="EnumValues"/> whose entity this build keeps as unknown (S6, #313): 21 of the 24 types.</summary>
    public static TheoryData<string, string> KeptValues => Rows(row => !s_onNotesThatSound.Contains(row.Type));

    /// <summary>
    /// Every place an unknown value still makes the file damaged: the three types of <see cref="s_onNotesThatSound"/> and
    /// Hand and KeyMode where they stand on a note correction, a performance or the assembly.
    /// </summary>
    public static TheoryData<string, string> RefusedValues
    {
        get
        {
            TheoryData<string, string> rows = Rows(row => s_onNotesThatSound.Contains(row.Type));
            rows.Add("Hand", "edits/0/added/hand");
            rows.Add("Hand", "performances/0/taken/0/added/0/hand");
            rows.Add("Hand", "performances/0/merge/bars/1/own/added/0/hand");
            rows.Add("Hand", "assembly/applied/notes/0/hand");
            rows.Add("KeyMode", "assembly/applied/keys/0/mode");
            return rows;
        }
    }

    private static TheoryData<string, string> Rows(Func<(string Type, string Path), bool> which)
    {
        var rows = new TheoryData<string, string>();
        foreach (object[] row in EnumValues)
        {
            if (which(((string)row[0], (string)row[1])))
            {
                rows.Add((string)row[0], (string)row[1]);
            }
        }

        return rows;
    }

    /// <summary>The list of the file an entity on the path stands in, as <see cref="UnknownEntity.List"/> names it, and its place.</summary>
    private static (string List, int Index) EntityOf(string path)
    {
        string[] parts = path.Split('/');
        int at = Array.FindIndex(parts, part => int.TryParse(part, out _));
        return (string.Join('.', parts[..at]), int.Parse(parts[at], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static JsonNode At(JsonNode root, string path)
    {
        JsonNode node = root;
        foreach (string part in path.Split('/'))
        {
            node = int.TryParse(part, out int index) ? node[index]! : node[part]!;
        }

        return node;
    }

    private static IEnumerable<UnknownEntity> AllUnknown(Project project) => [.. project.Unknown, .. project.Marks.Unknown, .. project.DocumentMarks.Unknown];

    private static byte[] File(string name) => System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", name));

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n");

    private static JsonObject Node(byte[] bytes) => JsonNode.Parse(bytes)!.AsObject();

    private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    private static JsonObject Parent(JsonNode root, string path, out string last)
    {
        string[] parts = path.Split('/');
        JsonNode node = root;
        foreach (string part in parts[..^1])
        {
            node = int.TryParse(part, out int index) ? node[index]! : node[part]!;
        }

        last = parts[^1];
        return node.AsObject();
    }

    /// <summary>Compares the writer's answer with its golden, or writes the golden when asked to.</summary>
    private static void AssertWritten(byte[] written, string golden, [CallerFilePath] string source = "")
    {
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            System.IO.File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(source)!, golden), written);
            return;
        }

        Assert.Equal(Text(File(golden)), Text(written));
    }

    [Theory]
    [InlineData(Paterlini)]
    [InlineData(Einaudi)]
    public void A_version_12_file_opens_and_is_written_as_version_15_with_nothing_else_changed(string name)
    {
        byte[] original = File($"{name}.motiff.json");

        Project project = ProjectFile.Read(original);
        OpenedProject opened = OpenedProject.Open(project);
        byte[] written = ProjectFile.Write(project);

        AssertWritten(written, $"{name}.written.motiff.json");
        JsonObject before = Node(original);
        JsonObject after = Node(written);
        Assert.Equal(12, before["version"]!.GetValue<int>());
        Assert.Equal(ProjectFile.Version, after["version"]!.GetValue<int>());
        // Version 13 added the performances; nothing else of a version 12 file changes on the way through.
        Assert.Equal("[]", after["performances"]!.ToJsonString());
        before["version"] = ProjectFile.Version;
        after.Remove("performances");
        Assert.True(JsonNode.DeepEquals(before, after), "a version 12 file written again keeps every field it had");
        Assert.Equal(written, ProjectFile.Write(ProjectFile.Read(written)));
        Assert.NotEmpty(opened.Notes);
        Assert.All(opened.Notes, note => Assert.Equal(Provenance.Detected, note.Attribution.Provenance));
    }

    [Fact]
    public void The_paterlini_file_keeps_its_motif_and_resume_points()
    {
        Project project = ProjectFile.Read(File($"{Paterlini}.motiff.json"));
        OpenedProject opened = OpenedProject.Open(project);

        Assert.Equal("p_048b2ed641a249b4bdad47b006c83058", project.Id);
        Assert.Equal("transkun 2.0.1", project.Transcription!.Engine);
        Assert.Null(project.Transcription.Bars);
        Assert.Equal(new[] { "r_0001", "r_0002" }, project.ResumePoints.Select(point => point.Id));
        Assert.Null(project.Check);
        // Before version 12 there were no Check flags; a version 12 file that has none gets them worked out on opening.
        Assert.NotNull(opened.Project.Check);
        Motif motif = Assert.Single(opened.Motifs);
        Assert.Equal(new[] { "n_0001", "n_0002", "n_0003" }, motif.Selection.NoteIds);
    }

    [Fact]
    public void The_einaudi_file_keeps_the_bars_worked_out_for_its_transcription()
    {
        Project project = ProjectFile.Read(File($"{Einaudi}.motiff.json"));
        OpenedProject opened = OpenedProject.Open(project);

        Assert.Equal("p_c23a6f74c9334f77ae14b938ee7ffdb7", project.Id);
        BarGrid bars = project.Transcription!.Bars!;
        Assert.Equal((3893L, 114_583, "4/4", 38, 1126), (bars.Origin.Value, bars.SixteenthMicroseconds, bars.Signature.ToString(), bars.BarStarts.Count, bars.Beats.Count));
        Assert.Empty(project.Motifs);
        Assert.Single(project.ResumePoints);
        Assert.True(opened.Piece.Meter.Changes.Count > 1, "the bars follow the playing, so the meter changes where a short bar moved the downbeat");
    }

    [Fact]
    public void The_version_15_file_reads_opens_and_is_written_back_unchanged()
    {
        byte[] golden = File(Worked);

        Project project = ProjectFile.Read(golden);
        OpenedProject opened = OpenedProject.Open(project);

        // The current version is its own golden: when the format moves on, the answer goes in a file of its own and this one stays.
        Assert.Equal(Text(golden), Text(ProjectFile.Write(project)));
        Assert.Equal(ProjectFile.Version, Node(golden)["version"]!.GetValue<int>());
        Assert.Equal(3, opened.Motifs.Count);
        Assert.Equal(11, opened.Variations.Count);
        Assert.Equal(3, project.Assembly.Steps.Count);
        Assert.NotNull(project.Assembly.Applied);
        SavedPerformance performance = Assert.Single(project.Performances);
        Assert.Equal(2, performance.Merge!.Decisions.Count);
        Assert.Contains(opened.Notes, note => note.Id == "n_added_0001");
        Assert.Equal("rhythm.anacrusis", Assert.Single(project.Observations).FactId);
    }

    [Theory]
    [MemberData(nameof(EnumValues))]
    public void The_version_15_file_holds_a_known_value_of_each_enum_type_where_the_future_file_changes_it(string type, string path)
    {
        Type found = Assert.Single(typeof(ProjectFile).Assembly.GetTypes(), candidate => candidate.IsEnum && candidate.Name == type);
        string value = Parent(Node(File(Worked)), path, out string last)[last]!.GetValue<string>();

        Assert.Contains(value, Enum.GetNames(found), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_future_file_is_the_version_15_file_with_version_16_two_unknown_fields_and_an_unknown_value_of_each_enum_type()
    {
        JsonObject expected = Node(File(Worked));
        expected["version"] = 16;
        Assert.Equal(24, EnumValues.Count);
        Assert.Equal(24, EnumValues.Select(row => (string)row[0]).Distinct().Count());
        foreach (object[] row in EnumValues)
        {
            Parent(expected, (string)row[1], out string last)[last] = UnknownValue;
        }

        expected["future"] = new JsonObject { ["note"] = "A field a later version adds; today's reader does not know it." };
        expected["motifs"]![0]!["future"] = "A field a later version adds to a motif.";

        Assert.True(JsonNode.DeepEquals(expected, Node(File(Future))), "the future file is the version 15 file with exactly these changes");
    }

    /// <summary>
    /// TODAY'S BEHAVIOUR, which read-only opening (basis rule S6, a later task) is meant to flip: a file of a later version
    /// is refused whole before any field is read (council fact V3). The tolerant reader for enum values (#313) does not
    /// change it: the file's version stays the reader's gate.
    /// </summary>
    [Fact]
    public void Today_the_future_file_is_refused_as_saved_by_a_newer_version()
    {
        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(File(Future)));

        Assert.Equal("This project was saved by a newer version of Motiff Lab. Update the app and try again.", refused.Message);
    }

    /// <summary>
    /// The tolerant reader (S6, #313), flipping council fact V3: with the version a reader knows, an unknown value of 21 of
    /// the 24 enum types no longer makes the file damaged. The entity that holds it is kept as unknown, out of the
    /// project's lists, and written back unchanged in its place, so the file read and written again is the file it was.
    /// </summary>
    [Theory]
    [MemberData(nameof(KeptValues))]
    public void One_unknown_enum_value_opens_and_its_entity_is_written_back_unchanged(string type, string path)
    {
        JsonObject file = Node(File(Worked));
        Parent(file, path, out string last)[last] = UnknownValue;
        (string list, int index) = EntityOf(path);

        Project project = ProjectFile.Read(Bytes(file));
        OpenedProject opened = OpenedProject.Open(project);
        byte[] written = ProjectFile.Write(project);

        UnknownEntity kept = Assert.Single(AllUnknown(project), entity => entity.Enum is not null);
        Assert.Equal((list, index, type), (kept.List, kept.Index, kept.Enum));
        Assert.True(JsonNode.DeepEquals(At(file, $"{list.Replace('.', '/')}/{index}"), JsonNode.Parse(kept.Json)), "the unknown entity is kept as the file held it");
        Assert.True(JsonNode.DeepEquals(file, Node(written)), "the file written back is the file read, the unknown value and its entity included");
        Assert.Equal(written, ProjectFile.Write(ProjectFile.Read(written)));
        Assert.NotEmpty(opened.Notes);
    }

    /// <summary>
    /// What stays as it was (Andrei's decision on #313, option A): an unknown value on a note correction, a performance or
    /// the assembly changes the notes that sound, so the file is still refused as damaged; read-only opening takes it up.
    /// </summary>
    [Theory]
    [MemberData(nameof(RefusedValues))]
    public void An_unknown_value_on_the_notes_that_sound_still_makes_the_file_damaged(string type, string path)
    {
        JsonObject file = Node(File(Worked));
        Parent(file, path, out string last)[last] = UnknownValue;

        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Bytes(file)));

        Assert.StartsWith("This project file is damaged", refused.Message);
        Assert.NotEmpty(type);
    }

    /// <summary>
    /// The enum part of the future file (criterion 3 of #313, as option A amends it): with the version this build reads, and
    /// the three values on the notes that sound put back, every other unknown value opens and is written back; an unknown
    /// field is still lost (council fact V2, a later task) unless it stands in an entity kept whole as unknown.
    /// </summary>
    [Fact]
    public void The_enum_part_of_the_future_file_opens_with_this_version()
    {
        JsonObject file = Node(File(Future));
        JsonObject worked = Node(File(Worked));
        file["version"] = ProjectFile.Version;
        foreach (object[] row in RefusedValues)
        {
            if (Parent(worked, (string)row[1], out string known)[known] is JsonNode value)
            {
                Parent(file, (string)row[1], out string last)[last] = value.DeepClone();
            }
        }

        Project project = ProjectFile.Read(Bytes(file));
        OpenedProject opened = OpenedProject.Open(project);
        JsonObject written = Node(ProjectFile.Write(project));

        // All three motifs hold or own an unknown value, so they and their eleven variations are kept unknown.
        Assert.Empty(opened.Motifs);
        Assert.Empty(opened.Variations);
        Assert.Equal(
            KeptValues.Select(row => EntityOf((string)row[1])).Distinct().Order(),
            AllUnknown(project).Where(entity => entity.Enum is not null).Select(entity => (entity.List, entity.Index)).Order());
        Assert.Contains(opened.Notes, note => note.Id == "n_added_0001");
        Assert.Single(project.Performances);
        // The unknown top-level field is lost; the motif's unknown field stays, because the motif is kept whole as unknown.
        file.Remove("future");
        Assert.True(JsonNode.DeepEquals(file, written), "everything but the unknown top-level field is written back");
    }

    /// <summary>
    /// Council fact V2, run rather than read from the code: the reader tolerates an unknown field and the writer drops it.
    /// TODAY'S BEHAVIOUR, which preserving unknown fields (basis rule S6) is meant to flip.
    /// </summary>
    [Fact]
    public void Today_an_unknown_field_in_a_version_15_file_is_read_and_then_lost_when_the_file_is_written()
    {
        JsonObject file = Node(File(Worked));
        file["future"] = new JsonObject { ["note"] = "A field a later version adds." };
        file["motifs"]![0]!["future"] = "A field a later version adds to a motif.";

        Project project = ProjectFile.Read(Bytes(file));
        string written = Text(ProjectFile.Write(project));

        Assert.DoesNotContain("future", written, StringComparison.Ordinal);
        Assert.Equal(Text(File(Worked)), written);
    }
}