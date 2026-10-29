using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>Work sessions on a piece (0012): the task, the passage and the ideas kept during it, saved with the project.</summary>
public class SessionTests
{
    private static readonly DateTimeOffset s_created = new(2026, 10, 23, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_started = new(2026, 10, 27, 19, 0, 0, TimeSpan.FromHours(1));

    private static readonly byte[] s_source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "importer-grand-staff.musicxml"));

    private static ImportedPiece Piece() => PieceImporter.Import("minuet.musicxml", s_source).Piece!;

    private static MotifSelection Bars(ImportedPiece piece, int first, int last) =>
        MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, first, last, piece.End), SelectionFilter.All);

    /// <summary>The minuet with two sessions, the first on bars 1–2 with a note kept during it, the second just started.</summary>
    private static Project Worked()
    {
        ImportedPiece piece = Piece();
        Project project = Project.Create("p_test", "minuet.musicxml", s_source, s_created);
        Session first = Session.Start(project.NextSessionId(), SessionIntent.Understand, "  Why does bar 2 feel tense? ", s_started);
        project = project.StartSession(first);
        project = project.ChangeSession(first.Id, session => session.Touch(SavedPassage.Of(Bars(piece, 1, 2)), s_started.AddMinutes(3)));
        project = project.Keep(Observation.Create(project.NextObservationId(), SavedPassage.Of(Bars(piece, 1, 2)), null, "Bars 1–2", "The bass answers the tune.", s_started.AddMinutes(4)) with { SessionId = first.Id });
        return project.StartSession(Session.Start(project.NextSessionId(), SessionIntent.Explore, null, s_started.AddHours(1)));
    }

    [Fact]
    public void A_session_keeps_its_task_trimmed_and_the_passage_it_was_on()
    {
        Project project = Worked();

        Assert.Equal(["s_0001", "s_0002"], project.Sessions.Select(session => session.Id));
        Session first = project.Sessions[0];
        Assert.Equal(SessionIntent.Understand, first.Intent);
        Assert.Equal("Why does bar 2 feel tense?", first.Task);
        Assert.NotNull(first.Passage);
        Assert.Equal(s_started, first.StartedAt);
        Assert.Equal(s_started.AddMinutes(3), first.LastActive);
        Assert.Null(project.Sessions[1].Task);
        Assert.Null(project.Sessions[1].Passage);
        Assert.Equal(["s_0002", "s_0001"], project.RecentSessions.Select(session => session.Id));
        Assert.Equal("s_0003", project.NextSessionId());
        Assert.Equal("o_0002", project.NextObservationId());
        Assert.Equal((2, 1), (project.Summary.SessionCount, project.Summary.IdeaCount));
    }

    [Fact]
    public void Sessions_and_the_session_of_each_idea_round_trip_through_the_project_file()
    {
        Project project = Worked();

        Project reread = ProjectFile.Read(ProjectFile.Write(project));

        Assert.Equal(project.Sessions, reread.Sessions);
        Assert.Equal("s_0001", reread.Observations[0].SessionId);
        Assert.Equal(ProjectFile.Write(project), ProjectFile.Write(reread));
    }

    [Fact]
    public void Ending_a_session_keeps_its_ideas_as_the_projects_own()
    {
        Project project = Worked().EndSession("s_0001");

        Assert.Equal(["s_0002"], project.Sessions.Select(session => session.Id));
        Assert.Single(project.Observations);
        Assert.Null(project.Observations[0].SessionId);
        Assert.Throws<KeyNotFoundException>(() => project.EndSession("s_0001"));
        Assert.Throws<ArgumentException>(() => project.StartSession(project.Sessions[0]));
    }

    [Fact]
    public void A_session_can_be_renamed_and_a_blank_task_is_none()
    {
        Project project = Worked().ChangeSession("s_0002", session => session.Rename(" Find another ending "));
        Assert.Equal("Find another ending", project.FindSession("s_0002")!.Task);
        Assert.Null(project.ChangeSession("s_0002", session => session.Rename("   ")).FindSession("s_0002")!.Task);
        Assert.Throws<KeyNotFoundException>(() => project.ChangeSession("s_0009", session => session));
    }

    [Fact]
    public void A_version_7_file_opens_with_no_sessions()
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Worked() with { Sessions = [], Observations = [] }))
            .Replace("\"version\": 9", "\"version\": 7", StringComparison.Ordinal)
            .Replace(",\n  \"sessions\": []", "", StringComparison.Ordinal);
        Assert.DoesNotContain("sessions", json);

        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(json));

        Assert.Empty(project.Sessions);
    }

    [Theory]
    [InlineData("\"sessionId\": \"s_0001\"", "\"sessionId\": \"s_0007\"")]
    [InlineData("\"intent\": \"Explore\"", "\"intent\": \"Wander\"")]
    [InlineData("\"id\": \"s_0002\"", "\"id\": \"s_0001\"")]
    public void A_damaged_session_is_refused_with_a_plain_reason(string from, string to)
    {
        string json = Encoding.UTF8.GetString(ProjectFile.Write(Worked())).Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(Encoding.UTF8.GetString(ProjectFile.Write(Worked())), json);

        ProjectFileException exception = Assert.Throws<ProjectFileException>(() => ProjectFile.Read(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("damaged", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}