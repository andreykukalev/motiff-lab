using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Assembly;

/// <summary>The assembly of the player's own version from a piece's sources (decision record 0024).</summary>
public class AssemblyTests
{
    private const long Eighth = 240;
    private static readonly TimeSignature s_threeEight = new(3, 8);
    private const long Bar = 3 * Eighth;
    private static readonly DateTimeOffset s_at = new(2026, 11, 7, 14, 2, 0, TimeSpan.Zero);

    private static Note N(string id, int pitch, long onset, long duration = Eighth, Hand hand = Hand.Right, int track = 0) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = track,
        Hand = new Attributed<Hand>(hand, Attribution.Imported),
        Attribution = Attribution.Imported,
    };

    /// <summary>Eight bars of 3/8 in A minor: three eighths a bar, the bass on each downbeat.</summary>
    private static SourceMusic Score(string prefix = "n", int bars = 8, Func<int, int[]>? melody = null)
    {
        List<Note> notes = [];
        for (int bar = 0; bar < bars; bar++)
        {
            int[] pitches = melody?.Invoke(bar) ?? [76, 75, 76];
            for (int i = 0; i < pitches.Length; i++)
            {
                notes.Add(N($"{prefix}{bar}r{i}", pitches[i], (bar * Bar) + (i * Eighth)));
            }

            notes.Add(N($"{prefix}{bar}l", 45, bar * Bar, Bar, Hand.Left, 1));
        }

        MeterMap meter = new([new MeterChange(Tick.Zero, s_threeEight)]);
        return SourceMusic.Of(notes, meter, new Tick(bars * Bar), [new PlaybackTempo(Tick.Zero, 500_000)], []);
    }

    private static AssemblyWork Work(SourceMusic cover) =>
        new(Score(), new Dictionary<string, SourceMusic> { ["cover"] = cover }, new Dictionary<string, string> { ["cover"] = "Кавер" }, [], 1);

    private static Note Find(SourceMusic music, string id) => music.Notes.Single(note => note.Id == id);

    [Fact]
    public void A_carry_replaces_the_selected_notes_and_keeps_where_each_note_came_from()
    {
        SourceMusic cover = Score("c", melody: bar => bar == 2 ? [81, 79, 77] : [76, 75, 76]);
        AssemblyWork work = Work(cover);
        SourceMusic notes = work.Result.Music;

        // Bar 3 of the cover over bar 3 of the notes: same length, nothing moves.
        AssemblyPlace place = AssemblyPlace.Between(Find(notes, "n2l"), Find(notes, "n2r2"));
        work = work.With(work.Carry("cover", new TickSpan(new Tick(2 * Bar), Bar), place, 0, s_at));

        Assembled result = work.Result;
        Assert.Equal([81, 79, 77], result.Music.NotesIn(new Tick(2 * Bar), new Tick(3 * Bar)).Where(note => note.Hand?.Value == Hand.Right).Select(note => note.Pitch.MidiNumber));
        Assert.Equal(1, result.OriginOf("c1.c2r0"));
        Assert.Equal(0, result.OriginOf("n3r0"));
        Assert.Equal(new Tick(3 * Bar), Find(result.Music, "n3r0").Span.Onset);
        Assert.Equal(8, result.Music.Bars.Count);
        Assert.True(work.Changed);
        Assert.Equal(1, Find(result.Music, "c1.c2l").Track);
    }

    [Fact]
    public void A_longer_fragment_moves_the_bars_after_it_and_keeps_their_downbeats()
    {
        AssemblyWork work = Work(Score("c"));
        SourceMusic notes = work.Result.Music;

        // Bar 3 of the cover and two eighths of its bar 4 replace bar 3.
        TickSpan from = TickSpan.FromBounds(new Tick(2 * Bar), new Tick((3 * Bar) + (2 * Eighth)));
        work = work.With(work.Carry("cover", from, AssemblyPlace.Between(Find(notes, "n2l"), Find(notes, "n2r2")), 0, s_at));

        Assembled result = work.Result;
        Assert.Equal(2 * Eighth, result.Steps[0].Delta);
        Assert.Equal(new Tick((3 * Bar) + (2 * Eighth)), Find(result.Music, "n3r0").Span.Onset);

        // Bar 4 is the cover's two eighths, a short bar of 2/8; bar 5 starts on the notes' old bar 4.
        AssemblyBar short4 = result.Music.Bars[3];
        Assert.Equal(2 * Eighth, short4.Span.Duration);
        MeterMap meter = result.Music.Meter;
        Assert.Equal(new TimeSignature(2, 8), meter.GetBar(4).Signature);
        Assert.Equal(Find(result.Music, "n3r0").Span.Onset, meter.GetBar(5).Span.Onset);
        Assert.Equal(s_threeEight, meter.GetBar(5).Signature);
        Assert.Equal(9, result.Music.Bars.Count);
    }

    [Fact]
    public void Short_bars_meeting_at_a_seam_become_one_bar()
    {
        IReadOnlyList<AssemblyBar> bars = Score().Bars;

        // The second half of bar 2 to the first half of bar 3, replaced by as much from the same place: still whole bars.
        Tick start = new(Bar + Eighth);
        Tick end = new((2 * Bar) + Eighth);
        IReadOnlyList<AssemblyBar> fragment = AssemblyBars.Cut(bars, start, end);
        IReadOnlyList<AssemblyBar> spliced = AssemblyBars.Splice(bars, start, end, fragment);

        Assert.Equal(bars.Count, spliced.Count);
        Assert.All(spliced, bar => Assert.False(bar.IsShort));
        Assert.Equal(bars.Count, AssemblyBars.ToMeter(spliced).BarsBefore(new Tick(bars.Count * Bar)).Count());
    }

    [Fact]
    public void Inserting_puts_the_fragment_in_and_moves_everything_after_it()
    {
        AssemblyWork work = Work(Score("c"));
        SourceMusic notes = work.Result.Music;
        work = work.With(work.Carry("cover", new TickSpan(Tick.Zero, Bar), AssemblyPlace.InsertAt(notes.Notes, new Tick(4 * Bar)), 0, s_at));

        Assembled result = work.Result;
        Assert.Empty(result.Steps[0].Removed);
        Assert.Equal(9, result.Music.Bars.Count);
        Assert.Equal(new Tick(5 * Bar), Find(result.Music, "n4r0").Span.Onset);
        Assert.Equal(new Tick(4 * Bar), Find(result.Music, "c1.c0r0").Span.Onset);
    }

    [Fact]
    public void Returning_a_carry_takes_the_note_changes_that_lean_on_it_and_leaves_the_rest()
    {
        AssemblyWork work = Work(Score("c", melody: bar => [81, 79, 77]));
        SourceMusic notes = work.Result.Music;
        work = work.With(work.Carry("cover", new TickSpan(new Tick(2 * Bar), Bar), AssemblyPlace.Between(Find(notes, "n2l"), Find(notes, "n2r2")), 0, s_at));
        work = work.ChangeNote("c1.c2r1", 2, s_at);
        work = work.With(work.Carry("cover", new TickSpan(new Tick(5 * Bar), Bar), AssemblyPlace.Between(Find(notes, "n5l"), Find(notes, "n5r2")), 0, s_at));

        Assert.Equal(81, Find(work.Result.Music, "c1.c2r1").Pitch.MidiNumber);
        RevertPlan plan = work.PlanRevert(1);
        Assert.Equal([2], plan.Dependents);
        Assert.Equal([3], plan.Staying);

        AssemblyWork back = work.Revert(1);
        Assert.Equal([3], back.Steps.Select(step => step.Number));
        Assert.Equal(76, Find(back.Result.Music, "n2r0").Pitch.MidiNumber);
        Assert.Equal(3, back.Result.OriginOf("c3.c5r0"));

        // The return is one change: ⌘Z brings all three back, ⇧⌘Z returns it again.
        Assert.Equal([1, 2, 3], back.Undo().Steps.Select(step => step.Number));
        Assert.Equal([3], back.Undo().Redo().Steps.Select(step => step.Number));
    }

    [Fact]
    public void A_step_whose_note_is_gone_is_named_and_never_applied_silently()
    {
        AssemblyWork work = Work(Score("c"));
        work = work.ChangeNote("c9.missing", 1, s_at);

        StepOutcome outcome = work.Result.Steps.Single();
        Assert.False(outcome.Applied);
        Assert.Equal("c9.missing", outcome.Lost);
        Assert.False(work.Changed);
    }

    [Fact]
    public void The_sheet_says_what_the_carry_does_in_the_same_six_lines()
    {
        // The cover in G minor-ish (two flats), a tone below the notes' A minor, and half a bar longer.
        SourceMusic cover = Score("c", melody: bar => [74, 73, 74]);
        cover = cover with { Notes = [.. cover.Notes.Select(note => note with { Pitch = new Pitch(note.Pitch.MidiNumber - 2) })] };
        AssemblyWork work = Work(cover);
        SourceMusic notes = work.Result.Music;
        AssemblyPlace place = AssemblyPlace.Between(Find(notes, "n2l"), Find(notes, "n2r2"));
        CarryStep carry = work.Carry("cover", TickSpan.FromBounds(new Tick(2 * Bar), new Tick((3 * Bar) + Eighth)), place, 0, s_at);

        CarrySheet sheet = work.Sheet(carry, place, new Tick(2 * Bar), Words.Russian);

        Assert.Equal(["where", "length", "seam", "key", "meter", "notes"], sheet.Lines.Select(line => line.Name));
        Assert.StartsWith("6 нот из Кавера", sheet.Title, StringComparison.Ordinal);
        Assert.Equal(SheetMark.Changes, sheet.Lines[1].Mark);
        Assert.Contains("на ⅓ такта длиннее", sheet.Lines[1].Text, StringComparison.Ordinal);
        Assert.Equal(SheetMark.Same, sheet.Lines[4].Mark);
        Assert.Equal("заменить 4 ноты такта 3", sheet.ReplaceText);
        Assert.Contains("войдут", sheet.Lines[5].Text, StringComparison.Ordinal);
        Assert.Contains("4 уйдут; из них 4 из прежних нот", sheet.Lines[5].Text, StringComparison.Ordinal);
        Assert.NotEmpty(sheet.Outcome.Put);
        Assert.Empty(work.Steps);
    }

    [Fact]
    public void The_comparison_names_the_bars_in_words_and_accepting_takes_only_the_corrections()
    {
        // The same piece with one corrected bar (bar 6) and a different bar 3.
        SourceMusic cover = Score("c", melody: bar => bar switch
        {
            2 => [60, 62, 64],
            5 => [76, 75, 77],
            _ => [76, 75, 76],
        });
        AssemblyWork work = Work(cover);

        AssemblyMatch match = work.Match("cover");
        Assert.Equal(MatchKind.Same, match.Bars[0].Kind);
        Assert.True(match.Bars[0].Identical);
        Assert.NotEqual(MatchKind.Same, match.Bars[2].Kind);
        Assert.Equal(MatchKind.Same, match.Bars[5].Kind);
        Assert.False(match.Bars[5].Identical);
        Assert.Contains("Совпадает в тактах", match.Summary(cover, work.Result.Music, Words.Russian), StringComparison.Ordinal);

        AssemblyWork accepted = work.Accept(match, s_at)!;
        Assert.Equal(77, accepted.Result.Music.NotesIn(new Tick(5 * Bar), new Tick(6 * Bar)).Single(note => note.Span.Onset.Value == (5 * Bar) + (2 * Eighth) && note.Hand?.Value == Hand.Right).Pitch.MidiNumber);
        Assert.Equal(76, accepted.Result.Music.NotesIn(new Tick(2 * Bar), new Tick(3 * Bar)).First(note => note.Hand?.Value == Hand.Right).Pitch.MidiNumber);
        Assert.Single(accepted.Steps);

        // A comparison made before the journal changed takes nothing.
        Assert.Null(accepted.Accept(match, s_at));
    }

    [Fact]
    public void The_project_keeps_the_journal_and_the_applied_notes_and_opens_with_them()
    {
        byte[] midi = MidiExporter.Export([.. Score().Notes], [], new MeterMap([new MeterChange(Tick.Zero, s_threeEight)]), "piece");
        Project project = Project.Create("p_1", "piece.mid", midi, s_at);
        OpenedProject opened = OpenedProject.Open(project);
        SourceMusic piece = SourceMusic.OfPiece(opened.Piece, opened.Notes);
        AssemblyWork work = new(piece, new Dictionary<string, SourceMusic> { ["cover"] = Score("c", melody: bar => [81, 79, 77]) }, new Dictionary<string, string> { ["cover"] = "Кавер" }, [], 1);
        Note first = piece.Notes.Where(note => note.Span.Onset.Value == 2 * Bar).OrderBy(note => note.Pitch.MidiNumber).First();
        Note last = piece.Notes.Where(note => note.Span.Onset.Value == (2 * Bar) + (2 * Eighth)).First();
        work = work.With(work.Carry("cover", TickSpan.FromBounds(new Tick(2 * Bar), new Tick((3 * Bar) + Eighth)), AssemblyPlace.Between(first, last), 0, s_at));

        project = project.WithJournal(work.Steps, work.Next).ApplyAssembly(work.Result.Music, work.Base, s_at);
        Project read = ProjectFile.Read(ProjectFile.Write(project));

        Assert.Single(read.Assembly.Steps);
        Assert.Equal(2, read.Assembly.Next);
        Assert.Equal(project.Assembly.Applied!.Notes.Count, read.Assembly.Applied!.Notes.Count);
        Assert.Equal(piece.Notes.Count, read.Assembly.Before!.Notes.Count);
        OpenedProject again = OpenedProject.Open(read);
        Assert.Equal(work.Result.Music.Notes.Select(note => note.Id).Order(StringComparer.Ordinal), again.Notes.Select(note => note.Id).Order(StringComparer.Ordinal));
        Assert.Equal(new TimeSignature(1, 8), again.Piece.Meter.GetBar(4).Signature);
        Assert.Equal(work.Result.Music.End, PlaybackScore.FromPiece(again.Piece).End);
    }
}