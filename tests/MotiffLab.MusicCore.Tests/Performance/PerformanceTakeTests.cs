using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Performance;

/// <summary>Taking a performer's bar into the score and keeping it with the piece (decision record 0022, sections 5 and 8).</summary>
public class PerformanceTakeTests
{
    private const long Quarter = 480;
    private const long Bar = 4 * Quarter;

    private static Note Printed(string id, int pitch, long onset, long duration = Quarter, Hand hand = Hand.Right) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Hand = new Attributed<Hand>(hand, Attribution.Imported),
        Attribution = Attribution.Imported,
    };

    /// <summary>Four bars: a quarter-note melody over a bass note on each beat 1.</summary>
    private static List<Note> Score()
    {
        int[] melody = [72, 74, 76, 74, 72, 71, 72, 74, 76, 77, 76, 74, 72, 71, 69, 71];
        List<Note> notes = [.. melody.Select((pitch, i) => Printed($"rh{i:D2}", pitch, i * Quarter))];
        notes.AddRange(Enumerable.Range(0, 4).Select(bar => Printed($"lh{bar}", 48, bar * Bar, Bar, Hand.Left)));
        return notes;
    }

    /// <summary>Played at a quarter a second (60 a minute), with bar 2's third beat left out and a figure of eighths over its last two beats.</summary>
    private static List<PlayedNote> Played()
    {
        List<PlayedNote> played = [.. Score().Where(note => note.Id != "rh06").Select(note => new PlayedNote("p_" + note.Id, note.Pitch, 2 * note.Span.Onset.Value * 1_000_000 / (2 * Quarter), 70, (note.Span.Duration * 1_000_000 / Quarter) - 50_000))];
        long bar2 = Bar * 1_000_000 / Quarter;
        played.Add(new PlayedNote("f1", new Pitch(79), bar2 + 2_010_000, 60, 480_000));
        played.Add(new PlayedNote("f2", new Pitch(81), bar2 + 2_505_000, 60, 480_000));
        played.Add(new PlayedNote("f3", new Pitch(79), bar2 + 3_000_000, 60, 480_000));
        played.Add(new PlayedNote("f4", new Pitch(77), bar2 + 3_490_000, 60, 480_000));
        return played;
    }

    private static (PerformanceComparison Comparison, List<PlayedNote> Played) Compare()
    {
        List<PlayedNote> played = Played();
        return (PerformanceComparison.Compare(Score(), MeterMap.Default, played, PerformanceAlignment.Align(Score(), played)), played);
    }

    [Fact]
    public void Taking_a_bar_removes_what_was_not_played_and_adds_what_was_on_the_16th_grid()
    {
        (PerformanceComparison comparison, List<PlayedNote> played) = Compare();
        BarComparison bar2 = comparison.Bars[1];
        Assert.Equal(BarDifference.AddedNotes, bar2.Difference);

        TakenBar taken = PerformanceTake.Take("perf_1", bar2, played, comparison.Alignment, MeterMap.Default, Score());

        Assert.Equal(["rh06"], taken.Removed);
        Assert.Equal(["perf-perf_1-f1", "perf-perf_1-f2", "perf-perf_1-f3", "perf-perf_1-f4"], taken.Added.Select(note => note.Id));
        Assert.Equal([Bar + (2 * Quarter), Bar + (2 * Quarter) + 240, Bar + (3 * Quarter), Bar + (3 * Quarter) + 240], taken.Added.Select(note => note.Span.Onset.Value));
        Assert.All(taken.Added, note => Assert.Equal(0, note.Span.Duration % PerformanceTake.Grid));
        Assert.All(taken.Added, note => Assert.Equal(Hand.Right, note.Hand));
    }

    [Fact]
    public void Notes_the_player_unticked_stay_as_printed()
    {
        (PerformanceComparison comparison, List<PlayedNote> played) = Compare();

        TakenBar taken = PerformanceTake.Take("perf_1", comparison.Bars[1], played, comparison.Alignment, MeterMap.Default, Score(), ["rh06", "f4"]);

        Assert.Empty(taken.Removed);
        Assert.Equal(3, taken.Added.Count);
    }

    [Fact]
    public void The_score_with_a_bar_taken_marks_the_new_notes_as_the_players_choice()
    {
        (PerformanceComparison comparison, List<PlayedNote> played) = Compare();
        TakenBar taken = PerformanceTake.Take("perf_1", comparison.Bars[1], played, comparison.Alignment, MeterMap.Default, Score());

        IReadOnlyList<Note> notes = PerformanceTake.Apply(Score(), [taken]);

        Assert.Equal(Score().Count - 1 + 4, notes.Count);
        Assert.DoesNotContain(notes, note => note.Id == "rh06");
        Assert.All(notes.Where(note => PerformanceTake.IsTaken(note.Id)), note => Assert.Equal(Provenance.UserEdited, note.Attribution.Provenance));
        Assert.Same(notes, PerformanceTake.Apply(notes, []));
    }

    [Fact]
    public void A_performance_and_its_taken_bars_are_kept_in_the_project_file()
    {
        List<Note> score = Score();
        (PerformanceComparison comparison, List<PlayedNote> played) = Compare();
        byte[] midi = MidiExporter.Export([.. score], [], MeterMap.Default, "performance");
        byte[] source = MidiExporter.Export([.. score], [], MeterMap.Default, "score");
        TakenBar taken = PerformanceTake.Take("perf_1", comparison.Bars[1], played, comparison.Alignment, MeterMap.Default, score);
        Project project = Project.Create("p_1", "score.mid", source, DateTimeOffset.UnixEpoch)
            .AddPerformance(new SavedPerformance("perf_1", "Live", PerformanceSource.Recording, midi, 2_000_000, comparison.Alignment.Anchors) { Engine = "transkun 2.0.1" })
            .ChangePerformance("perf_1", performance => performance.Dismiss(3).TakeBars([taken]));

        byte[] written = ProjectFile.Write(project);
        Project reread = ProjectFile.Read(written);

        SavedPerformance kept = Assert.Single(reread.Performances);
        Assert.Equal(project.Performances[0], kept);
        Assert.Equal([3], kept.Dismissed);
        Assert.Equal(written, ProjectFile.Write(reread));
        Assert.True(reread.ChangesNotes);
        Assert.Equal("perf_2", reread.NextPerformanceId());
        Assert.Equal(score.Count, kept.Notes().Count);
        Assert.True(kept.Notes().Min(note => note.Onset) >= 2_000_000);

        OpenedProject opened = OpenedProject.Open(reread);
        Assert.Contains(opened.Notes, note => note.Id == "perf-perf_1-f1");
        // The imported file has its own ids, so the removed "rh06" is not there and is ignored.
        Assert.Equal(score.Count + taken.Added.Count, opened.Notes.Count);
    }

    [Fact]
    public void Removing_a_performance_drops_its_bars_and_its_check_flags()
    {
        byte[] midi = MidiExporter.Export([.. Score()], [], MeterMap.Default, "performance");
        Project project = Project.Create("p_1", "score.mid", midi, DateTimeOffset.UnixEpoch)
            .AddPerformance(new SavedPerformance("perf_1", "Live", PerformanceSource.Midi, midi, 0, []))
            .WithPerformanceChecks([new CheckFlag(2, CheckSource.Performance, CheckReason.RecordingOctave, false)]);

        Assert.Single(project.OpenChecks);
        Project removed = project.RemovePerformance("perf_1");

        Assert.Empty(removed.Performances);
        Assert.Empty(removed.OpenChecks);
        Assert.Throws<KeyNotFoundException>(() => removed.RemovePerformance("perf_1"));
    }
}