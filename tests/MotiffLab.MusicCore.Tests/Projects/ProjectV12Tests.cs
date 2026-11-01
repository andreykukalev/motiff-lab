using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// Project file version 12 (0018; preparation items 1–6): the marks, bookmarks, resume points, Check flags and
/// settings overrides kept with the piece, and how a version 11 file opens.
/// </summary>
public class ProjectV12Tests
{
    private static readonly DateTimeOffset s_created = new(2026, 10, 23, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_now = new(2026, 11, 1, 14, 0, 0, TimeSpan.FromHours(1));

    private static readonly byte[] s_source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "importer-grand-staff.musicxml"));

    private static ImportedPiece Piece() => PieceImporter.Import("minuet.musicxml", s_source).Piece!;

    private static TickSpan Bars(ImportedPiece piece, int first, int last) => MotifSelector.Bars(piece.Meter, first, last, piece.End);

    private static InkStroke Stroke(string id, Tick bar, InkColour colour = InkColour.Graphite) =>
        new(id, colour == InkColour.Amber ? InkTool.Highlighter : InkTool.Pen, colour, 0.16, bar, 0.25, 1.5, [0.5, 0.25, 0.5, 1.0, 0.5, 0.6], 0.5);

    /// <summary>The minuet with one of everything version 12 keeps.</summary>
    private static Project Worked()
    {
        ImportedPiece piece = Piece();
        TickSpan opening = Bars(piece, 1, 2);
        TickSpan bar3 = Bars(piece, 3, 3);
        string firstNote = piece.Notes[0].Id;
        Project project = Project.Create("p_test", "minuet.musicxml", s_source, s_created)
            .ChangeMarks(marks => marks
                .AddInk([Stroke("k1", opening.Onset), Stroke("k2", bar3.Onset, InkColour.Amber)])
                .AddNote(new MarginNote(marks.NextNoteId(), bar3, "  Breathe here ", s_now))
                .AddHighlight(new Highlight("h_0001", opening, [firstNote], InkColour.Amber))
                .SetFingering(new Fingering(firstNote, 3)))
            .AddCategory(new BookmarkCategory("c_0001", "Exam", InkColour.Ochre))
            .RenameCategory(BookmarkCategory.TeacherId, "Lena")
            .AddBookmark(new Bookmark("b_0001", "Opening", BookmarkCategory.PractiseId, opening, FromPhrase: true, s_now))
            .AddBookmark(new Bookmark("b_0002", "Breathe", "c_0001", bar3, FromPhrase: false, s_now.AddMinutes(1)))
            .AddResumePoint(ResumePoint.Start("r_0001", s_now, "Left hand").Touch(bar3.Onset, new SavedPassage(bar3, SelectionFilter.ForHand(Hand.Left)), true, [Overlay.Insights, Overlay.PianoRoll], 60, Hand.Left, true, s_now.AddMinutes(5)))
            .OverrideSetting("phrases.granularity", "short")
            .OverrideSetting("layers.bookmarks", "off");
        return project with { Check = [new CheckFlag(2, CheckSource.Transcription, CheckReason.TempoUncertain, false), new CheckFlag(4, CheckSource.Transcription, CheckReason.HandsGuessed, true)] };
    }

    [Fact]
    public void Everything_version_12_keeps_round_trips_byte_for_byte()
    {
        Project project = Worked();
        byte[] written = ProjectFile.Write(project);

        Project reread = ProjectFile.Read(written);

        Assert.Equal(12, ProjectFile.Version);
        Assert.Equal(project.Marks, reread.Marks);
        Assert.Equal(project.Categories, reread.Categories);
        Assert.Equal(project.Bookmarks, reread.Bookmarks);
        Assert.Equal(project.ResumePoints, reread.ResumePoints);
        Assert.Equal(project.Check, reread.Check);
        Assert.Equal(project.SettingOverrides.OrderBy(pair => pair.Key), reread.SettingOverrides.OrderBy(pair => pair.Key));
        Assert.Equal(written, ProjectFile.Write(reread));
        Assert.Equal("Breathe here", reread.Marks.Notes[0].Text);
        Assert.Equal("Lena", reread.FindCategory(BookmarkCategory.TeacherId)!.Name);
        Assert.Equal((1, 2), (reread.Summary.ResumeCount, reread.Bookmarks.Count));
    }

    [Fact]
    public void The_fact_an_idea_came_from_is_kept()
    {
        ImportedPiece piece = Piece();
        Project project = Project.Create("p_test", "minuet.musicxml", s_source, s_created)
            .Keep(Observation.Create("o_0001", new SavedPassage(Bars(piece, 1, 2), SelectionFilter.All), null, "A thought", "Answer in the bass.", s_now) with { FactId = "rhythm.anacrusis" });

        Project reread = ProjectFile.Read(ProjectFile.Write(project));

        Assert.Equal("rhythm.anacrusis", reread.Observations[0].FactId);
    }

    /// <summary>A version 11 file as the app wrote it before this version: sessions, an observation with its session, a scan with a confirmed bar.</summary>
    private static string Version11(byte[] source, string scan = "null") => $$"""
        {
          "format": "motiff-lab-project",
          "version": 11,
          "id": "p_old",
          "fileName": "minuet.musicxml",
          "createdAt": "2026-10-23T21:00:00.0000000+00:00",
          "lastOpened": "2026-10-24T08:30:15.0000000+02:00",
          "source": "{{Convert.ToBase64String(source)}}",
          "motifs": [],
          "variations": [],
          "observations": [
            {
              "id": "o_0001",
              "name": "Bars 1–2",
              "sessionId": "s_0001",
              "note": "The bass answers the tune.",
              "createdAt": "2026-10-27T19:04:00.0000000+01:00",
              "phrase": { "onset": 0, "duration": 1920, "filter": {} }
            }
          ],
          "edits": [],
          "phrases": { "added": [], "removed": [] },
          "sessions": [
            {
              "id": "s_0001",
              "intent": "Understand",
              "task": "Why does bar 2 feel tense?",
              "passage": { "onset": 0, "duration": 1920, "filter": {} },
              "startedAt": "2026-10-27T19:00:00.0000000+01:00",
              "lastActive": "2026-10-27T19:03:00.0000000+01:00"
            },
            {
              "id": "s_0002",
              "intent": "Explore",
              "startedAt": "2026-10-27T20:00:00.0000000+01:00",
              "lastActive": "2026-10-27T20:00:00.0000000+01:00"
            }
          ],
          "scan": {{scan}},
          "transcription": null
        }
        """;

    [Fact]
    public void A_version_11_file_opens_with_its_sessions_as_resume_points_named_by_their_task()
    {
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(Version11(s_source)));

        Assert.Equal(["r_0001", "r_0002"], project.ResumePoints.Select(point => point.Id));
        ResumePoint first = project.ResumePoints[0];
        Assert.Equal("Why does bar 2 feel tense?", first.Name);
        Assert.Equal(new Tick(0), first.At);
        Assert.Equal(new TickSpan(Tick.Zero, 1920), first.Passage!.Span);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 19, 0, 0, TimeSpan.FromHours(1)), first.CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 19, 3, 0, TimeSpan.FromHours(1)), first.LastActive);
        Assert.Equal(100, first.TempoPercent);
        Assert.Null(project.ResumePoints[1].Name);
        Assert.Null(project.ResumePoints[1].Passage);
        Assert.Equal("r_0003", project.NextResumePointId());

        Assert.Single(project.Observations);
        Assert.Null(project.Observations[0].FactId);
        Assert.True(project.Marks.IsEmpty);
        Assert.Empty(project.Bookmarks);
        Assert.Equal(BookmarkCategory.BuiltIn, project.Categories);
        Assert.Empty(project.SettingOverrides);
        Assert.Null(project.Check);

        OpenedProject opened = OpenedProject.Open(project);
        Assert.NotNull(opened.Project.Check);
        Assert.Empty(opened.Project.Check);
        Assert.Equal(2, ProjectFile.Read(ProjectFile.Write(opened.Project)).ResumePoints.Count);
    }

    [Fact]
    public void A_scanned_version_11_file_gets_its_check_flags_when_opened_resolved_where_the_bar_was_confirmed()
    {
        byte[] scan = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Scan", "Fixtures", "homr-fur-elise-page1.musicxml"));
        ScanCheck check = ScanCheck.Of(PieceImporter.Import("scan.musicxml", scan).Piece!);
        ScanBar flagged = check.Flagged.First();
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(Version11(scan, $$"""{ "engine": "homr 0.7.0", "confirmedBars": [{{flagged.Bar}}] }""")));
        Assert.Null(project.Check);

        Project opened = OpenedProject.Open(project).Project;

        Assert.NotNull(opened.Check);
        Assert.Equal(check.Flagged.Sum(bar => bar.Flags.Count), opened.Check.Count);
        Assert.All(opened.Check.Where(flag => flag.Bar == flagged.Bar), flag => Assert.True(flag.Resolved));
        Assert.All(opened.Check.Where(flag => flag.Bar != flagged.Bar), flag => Assert.False(flag.Resolved));
        Assert.All(opened.Check, flag => Assert.Equal(CheckSource.Scan, flag.Source));
        Assert.DoesNotContain(opened.OpenChecks, flag => flag.Bar == flagged.Bar);

        // Resolving another bar confirms it for the scan too, and the flags round trip.
        int other = opened.OpenChecks[0].Bar;
        Project resolved = opened.ResolveCheck(other);
        Assert.True(resolved.Scan!.IsConfirmed(other));
        Assert.DoesNotContain(resolved.OpenChecks, flag => flag.Bar == other);
        Assert.Equal(resolved.Check, ProjectFile.Read(ProjectFile.Write(resolved)).Check);
        Assert.False(resolved.ResolveCheck(other, resolved: false).Scan!.IsConfirmed(other));
    }

    [Theory]
    [InlineData("\"finger\": 3", "\"finger\": 6", "fingering")]
    [InlineData("\"colour\": \"amber\"", "\"colour\": \"pink\"", "colour")]
    [InlineData("\"category\": \"c_0001\"", "\"category\": \"c_0009\"", "bookmarks")]
    [InlineData("\"tempoPercent\": 60", "\"tempoPercent\": 10", "resume point")]
    [InlineData("\"reason\": \"TempoUncertain\"", "\"reason\": \"Hunch\"", "reason")]
    [InlineData("\"phrases.granularity\": \"short\"", "\"phrases.granularity\": \"tiny\"", "settings")]
    [InlineData("\"layers.bookmarks\": \"off\"", "\"guidance.hints\": \"off\"", "settings")]
    public void Damaged_version_12_fields_are_refused_with_a_plain_reason(string from, string to, string what)
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Worked()));
        Assert.Contains(from, json, StringComparison.Ordinal);

        ProjectFileException exception = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json.Replace(from, to, StringComparison.Ordinal))));

        Assert.Contains("damaged", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(what, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_ink_document_is_the_pencil_layers_shape_and_the_files()
    {
        Project project = Worked();

        string ink = ProjectFile.WriteInk(project.Marks.Ink);

        Assert.Contains("\"version\":2", ink, StringComparison.Ordinal);
        Assert.Contains("\"tool\":\"highlighter\"", ink, StringComparison.Ordinal);
        Assert.Contains("\"colour\":\"graphite\"", ink, StringComparison.Ordinal);
        Assert.Equal(project.Marks.Ink, ProjectFile.ReadInk(ink));
        Assert.Empty(ProjectFile.ReadInk(null));
        Assert.Empty(ProjectFile.ReadInk("{\"version\":2,\"marks\":[]}"));
        Assert.Throws<ProjectFileException>(() => ProjectFile.ReadInk("[1, 2]"));
        Assert.Throws<ProjectFileException>(() => ProjectFile.ReadInk("{\"marks\":[{\"id\":\"x\"}]}"));
    }

    [Fact]
    public void A_newer_file_is_still_refused()
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Worked())).Replace("\"version\": 12", "\"version\": 13", StringComparison.Ordinal);

        ProjectFileException exception = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("newer version", exception.Message, StringComparison.Ordinal);
    }
}