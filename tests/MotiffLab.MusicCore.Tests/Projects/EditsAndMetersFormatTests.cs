using System.Text;
using System.Text.Json.Nodes;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Tests.Import.Transcription;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// The format step of version 0.5 within version 15 (v0.5 data design, section 9; Andrei, 2026-11-12): "editsOff" at the
/// top level, "meters" in a recording's bars and "sentFromMs", where in the recording file the stretch heard started.
/// Optional fields, so versions 0.2 to 0.4 open the file and drop them on writing; a pull keeps this device's meters and
/// stretch start when the online copy lacks them and its notes are the same import.
/// </summary>
public class EditsAndMetersFormatTests
{
    private const string Golden = "v15-edits-meters.motiff.json";

    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSignature s_seven = new(7, 8);

    /// <summary>The stretch heard started 12 min 34 s into the recording file.</summary>
    private const long SentFrom = 754_000;

    /// <summary>Four bars of 4/4, then four of 7/8 (4 + 3 eighths), played at 120 BPM a second in: (16th, length in 16ths, pitch).</summary>
    private static IEnumerable<(int, int, int)> Played()
    {
        // The march's first four bars: ten notes a bar.
        foreach ((int, int, int) note in BarGridTests.March().Take(40))
        {
            yield return note;
        }

        for (int bar = 0; bar < 4; bar++)
        {
            int start = 64 + (bar * 14);
            yield return (start, 8, 38);
            yield return (start + 8, 6, 45);
            for (int i = 0; i < 7; i++)
            {
                yield return (start + (2 * i), 2, 74 + (i % 4));
            }
        }
    }

    /// <summary>The bars of <see cref="Played"/>: 4/4 from the first 16th, 7/8 from the 64th.</summary>
    private static BarGrid Bars() => new(new Tick(960), 125_000, TimeSignature.CommonTime, [0, 64], null, 4) { Meters = [new GridMeter(64, s_seven)] };

    /// <summary>A recording of a piece that changes signature, with its first note corrected away and the corrections off.</summary>
    private static Project Heard()
    {
        byte[] midi = LivePlaying.Heard(LivePlaying.At(1_000_000, 125_000, Played()));
        Project project = Project.Create("p_6c1d2e3f4a5b4c6d9e0f1a2b3c4d5e6f", "Take.mid", midi, s_now) with
        {
            Transcription = new TranscriptionOrigin("transkun 2", Bars()) { SentFromMilliseconds = SentFrom },
        };
        IReadOnlyList<Note> notes = OpenedProject.Open(project).Notes;
        return project with { Edits = NoteEditLog.Empty.Remove(notes, notes[0].Id), EditsOff = true };
    }

    private static Project RoundTrip(Project project) => ProjectFile.Read(ProjectFile.Write(project));

    private static Project Read(JsonNode file) => ProjectFile.Read(Encoding.UTF8.GetBytes(file.ToJsonString()));

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n");

    private static string GoldenFolder([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(source))!, "Golden");

