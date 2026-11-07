using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Performance;

/// <summary>Merging a version through a draft that changes nothing until applied (testing items 32 and 34).</summary>
public class PerformanceMergeTests
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

    /// <summary>The version: a figure of eighths over the last two beats of bars 2 and 3, their third beat left out.</summary>
    private static List<Note> Played()
    {
        List<Note> played = [.. Score().Where(note => note.Id is not "rh06" and not "rh10")];
        foreach (int bar in new[] { 1, 2 })
        {
            int[] figure = [79, 81, 79, 77];
            played.AddRange(figure.Select((pitch, i) => Printed($"f{bar}{i}", pitch, (bar * Bar) + (2 * Quarter) + (i * Quarter / 2), Quarter / 2)));
        }

        return played;
    }

    private static (SavedPerformance Version, PerformanceComparison Comparison) Version()
    {
        byte[] midi = MidiExporter.Export([.. Played()], [], MeterMap.Default, "performance");
        IReadOnlyList<PlayedNote> played = PlayedNotes.FromMidi(midi);
        PerformanceAlignment alignment = PerformanceAlignment.Align(Score(), played);
        var version = new SavedPerformance("perf_1", "Live", PerformanceSource.Midi, midi, 0, alignment.Anchors);
        return (version, PerformanceComparison.Compare(Score(), MeterMap.Default, version.Notes(), version.Alignment()));
    }

    [Fact]
    public void The_draft_changes_nothing_until_applied_and_every_step_can_be_undone_and_redone()
    {
        (SavedPerformance version, PerformanceComparison comparison) = Version();
        MergeDraft draft = PerformanceMerge.Open(version, comparison, MergeStart.Mine);
        Assert.Equal([2, 3], draft.Bars);
        Assert.Equal(Score().Count, PerformanceMerge.Result(draft, version, comparison, MeterMap.Default, Score()).Count);

        MergeDraft taken = draft.Choose([2], MergeChoice.Version);
        IReadOnlyList<Note> result = PerformanceMerge.Result(taken, version, comparison, MeterMap.Default, Score());
        Assert.Equal(Score().Count - 1 + 4, result.Count);
        Assert.DoesNotContain(result, note => note.Id == "rh06");
        Assert.Contains(result, note => note.Id == "rh10");
        Assert.Empty(version.Taken);

        MergeDraft phrase = taken.Choose([2, 3], MergeChoice.Version);
        Assert.Equal(2, PerformanceMerge.Taken(phrase, version, comparison, MeterMap.Default, Score()).Count);
        Assert.Equal(taken.Decisions, phrase.Undo().Decisions);
        Assert.Equal(phrase.Decisions, phrase.Undo().Redo().Decisions);
        Assert.False(phrase.Undo().Choose([3], MergeChoice.NotADifference).CanRedo);

        MergeDraft reset = phrase.Reset();
        Assert.Equal(draft.Decisions, reset.Decisions);
        Assert.Equal(phrase.Decisions, reset.Undo().Decisions);
        Assert.Same(reset, reset.Reset());
    }

    [Fact]
    public void Applying_takes_the_bars_keeps_the_choices_to_open_again_and_the_merge_can_be_undone()
    {
        (SavedPerformance version, PerformanceComparison comparison) = Version();
        MergeDraft draft = PerformanceMerge.Open(version, comparison, MergeStart.All)
            .Choose([3], MergeChoice.NotADifference);
        var at = new DateTimeOffset(2026, 11, 7, 12, 30, 0, TimeSpan.Zero);

        SavedPerformance merged = PerformanceMerge.Apply(draft, version, comparison, MeterMap.Default, Score(), at);
        Assert.Equal([2], merged.Taken.Select(bar => bar.Bar));
        Assert.Equal([3], merged.Dismissed);
        Assert.Equal(at, merged.Merge!.AppliedAt);

        MergeDraft again = PerformanceMerge.Open(merged, comparison, MergeStart.Saved);
        Assert.Equal(draft.Decisions, again.Decisions);
        Assert.False(again.Changed);

        SavedPerformance undone = PerformanceMerge.Undo(merged);
        Assert.Empty(undone.Taken);
        Assert.Null(undone.Merge!.AppliedAt);
        Assert.Equal(draft.Decisions, PerformanceMerge.Open(undone, comparison, MergeStart.Saved).Decisions);

        // Kept with the piece: the choices and when they were applied come back from the project file.
        Project project = Project.Create("p", "piece.mid", [1], at).AddPerformance(merged);
        SavedPerformance read = ProjectFile.Read(ProjectFile.Write(project)).FindPerformance("perf_1")!;
        Assert.Equal(merged, read);
    }

    [Fact]
    public void A_bar_the_player_writes_in_the_roll_keeps_the_notes_left_as_they_were_and_brings_the_rest()
    {
        (SavedPerformance version, PerformanceComparison comparison) = Version();
        TickSpan bar2 = MeterMap.Default.GetBar(2).Span;
        List<Note> mine = [.. Score().Where(note => bar2.Contains(note.Span.Onset))];
        // The player keeps the bar but moves its third beat up a step and adds a note an eighth before the end, past the bar line.
        List<Note> written = [.. mine.Select(note => note.Id == "rh06" ? note with { Pitch = new Pitch(74) } : note),
            Printed("new", 79, bar2.End.Value - (Quarter / 2), Quarter)];

        TakenBar own = PerformanceMerge.OwnBar("perf_1", 2, MeterMap.Default, Score(), written);
        Assert.Equal(["rh06"], own.Removed);
        Assert.Equal(["perf-perf_1-own-2-1", "perf-perf_1-own-2-2"], own.Added.Select(note => note.Id));
        Assert.Equal(bar2.End, own.Added[1].Span.End);

        MergeDraft draft = PerformanceMerge.Open(version, comparison, MergeStart.Mine).WriteOwn(own);
        Assert.Equal(MergeChoice.Own, draft.DecisionFor(2)!.Choice);
        IReadOnlyList<Note> result = PerformanceMerge.Result(draft, version, comparison, MeterMap.Default, Score());
        Assert.Contains(result, note => note.Id == "perf-perf_1-own-2-1" && note.Pitch.MidiNumber == 74);
    }
}