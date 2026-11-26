using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Facade.Sessions;

/// <summary>
/// The performances session typed (audit W1.7): a version added and compared with the printed notes, bars taken and
/// restored, a bar dismissed and a version renamed, with no request and no JSON. What the iPad reads of the same calls is
/// pinned by tests/contracts/facade-answers (FacadeAnswersContractTests).
/// </summary>
public class PerformancesSessionTests
{
    private static SessionPiece Example() => OpenPiece.Import(
        "p_session",
        "FurElise.musicxml",
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml")),
        Words.English,
        new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero)).State;

    /// <summary>The piece played with three notes added in bar 5, as MIDI.</summary>
    private static byte[] PlayedWithAddedNotes(SessionPiece piece)
    {
        TickSpan bar5 = piece.Piece.Meter.GetBar(5).Span;
        Note[] melody = [.. piece.Notes.Where(note => bar5.Contains(note.Span.Onset)).OrderByDescending(note => note.Pitch.MidiNumber).Take(3)];
        Note[] played = [.. piece.Notes, .. melody.Select((note, i) => note with { Id = $"x{i}", Pitch = new Pitch(note.Pitch.MidiNumber + (i * 7) - 3) })];
        return MidiExporter.Export(played, [], piece.Piece.Meter, "performance");
    }

    [Fact]
    public void A_version_added_answers_the_project_it_makes_and_is_compared_with_the_printed_notes()
    {
        SessionPiece piece = Example();
        Assert.Equal("badRequest", Assert.Throws<FacadeException>(() => PerformancesSession.Add(piece, piece.Notes, "Bad", [1, 2, 3], PerformanceSource.Midi, 0, null)).Code);
        Assert.Equal("notFound", Assert.Throws<FacadeException>(() => PerformancesSession.Find(piece, "perf_1")).Code);
        Assert.Equal("badRequest", Assert.Throws<FacadeException>(() => PerformancesSession.Version("notes.txt", [1, 2, 3])).Code);

        AddedVersion added = PerformancesSession.Add(piece, piece.Notes, "Live", PlayedWithAddedNotes(piece), PerformanceSource.Midi, 0, "engine");
        Assert.Empty(piece.Project.Performances);
        SessionPiece with = piece with { Project = added.Project };
        Assert.Same(added.Performance, PerformancesSession.Find(with, "perf_1"));
        Assert.Equal("engine", added.Performance.Engine);

        PerformanceComparison compared = PerformancesSession.Compare(with, with.Notes, added.Performance);
        Assert.Equal(piece.Notes.Count, compared.MatchedCount);
        Assert.Equal(5, Assert.Single(compared.Bars, bar => bar.Difference != BarDifference.None).Bar);
        Assert.Equal(3, PerformancesSession.Layer(with, with.Notes, added.Performance, artefacts: false).Added.Count);
    }

    [Fact]
    public void Bars_taken_from_a_version_replace_the_printed_ones_until_restored()
    {
        SessionPiece piece = Example();
        AddedVersion added = PerformancesSession.Add(piece, piece.Notes, "Live", PlayedWithAddedNotes(piece), PerformanceSource.Midi, 0, null);
        SessionPiece with = piece with { Project = added.Project };
        Assert.Equal("badRequest", Assert.Throws<FacadeException>(() => PerformancesSession.Take(with, with.Notes, added.Performance, [999], [])).Code);

        IReadOnlyList<TakenBar> taken = PerformancesSession.Take(with, with.Notes, added.Performance, [5], []);
        Project kept = PerformancesSession.WithBarsTaken(with.Project, "perf_1", taken);
        Assert.Equal(piece.Notes.Count + 3, PerformancesSession.NotesWithTakes(piece.Notes, kept).Count);
        Assert.Equal([5], PerformancesSession.TakeAll(with, with.Notes, added.Performance, confidence: null).Select(bar => bar.Bar));

        Project restored = PerformancesSession.Restored(kept, "perf_1", [5]);
        Assert.Equal(piece.Notes, PerformancesSession.NotesWithTakes(piece.Notes, restored));
    }

    [Fact]
    public void A_bar_is_dismissed_and_a_version_renamed_but_never_to_an_empty_name()
    {
        SessionPiece piece = Example();
        Project project = PerformancesSession.Add(piece, piece.Notes, "Live", PlayedWithAddedNotes(piece), PerformanceSource.Midi, 0, null).Project;

        Project dismissed = PerformancesSession.Dismissed(project, "perf_1", 5, dismissed: true);
        Assert.Equal([5], dismissed.FindPerformance("perf_1")!.Dismissed);
        Assert.Empty(PerformancesSession.Dismissed(dismissed, "perf_1", 5, dismissed: false).FindPerformance("perf_1")!.Dismissed);

        Assert.Equal("Concert", PerformancesSession.Renamed(project, "perf_1", " Concert ").FindPerformance("perf_1")!.Name);
        Assert.Equal("badRequest", Assert.Throws<FacadeException>(() => PerformancesSession.Renamed(project, "perf_1", "  ")).Code);
    }
}