    private static byte[] Kept(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", name));

    [Fact]
    public void The_file_version_0_5_writes_is_the_golden_and_reads_back_unchanged()
    {
        byte[] written = ProjectFile.Write(Heard());
        if (Environment.GetEnvironmentVariable("MOTIFF_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllBytes(Path.Combine(GoldenFolder(), Golden), written);
            return;
        }

        byte[] kept = Kept(Golden);
        Assert.Equal(Text(kept), Text(written));
        Assert.Equal(Text(kept), Text(ProjectFile.Write(ProjectFile.Read(kept))));
    }

    [Fact]
    public void The_golden_holds_each_new_field_and_opens_with_its_bars_and_without_its_corrections()
    {
        Project project = ProjectFile.Read(Kept(Golden));

        Assert.True(project.EditsOff);
        Assert.Equal(new GridMeter(64, s_seven), Assert.Single(project.Transcription!.Bars!.Meters));
        Assert.Single(project.Edits.Edits);
        Assert.Equal(SentFrom, project.Transcription.SentFromMilliseconds);

        OpenedProject off = OpenedProject.Open(project, EditsLayer.Off);
        Bar[] bars = [.. off.Piece.Meter.BarsBefore(off.Piece.End)];
        Assert.Equal(8, bars.Length);
        Assert.All(bars[..4], bar => Assert.Equal(TimeSignature.CommonTime, bar.Signature));
        Assert.All(bars[4..], bar => Assert.Equal((s_seven, 14 * BarGrid.StepTicks), (bar.Signature, bar.Span.Duration)));
        Assert.Equal(OpenedProject.Open(project).Notes.Count + 1, off.Notes.Count);
        Assert.Equal((7, 8), off.Piece.Midi!.TimeSignatures.Where(meter => meter.At.Value >= 64 * BarGrid.StepTicks).Select(meter => (meter.Numerator, meter.Denominator)).Distinct().Single());
    }

    /// <summary>
    /// What versions 0.2 to 0.4 do with the file, short of running them: it is version 15, and without the new fields it is
    /// the same notes at the same ticks, on bars laid from the same starts in the grid's one signature, the corrections on.
    /// </summary>
    [Fact]
    public void Without_the_new_fields_the_file_is_the_same_notes_on_bars_from_the_same_starts()
    {
        Project project = Heard();
        JsonObject file = JsonNode.Parse(ProjectFile.Write(project))!.AsObject();
        _ = file.Remove("editsOff");
        _ = file["transcription"]!["bars"]!.AsObject().Remove("meters");
        _ = file["transcription"]!.AsObject().Remove("sentFromMs");

        Project old = Read(file);

        Assert.False(old.EditsOff);
        Assert.Null(old.Transcription!.SentFromMilliseconds);
        Assert.Empty(old.Transcription!.Bars!.Meters);
        Assert.Equal(project.Transcription!.Bars! with { Meters = [] }, old.Transcription.Bars);
        Assert.Equal(OpenedProject.Open(project).Notes, OpenedProject.Open(old).Notes);
        MeterMap meter = OpenedProject.Open(old).Piece.Meter;
        Assert.Equal(new Tick(64 * BarGrid.StepTicks), meter.BarAt(new Tick(64 * BarGrid.StepTicks)).Span.Onset);
        Assert.All(meter.Changes, change => Assert.Equal(TimeSignature.CommonTime, change.Signature));
    }

    [Fact]
    public void Edits_off_is_written_only_when_true_and_a_value_of_another_kind_is_damage()
    {
        Project on = Heard() with { EditsOff = false };
        Assert.False(JsonNode.Parse(ProjectFile.Write(on))!.AsObject().ContainsKey("editsOff"));
        Assert.True(RoundTrip(on with { EditsOff = true }).EditsOff);

        JsonObject file = JsonNode.Parse(ProjectFile.Write(on))!.AsObject();
        file["editsOff"] = false;
        Assert.False(Read(file).EditsOff);
        foreach (JsonNode? broken in new JsonNode?[] { "true", 1, null, new JsonObject() })
        {
            file["editsOff"] = broken;
            Assert.Contains("editsOff", Assert.Throws<ProjectFileException>(() => Read(file)).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Where_the_stretch_heard_started_is_kept_from_0_on_and_any_other_value_is_damage()
    {
        Project heard = Heard();
        Assert.Equal(0, RoundTrip(heard with { Transcription = heard.Transcription! with { SentFromMilliseconds = 0 } }).Transcription!.SentFromMilliseconds);
        Project unknown = heard with { Transcription = heard.Transcription! with { SentFromMilliseconds = null } };
        Assert.False(JsonNode.Parse(ProjectFile.Write(unknown))!["transcription"]!.AsObject().ContainsKey("sentFromMs"));
        Assert.Null(RoundTrip(unknown).Transcription!.SentFromMilliseconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => heard.Transcription! with { SentFromMilliseconds = -1 });
        Assert.NotEqual(heard.Transcription, unknown.Transcription);

        JsonObject file = JsonNode.Parse(ProjectFile.Write(heard))!.AsObject();
        foreach (JsonNode? broken in new JsonNode?[] { -1, 1.5, "754000", null })
        {
            file["transcription"]!["sentFromMs"] = broken;
            Assert.Contains("sentFromMs", Assert.Throws<ProjectFileException>(() => Read(file)).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_change_of_signature_that_is_not_well_formed_is_damage()
    {
        JsonObject file = JsonNode.Parse(ProjectFile.Write(Heard()))!.AsObject();
        JsonObject bars = file["transcription"]!["bars"]!.AsObject();
        static JsonObject Meter(int at, string signature) => new() { ["at"] = at, ["signature"] = signature };
        foreach (JsonNode broken in new JsonNode[]
        {
            new JsonObject(),
            new JsonArray(Meter(0, "7/8")),
            new JsonArray(Meter(63, "7/8")),
            new JsonArray(Meter(64, "7/x")),
            new JsonArray(Meter(64, "7/0")),
            new JsonArray(Meter(64, "7/32")),
            new JsonArray(Meter(64, "7/8"), Meter(64, "3/4")),
            new JsonArray(new JsonObject { ["at"] = 64 }),
            new JsonArray(7),
        })
        {
            bars["meters"] = broken;
            Assert.Throws<ProjectFileException>(() => Read(file));
        }

        bars["meters"] = new JsonArray();
        Assert.Empty(Read(file).Transcription!.Bars!.Meters);
    }

    [Fact]
    public void A_grid_keeps_its_changes_at_its_later_bar_starts_and_moves_them_with_its_bars()
    {
        BarGrid plain = Bars() with { Meters = [] };
        Assert.Throws<ArgumentException>(() => plain with { Meters = [new GridMeter(0, s_seven)] });
        Assert.Throws<ArgumentException>(() => plain with { Meters = [new GridMeter(32, s_seven)] });
        Assert.Throws<ArgumentException>(() => plain with { Meters = [new GridMeter(64, new TimeSignature(7, 32))] });

        BarGrid grid = Bars();
        Assert.Equal((TimeSignature.CommonTime, TimeSignature.CommonTime, s_seven, s_seven), (grid.SignatureAt(0), grid.SignatureAt(63), grid.SignatureAt(64), grid.SignatureAt(500)));
        Assert.NotEqual(plain, grid);
        Assert.Equal(Bars(), grid);
        Assert.Equal([new GridMeter(68, s_seven)], grid.Shifted(1).Meters);
    }

    [Fact]
    public void A_pull_keeps_this_devices_meters_when_the_online_copy_has_the_same_bars_without_them()
    {
        Project here = Heard();
        Project fromOldBuild = RoundTrip(here with
        {
            Transcription = here.Transcription! with { Bars = Bars() with { Meters = [] }, SentFromMilliseconds = null },
            EditsOff = false,
            Metadata = new PieceMetadata("Take (edited)", null, [], []),
        });

        Project pulled = ProjectSync.Pulled(fromOldBuild, here);
        Assert.Equal("Take (edited)", pulled.Metadata.Title);
        Assert.Equal(Bars().Meters, pulled.Transcription!.Bars!.Meters);
        Assert.Equal(SentFrom, pulled.Transcription.SentFromMilliseconds);
        Assert.Equal(1_000, ProjectSync.Pulled(RoundTrip(fromOldBuild with { Transcription = fromOldBuild.Transcription! with { SentFromMilliseconds = 1_000 } }), here).Transcription!.SentFromMilliseconds);

        // Whether the corrections are off is the copy saved last; bars found again online are not given this device's meters.
        Assert.False(pulled.EditsOff);
        BarGrid other = new(new Tick(960), 125_000, new TimeSignature(3, 4), [0, 64], null, 4);
        Project rebarred = RoundTrip(fromOldBuild with { Transcription = here.Transcription with { Bars = other } });
        Assert.Empty(ProjectSync.Pulled(rebarred, here).Transcription!.Bars!.Meters);
        Project readAgain = RoundTrip(fromOldBuild with { Source = [.. here.Source, 0] });
        Assert.Empty(ProjectSync.Pulled(readAgain, here).Transcription!.Bars!.Meters);
        Assert.Null(ProjectSync.Pulled(readAgain, here).Transcription!.SentFromMilliseconds);
    }

    /// <summary>
    /// Every field a project file holds has its class for the merge (0026 S8): the top-level fields of every golden this
    /// build reads and of the files it writes, and the fields within one that a version added on its own.
    /// </summary>
    [Fact]
    public void Every_field_of_the_file_has_a_class_for_the_merge()
    {
        string[] goldens = [.. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Golden"), "v1*.motiff.json")
            .Select(path => Path.GetFileName(path)).Where(name => !name.StartsWith("v16-", StringComparison.Ordinal))];
        Assert.Contains(Golden, goldens);
        IEnumerable<JsonObject> files = goldens.Select(name => JsonNode.Parse(Kept(name))!.AsObject())
            .Append(JsonNode.Parse(ProjectFile.Write(Heard()))!.AsObject());
        foreach (JsonObject file in files)
        {
            foreach (string field in file.Select(entry => entry.Key))
            {
                Assert.True(ProjectSync.Fields.ContainsKey(field), $"\"{field}\" has no class in ProjectSync.Fields.");
            }
        }

        Assert.Equal(FieldClass.Import, ProjectSync.Fields["transcription.bars.meters"]);
        Assert.Equal(FieldClass.Import, ProjectSync.Fields["transcription.sentFromMs"]);
        Assert.Equal(FieldClass.Import, ProjectSync.Fields["scan.pages"]);
        Assert.Equal(FieldClass.Bookkeeping, ProjectSync.Fields["editsOff"]);
    }
}