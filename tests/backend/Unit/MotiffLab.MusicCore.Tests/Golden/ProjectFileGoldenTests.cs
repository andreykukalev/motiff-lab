using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
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
    /// <summary>The worked file as version 0.5 wrote it, in version 15: an input, never rewritten.</summary>
    private const string Worked15 = "v15-worked.motiff.json";

    /// <summary>What versions 0.6 to 1.0 made of <see cref="Worked15"/>, in version 16: an input since version 17, never rewritten.</summary>
    private const string Worked16 = "v16-worked.motiff.json";

    /// <summary>What today's writer makes of <see cref="Worked15"/> and <see cref="Worked16"/>, in version 17.</summary>
    private const string Worked = "v17-worked.motiff.json";

    /// <summary>The future file made while version 15 was today's: the version 15 file with version 16.</summary>
    private const string Future15 = "v16-future.motiff.json";

    /// <summary>The future file made while version 16 was today's: the version 16 file with version 17.</summary>
    private const string Future16 = "v17-future.motiff.json";

    /// <summary>The future file of today: the version 17 file with version 18.</summary>
    private const string Future = "v18-future.motiff.json";

    /// <summary>
    /// Where the future file puts an unknown value of each enum type the reader names (council fact V28: 24 types then;
    /// today 31 types behind the 36 uses of Name and LowerName in Projects/File/), as a path into the version 16 and 17 files:
    /// the working notes are the first Notes, the compared ones the second.
    /// </summary>
    public static TheoryData<string, string> EnumValues => new()
    {
        { "AccentPattern", "workspace/variations/3/steps/0/parameters/pattern" },
        { "AccompanimentPatternKind", "workspace/variations/5/steps/0/parameters/kind" },
        { "ArticulationKind", "workspace/variations/1/steps/0/parameters/kind" },
        { "BassPatternKind", "workspace/variations/7/steps/0/parameters/kind" },
        { "CheckReason", "notes/0/check/0/reason" },
        { "CheckSource", "notes/0/check/0/source" },
        { "DensityKind", "workspace/variations/6/steps/0/parameters/kind" },
        { "Hand", "workspace/motifs/0/filter/hand" },
        { "HandEditKind", "workspace/variations/10/pending/parameters/kind" },
        { "InkColour", "workspace/marks/highlights/0/colour" },
        { "InkTool", "workspace/marks/ink/0/tool" },
        { "KeyMode", "workspace/variations/2/key/mode" },
        { "MergeChoice", "notes/1/performance/merge/bars/0/choice" },
        { "MotifMode", "workspace/motifs/0/mode" },
        { "MotifSelectionKind", "workspace/motifs/0/kind" },
        { "OperationScope", "workspace/variations/8/steps/0/parameters/scope" },
        { "OrnamentKind", "workspace/variations/2/steps/0/parameters/kind" },
        { "PassingNoteKind", "workspace/variations/4/steps/0/parameters/kind" },
        { "PatternUnit", "workspace/variations/6/steps/0/parameters/unit" },
        { "PerformanceSource", "notes/1/performance/source" },
        { "Provenance", "workspace/assembly/applied/notes/0/attribution/provenance" },
        { "ReharmoniseKind", "workspace/variations/9/steps/0/parameters/kind" },
        { "RhythmReshapeKind", "workspace/variations/0/steps/0/parameters/kind" },
        { "VoicingKind", "workspace/variations/8/steps/0/parameters/kind" },
    };

    /// <summary>The same place in a version 15 file, where everything stood at the top level.</summary>
    private static string Path15(string path) =>
        path.StartsWith("workspace/", StringComparison.Ordinal) ? path["workspace/".Length..]
        : path.StartsWith("notes/0/", StringComparison.Ordinal) ? path["notes/0/".Length..]
        : path.StartsWith("notes/1/performance/", StringComparison.Ordinal) ? "performances/0/" + path["notes/1/performance/".Length..]
        : path;

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
            rows.Add("Hand", "notes/0/edits/0/added/hand");
            rows.Add("Hand", "notes/1/performance/taken/0/added/0/hand");
            rows.Add("Hand", "notes/1/performance/merge/bars/1/own/added/0/hand");
            rows.Add("Hand", "workspace/assembly/applied/notes/0/hand");
            rows.Add("KeyMode", "workspace/assembly/applied/keys/0/mode");
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

    /// <summary>
    /// The list of the file an entity on the path stands in, as <see cref="UnknownEntity.List"/> names it, its place, and
    /// the path of the entity itself.
    /// </summary>
    private static (string List, int Index) EntityOf(string path) => EntityOf(path, out _);

    private static (string List, int Index) EntityOf(string path, out string entity)
    {
        string[] parts = path.Split('/');
        int from = parts[0] == "workspace" ? 1 : parts[0] == "notes" ? 2 : 0;
        int at = Array.FindIndex(parts, from, part => int.TryParse(part, out _));
        entity = string.Join('/', parts[..(at + 1)]);
        return (string.Join('.', parts[from..at]), int.Parse(parts[at], System.Globalization.CultureInfo.InvariantCulture));
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
    public void A_version_12_file_opens_and_is_written_as_this_version_with_nothing_else_changed(string name)
    {
        byte[] original = File($"{name}.motiff.json");

        Project project = ProjectFile.Read(original);
        OpenedProject opened = OpenedProject.Open(project);
        byte[] written = ProjectFile.Write(project);

        AssertWritten(written, $"{name}.written.motiff.json");
        JsonObject before = Node(original);
        Assert.Equal(12, before["version"]!.GetValue<int>());
        Assert.Equal(ProjectFile.Version, Node(written)["version"]!.GetValue<int>());
        // Laid out at the top level again: version 13 added the performances and version 15 the originals' descriptions,
        // which versions 16 and 17 write for every original; nothing else of a version 12 file changes on the way through.
        JsonObject after = Node(Projects.LegacyFile.Bytes(written, 12));
        Assert.Equal("[]", after["performances"]!.ToJsonString());
        Assert.Equal(project.Originals.Select(original => original.Id), after["originals"]!.AsArray().Select(original => original!["id"]!.GetValue<string>()));
        after.Remove("performances");
        after.Remove("originals");
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

    /// <summary>
    /// The version 15 file is written as this version, and laid out at the top level again it is the version 15 file, every
    /// field in the order the version 15 writer wrote them: the conversion moves every field and loses none.
    /// </summary>
    [Fact]
    public void The_version_15_file_reads_and_is_written_as_the_file_of_this_version_that_is_it_laid_out_anew()
    {
        byte[] earlier = File(Worked15);

        byte[] written = ProjectFile.Write(ProjectFile.Read(earlier));

        AssertWritten(written, Worked);
        Assert.Equal(15, Node(earlier)["version"]!.GetValue<int>());
        JsonObject flat = Node(Projects.LegacyFile.Bytes(written, 15));
        // Version 16 describes every original; the version 15 file left the ones the piece shows to be worked out.
        Assert.False(Node(earlier).ContainsKey("originals"));
        Assert.Equal(ProjectFile.Read(earlier).Originals.Select(original => original.Id), flat["originals"]!.AsArray().Select(original => original!["id"]!.GetValue<string>()));
        flat.Remove("originals");
        Assert.True(JsonNode.DeepEquals(Node(earlier), flat), "the file of this version laid out at the top level is the version 15 file");
        Assert.Equal(Node(earlier).Select(field => field.Key), flat.Select(field => field.Key));
    }

    /// <summary>
    /// Version 17 keeps the layout of version 16 (a piece may be only its recording, and the drafts come with their step):
    /// the version 16 file reads and is written as the version 17 file, which is it with the version raised and nothing else.
    /// </summary>
    [Fact]
    public void The_version_16_file_reads_and_is_written_as_the_version_17_file_with_only_the_version_changed()
    {
        byte[] earlier = File(Worked16);

        byte[] written = ProjectFile.Write(ProjectFile.Read(earlier));

        AssertWritten(written, Worked);
        JsonObject raised = Node(earlier);
        Assert.Equal(16, raised["version"]!.GetValue<int>());
        raised["version"] = 17;
        Assert.True(JsonNode.DeepEquals(raised, Node(written)), "the version 17 file is the version 16 file with version 17");
    }

    [Fact]
    public void The_version_17_file_reads_opens_and_is_written_back_unchanged()
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
    public void The_version_17_file_holds_a_known_value_of_each_enum_type_where_the_future_file_changes_it(string type, string path)
    {
        Type found = Assert.Single(typeof(ProjectFile).Assembly.GetTypes(), candidate => candidate.IsEnum && candidate.Name == type);
        string value = Parent(Node(File(Worked)), path, out string last)[last]!.GetValue<string>();

        Assert.Contains(value, Enum.GetNames(found), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(value, Parent(Node(File(Worked15)), Path15(path), out string earlier)[earlier]!.GetValue<string>());
    }

    [Fact]
    public void The_future_file_is_the_version_17_file_with_version_18_three_unknown_fields_and_an_unknown_value_of_each_enum_type()
    {
        Assert.Equal(24, EnumValues.Count);
        Assert.Equal(24, EnumValues.Select(row => (string)row[0]).Distinct().Count());

        Assert.True(JsonNode.DeepEquals(Futured(Node(File(Worked)), 18, path => path), Node(File(Future))), "the future file is the version 17 file with exactly these changes");
    }

    /// <summary>The future file made while version 16 was today's is the version 16 file with the same changes.</summary>
    [Fact]
    public void The_future_file_of_version_16_is_the_version_16_file_with_version_17_three_unknown_fields_and_an_unknown_value_of_each_enum_type() =>
        Assert.True(JsonNode.DeepEquals(Futured(Node(File(Worked16)), 17, path => path), Node(File(Future16))), "the future file of version 16 is the version 16 file with exactly these changes");

    /// <summary>The future file made while version 15 was today's is the version 15 file with the same changes in its places.</summary>
    [Fact]
    public void The_earlier_future_file_is_the_version_15_file_with_version_16_two_unknown_fields_and_an_unknown_value_of_each_enum_type()
    {
        JsonObject expected = Futured(Node(File(Worked15)), 16, Path15);

        Assert.True(JsonNode.DeepEquals(expected, Node(File(Future15))), "the earlier future file is the version 15 file with exactly these changes");
    }

    /// <summary><paramref name="file"/> with the version, the unknown fields and the unknown value of each enum type of a future file.</summary>
    private static JsonObject Futured(JsonObject file, int version, Func<string, string> at)
    {
        file["version"] = version;
        foreach (object[] row in EnumValues)
        {
            Parent(file, at((string)row[1]), out string last)[last] = UnknownValue;
        }

        file["future"] = new JsonObject { ["note"] = "A field a later version adds; today's reader does not know it." };
        Parent(file, at("workspace/motifs/0/future"), out string field)[field] = "A field a later version adds to a motif.";
        if (file["workspace"] is JsonObject workspace)
        {
            workspace["future"] = "A field a later version adds to the workspace.";
        }

        return file;
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
    /// The earlier future file has a version laid out in the three layers and the layout of version 15, which no build of
    /// version 16 or later writes: it is damaged, for want of its workspace, rather than read as something it is not.
    /// </summary>
    [Fact]
    public void The_earlier_future_file_now_has_a_version_of_the_layers_in_another_layout_and_is_damaged()
    {
        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(File(Future15)));

        Assert.StartsWith("This project file is damaged", refused.Message);
    }

    /// <summary>
    /// The future file of version 16 now has this version: its unknown values on the notes that sound make it damaged, as
    /// any file of this version with them (Andrei's decision on #313, option A).
    /// </summary>
    [Fact]
    public void The_future_file_of_version_16_now_has_this_version_and_is_damaged_by_its_values_on_the_notes_that_sound()
    {
        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(File(Future16)));

        Assert.StartsWith("This project file is damaged", refused.Message);
    }

    /// <summary>
    /// A version 16 file, as builds 0.6 to 1.0 saved it, reads an unknown value as those builds did: the entity holding it
    /// is kept as unknown and written back where it stood, the file written being the file read with version 17 and
    /// nothing else changed (docs/project-file.md, "Unknown values").
    /// </summary>
    [Theory]
    [MemberData(nameof(KeptValues))]
    public void An_unknown_value_in_a_version_16_file_is_kept_as_in_a_file_of_this_version(string type, string path)
    {
        JsonObject file = Node(File(Worked16));
        Parent(file, path, out string last)[last] = UnknownValue;
        (string list, int index) = EntityOf(path, out string entityPath);

        Project project = ProjectFile.Read(Bytes(file));
        JsonObject written = Node(ProjectFile.Write(project));

        UnknownEntity kept = Assert.Single(AllUnknown(project), entity => entity.Enum is not null);
        Assert.Equal((list, index, type), (kept.List, kept.Index, kept.Enum));
        Assert.True(JsonNode.DeepEquals(At(file, entityPath), JsonNode.Parse(kept.Json)), "the unknown entity is kept as the file held it");
        file["version"] = ProjectFile.Version;
        Assert.True(JsonNode.DeepEquals(file, written), "the file written back is the version 16 file read, with version 17");
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
        (string list, int index) = EntityOf(path, out string entityPath);

        Project project = ProjectFile.Read(Bytes(file));
        OpenedProject opened = OpenedProject.Open(project);
        byte[] written = ProjectFile.Write(project);

        UnknownEntity kept = Assert.Single(AllUnknown(project), entity => entity.Enum is not null);
        Assert.Equal((list, index, type), (kept.List, kept.Index, kept.Enum));
        Assert.True(JsonNode.DeepEquals(At(file, entityPath), JsonNode.Parse(kept.Json)), "the unknown entity is kept as the file held it");
        Assert.True(JsonNode.DeepEquals(file, Node(written)), "the file written back is the file read, the unknown value and its entity included");
        Assert.Equal(written, ProjectFile.Write(ProjectFile.Read(written)));
        Assert.NotEmpty(opened.Notes);
    }

    /// <summary>
    /// What stays as it was (Andrei's decision on #313, option A): an unknown value on a note correction, a performance or
    /// the assembly changes the notes that sound, so the file is still refused as damaged, in a version 16 file as in a
    /// version 17 one; read-only opening takes it up.
    /// </summary>
    [Theory]
    [MemberData(nameof(RefusedValues))]
    public void An_unknown_value_on_the_notes_that_sound_still_makes_the_file_damaged(string type, string path)
    {
        foreach (string golden in (string[])[Worked16, Worked])
        {
            JsonObject file = Node(File(golden));
            Parent(file, path, out string last)[last] = UnknownValue;

            ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Bytes(file)));

            Assert.StartsWith("This project file is damaged", refused.Message);
        }

        Assert.NotEmpty(type);
    }

    /// <summary>
    /// In a file of an earlier version every value is one the builds of its time knew (docs/project-file.md, "Unknown
    /// values"), so an unknown one there is damage, wherever it stands.
    /// </summary>
    [Theory]
    [MemberData(nameof(EnumValues))]
    public void An_unknown_value_in_a_version_15_file_makes_it_damaged(string type, string path)
    {
        JsonObject file = Node(File(Worked15));
        Parent(file, Path15(path), out string last)[last] = UnknownValue;

        ProjectFileException refused = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Bytes(file)));

        Assert.StartsWith("This project file is damaged", refused.Message);
        Assert.NotEmpty(type);
    }

    /// <summary>
    /// The future file with the version this build reads (criterion 3 of #313, as option A amends it), and the three values
    /// on the notes that sound put back: every other unknown value opens and is written back, and so are the unknown fields
    /// at the top level and in the workspace (S6, version 16 on).
    /// </summary>
    [Fact]
    public void The_future_file_opens_with_this_version_and_is_written_back_whole()
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
        // The unknown fields stay: the top level's and the workspace's kept as fields, the motif's with the motif kept whole.
        Assert.True(JsonNode.DeepEquals(file, written), "the file is written back whole");
    }

    /// <summary>
    /// Basis rule S6, flipping council fact V2 in version 16: an unknown field at the top level, in the workspace, in a Notes,
    /// an original or a provenance is kept and written back after the known ones; one inside a known entity is still lost.
    /// A version 15 file's unknown field is lost as before, since no build that wrote one knew a field this one does not.
    /// A version 16 file keeps them as a version 17 one does, written back with version 17.
    /// </summary>
    [Theory]
    [InlineData(Worked16)]
    [InlineData(Worked)]
    public void An_unknown_field_in_a_file_of_the_layers_is_kept_where_the_layers_keep_them(string golden)
    {
        JsonObject file = Node(File(golden));
        file["future"] = new JsonObject { ["note"] = "A field a later version adds." };
        file["workspace"]!["future"] = 1;
        file["notes"]![0]!["future"] = 2;
        file["notes"]![0]!["provenance"]!["future"] = 3;
        file["notes"]![1]!["future"] = 4;
        file["originals"]![0]!["future"] = 5;
        file["workspace"]!["motifs"]![0]!["future"] = "A field a later version adds to a motif.";

        JsonObject written = Node(ProjectFile.Write(ProjectFile.Read(Bytes(file))));

        _ = file["workspace"]!["motifs"]![0]!.AsObject().Remove("future");
        file["version"] = ProjectFile.Version;
        Assert.True(JsonNode.DeepEquals(file, written), "every unknown field but the motif's is written back");

        JsonObject earlier = Node(File(Worked15));
        earlier["future"] = new JsonObject { ["note"] = "A field a later version adds." };
        Assert.Equal(Text(File(Worked)), Text(ProjectFile.Write(ProjectFile.Read(Bytes(earlier)))));
    }
}