using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Tests.Golden;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Performance;

/// <summary>A printed score aligned with a performance and compared bar by bar (decision record 0022, sections 2 and 3).</summary>
public class PerformanceComparisonTests
{
    private const long Quarter = 480;
    private const long BarTicks = 4 * Quarter;
    private const int Bars = 8;

    private static Note Printed(string id, int pitch, long onset, long duration = Quarter) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    /// <summary>
    /// Eight bars of 4/4: a quarter-note melody in the right hand, and in the left hand a bass note and a
    /// fifth above it on beats 1 and 3. Bar 6 has a low G1 under its first bass note.
    /// </summary>
    private static List<Note> Score()
    {
        int[] melody = [72, 74, 76, 74, 71, 72, 74, 76, 77, 76, 74, 72, 71, 72, 74, 79, 76, 74, 72, 71, 72, 76, 79, 77, 76, 74, 72, 74, 72, 71, 72, 67];
        int[] bass = [48, 50, 52, 50, 48, 43, 45, 48];
        List<Note> notes = [];
        for (int i = 0; i < melody.Length; i++)
        {
            notes.Add(Printed($"rh{i:D2}", melody[i], i * Quarter));
        }

        for (int bar = 0; bar < Bars; bar++)
        {
            foreach (int beat in new[] { 0, 2 })
            {
                long at = (bar * BarTicks) + (beat * Quarter);
                notes.Add(Printed($"lh{bar}{beat}a", bass[bar], at, 2 * Quarter));
                notes.Add(Printed($"lh{bar}{beat}b", bass[bar] + 7, at, 2 * Quarter));
            }
        }

        notes.Add(Printed("low", 31, 5 * BarTicks, 2 * Quarter));
        return notes;
    }

    /// <summary>The time a score position is played at: each bar a little slower than the last, from a quarter at 120 a minute.</summary>
    private static long Time(long tick)
    {
        long micros = 1_000_000;
        long bar = tick / BarTicks;
        for (long b = 0; b < bar; b++)
        {
            micros += 4 * (500_000 + (20_000 * b));
        }

        return micros + ((tick % BarTicks) * (500_000 + (20_000 * bar)) / Quarter);
    }

    /// <summary>The score played as printed, with a few milliseconds of unevenness per note.</summary>
    private static List<PlayedNote> AsPrinted(IEnumerable<Note> score) =>
        [.. score.Select((note, i) => new PlayedNote("p_" + note.Id, note.Pitch, Time(note.Span.Onset.Value) + (((i * 7) % 5) - 2) * 4_000, 70, Time(note.Span.End.Value) - Time(note.Span.Onset.Value) - 20_000))];

    /// <summary>The performance used by most tests: the score with one change per bar from bar 2 on.</summary>
    private static List<PlayedNote> Performance()
    {
        List<Note> score = Score();

        // Bar 5: the left hand's second fifth and the last melody note are left out. Bar 6: the low G1 is not heard.
        score.RemoveAll(note => note.Id is "lh42a" or "lh42b" or "rh19" or "low");

        // Bar 4: G2 instead of D3 under the first beat. Bar 7: the melody an octave lower.
        score = [.. score.Select(note => note.Id switch
        {
            "lh30a" => note with { Pitch = new Pitch(43) },
            _ when note.Id.StartsWith("rh", StringComparison.Ordinal) && note.Span.Onset.Value / BarTicks == 6 => note with { Pitch = new Pitch(note.Pitch.MidiNumber - 12) },
            _ => note,
        })];
        List<PlayedNote> played = AsPrinted(score);

        // Bar 2: a quiet overtone an octave above the melody's first note.
        played.Add(new PlayedNote("overtone", new Pitch(83), Time(BarTicks) + 10_000, 30, 100_000));

        // Bar 3: a filling figure of eighths over beats 2 and 3.
        long bar3 = 2 * BarTicks;
        played.Add(new PlayedNote("fill1", new Pitch(81), Time(bar3 + 600), 60, 100_000));
        played.Add(new PlayedNote("fill2", new Pitch(84), Time(bar3 + 840), 60, 100_000));
        played.Add(new PlayedNote("fill3", new Pitch(81), Time(bar3 + 1080), 60, 100_000));

        // Bar 8: a quick run up to the third beat's melody note, as an ornament is played.
        long bar8 = 7 * BarTicks;
        played.Add(new PlayedNote("orn1", new Pitch(67), Time(bar8 + 900), 60, 100_000));
        played.Add(new PlayedNote("orn2", new Pitch(69), Time(bar8 + 960), 60, 100_000));
        played.Add(new PlayedNote("orn3", new Pitch(71), Time(bar8 + 1020), 60, 100_000));
        return played;
    }

    private static PerformanceComparison Compare(List<Note> score, List<PlayedNote> played) =>
        PerformanceComparison.Compare(score, MeterMap.Default, played, PerformanceAlignment.Align(score, played));

    [Fact]
    public void The_time_map_follows_a_slowing_tempo()
    {
        List<Note> score = Score();
        PerformanceAlignment alignment = PerformanceAlignment.Align(score, AsPrinted(score));

        Assert.True(alignment.Anchors.Count >= 4 * Bars);
        for (int bar = 0; bar <= Bars; bar++)
        {
            long tick = bar * BarTicks;
            Assert.InRange(alignment.MicrosecondsAt(new Tick(tick)), Time(tick) - 15_000, Time(tick) + 15_000);
            Assert.InRange(alignment.TickAt(Time(tick)).Value, tick - 20, tick + 20);
        }
    }

    [Fact]
    public void A_score_played_as_printed_has_no_differences()
    {
        List<Note> score = Score();
        PerformanceComparison comparison = Compare(score, AsPrinted(score));

        Assert.All(comparison.Bars, bar => Assert.True(bar.Aligned));
        Assert.Empty(comparison.Differences);
        Assert.Equal(score.Count, comparison.MatchedCount);
        Assert.All(comparison.Bars, bar => Assert.Equal(100, bar.Confidence));
    }

    [Fact]
    public void Each_bar_names_what_the_performer_does_differently()
    {
        PerformanceComparison comparison = Compare(Score(), Performance());
        BarDifference[] kinds = [.. comparison.Bars.Select(bar => bar.Difference)];

        Assert.Equal(
            [BarDifference.None, BarDifference.None, BarDifference.AddedNotes, BarDifference.DifferentBass, BarDifference.Simplified, BarDifference.None, BarDifference.Octave, BarDifference.Ornament],
            kinds);
    }

    [Fact]
    public void One_wrong_note_is_not_a_difference()
    {
        List<Note> score = Score();
        List<PlayedNote> played = [.. AsPrinted(score).Select(note => note.Id == "p_rh05" ? note with { Pitch = new Pitch(73) } : note)];
        BarComparison bar2 = Compare(score, played).Bars[1];

        Assert.Equal(["rh05"], bar2.PrintedNotHeard);
        Assert.Equal(BarDifference.None, bar2.Difference);
    }

    [Fact]
    public void A_quiet_overtone_is_a_likely_artefact_not_a_difference()
    {
        BarComparison bar2 = Compare(Score(), Performance()).Bars[1];

        Assert.Equal(["overtone"], bar2.LikelyArtefacts.Select(note => note.Id));
        Assert.Empty(bar2.PlayedNotPrinted);
    }

    [Fact]
    public void A_very_low_printed_note_not_heard_is_probably_not_heard()
    {
        BarComparison bar6 = Compare(Score(), Performance()).Bars[5];

        Assert.Equal(["low"], bar6.ProbablyNotHeard);
        Assert.Empty(bar6.PrintedNotHeard);
        Assert.Equal(BarDifference.None, bar6.Difference);
    }

    [Fact]
    public void The_lists_name_the_notes_on_each_side()
    {
        PerformanceComparison comparison = Compare(Score(), Performance());

        Assert.Equal(["fill1", "fill2", "fill3"], comparison.Bars[2].PlayedNotPrinted.Select(note => note.Id));
        Assert.Equal(["lh42a", "lh42b", "rh19"], comparison.Bars[4].PrintedNotHeard.Order(StringComparer.Ordinal));
        Assert.Equal(["lh30a"], comparison.Bars[3].PrintedNotHeard);
        Assert.Equal([43], comparison.Bars[3].PlayedNotPrinted.Select(note => note.Pitch.MidiNumber));
    }

    [Fact]
    public void Bars_without_anchors_claim_nothing()
    {
        List<Note> score = Score();

        // The first two bars are played freely: every note a semitone away, so nothing there can anchor.
        List<PlayedNote> played = [.. AsPrinted(score).Select(note => note.Onset < Time(2 * BarTicks) ? note with { Pitch = new Pitch(note.Pitch.MidiNumber + 1) } : note)];
        PerformanceComparison comparison = Compare(score, played);

        Assert.False(comparison.Bars[0].Aligned);
        Assert.False(comparison.Bars[1].Aligned);
        Assert.Empty(comparison.Bars[0].PrintedNotHeard);
        Assert.Empty(comparison.Bars[0].PlayedNotPrinted);
        Assert.Equal(BarDifference.None, comparison.Bars[0].Difference);
        Assert.All(comparison.Bars.Skip(2), bar => Assert.True(bar.Aligned));
    }

    [Fact]
    public void The_same_notes_give_the_same_comparison()
    {
        PerformanceComparison first = Compare(Score(), Performance());
        List<PlayedNote> shuffled = [.. Performance().OrderByDescending(note => note.Id, StringComparer.Ordinal)];
        PerformanceComparison second = Compare(Score(), shuffled);

        Assert.Equal(first.Alignment.Anchors, second.Alignment.Anchors);
        Assert.Equal(first.Bars.Select(bar => bar.Difference), second.Bars.Select(bar => bar.Difference));
        Assert.Equal(first.MatchedCount, second.MatchedCount);
    }

    [Fact]
    public void Anchors_kept_in_a_project_rebuild_the_same_map()
    {
        List<Note> score = Score();
        PerformanceAlignment aligned = PerformanceAlignment.Align(score, AsPrinted(score));
        PerformanceAlignment rebuilt = PerformanceAlignment.FromAnchors(aligned.Anchors);

        Assert.Equal(aligned.MicrosecondsAt(new Tick(1234)), rebuilt.MicrosecondsAt(new Tick(1234)));
        Assert.Throws<ArgumentException>(() => PerformanceAlignment.FromAnchors([new Anchor(new Tick(480), 1_000), new Anchor(new Tick(480), 2_000)]));
    }

    [Fact]
    public void Confirmed_anchors_are_kept_and_the_notes_between_them_aligned_on_their_own()
    {
        List<Note> score = Score();
        PerformanceAlignment found = PerformanceAlignment.Align(score, AsPrinted(score));
        Anchor confirmed = new(found.Anchors[2].Score, found.Anchors[3].Microseconds);

        PerformanceAlignment aligned = PerformanceAlignment.Align(score, AsPrinted(score), [confirmed]);

        Assert.Contains(confirmed, aligned.Anchors);
        Assert.Equal(found.Anchors[2].Score, aligned.TickAt(confirmed.Microseconds));
        Assert.Equal(PerformanceAlignment.FromAnchors(aligned.Anchors).Anchors, aligned.Anchors);
        Assert.Equal(found.Anchors, PerformanceAlignment.Align(score, AsPrinted(score), []).Anchors);
    }

    [Fact]
    public void After_the_last_confirmed_anchor_the_music_follows_it_and_the_gap_falls_at_the_end()
    {
        // Eight bars alike, of which two are played, confirmed to start at bar 3: bar 3's second beat is the second note played.
        int[] melody = [72, 74, 76, 77];
        List<Note> score = [.. Enumerable.Range(0, Bars * 4).Select(i => Printed($"q{i:D2}", melody[i % 4], i * Quarter))];
        List<PlayedNote> played = AsPrinted(score.Take(8));
        Anchor confirmed = new(new Tick(2 * BarTicks), played[0].Onset);

        PerformanceAlignment aligned = PerformanceAlignment.Align(score, played, [confirmed]);

        Assert.Equal(new Anchor(new Tick((2 * BarTicks) + Quarter), played[1].Onset), aligned.Anchors.SkipWhile(anchor => anchor != confirmed).ElementAt(1));
    }

    [Fact]
    public void A_recording_of_the_whole_piece_is_compared_with_its_opening()
    {
        // Public-domain material: Für Elise's opening as printed, and Transkun's hearing of a recording of the whole piece.
        // Notes played just before the opening ends, early for a bar it does not have, are counted in its last bar.
        ImportedPiece piece = Piece("Experiments", "Fixtures", "fur-elise-original.musicxml");
        IReadOnlyList<PlayedNote> played = Recording("transkun-fur-elise.mid");

        PerformanceComparison comparison = PerformanceComparison.Compare(piece.Notes, piece.Meter, played, PerformanceAlignment.Align(piece.Notes, played));

        Assert.Equal(piece.Notes.Count, comparison.MatchedCount);
        Assert.Empty(comparison.Differences);
    }

    private static ImportedPiece Piece(params string[] path) =>
        PieceImporter.Import(path[^1], File.ReadAllBytes(Path.Combine([AppContext.BaseDirectory, .. path]))).Piece!;

    private static IReadOnlyList<PlayedNote> Recording(string name) =>
        PlayedNotes.FromMidi(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Transcription", "Fixtures", name)));

    /// <summary>The score played as printed but for bar 3's melody, played backwards, and a very quiet extra note in bar 4.</summary>
    private static List<PlayedNote> BackwardsMelody(List<Note> score)
    {
        string[] bar3 = ["rh08", "rh09", "rh10", "rh11"];
        List<PlayedNote> played = AsPrinted(score.Select(note => Array.IndexOf(bar3, note.Id) is var i and >= 0
            ? note with { Span = new TickSpan(new Tick((2 * BarTicks) + ((3 - i) * Quarter)), note.Span.Duration) }
            : note));
        played.Add(new PlayedNote("quiet", new Pitch(90), Time((3 * BarTicks) + Quarter), 10, 100_000));
        return played;
    }

    /// <summary>The print, the meter and the performance of a golden case.</summary>
    private static (IReadOnlyList<Note> Score, MeterMap Meter, IReadOnlyList<PlayedNote> Played) Case(string name)
    {
        List<Note> score = Score();
        return name switch
        {
            "eight-bars" => (score, MeterMap.Default, Performance()),
            "eight-bars-free-opening" => (score, MeterMap.Default, [.. AsPrinted(score).Select(note => note.Onset < Time(2 * BarTicks) ? note with { Pitch = new Pitch(note.Pitch.MidiNumber + 1) } : note)]),
            "eight-bars-backwards-melody" => (score, MeterMap.Default, BackwardsMelody(score)),
            "fur-elise-transkun" => Real(Piece("Experiments", "Fixtures", "fur-elise-original.musicxml"), Recording("transkun-fur-elise.mid")),
            "kit-elise-transkun" => Real(Piece("Golden", "Analysis", "elise.musicxml"), Recording("transkun-fur-elise.mid")),
            "ode-to-joy-basic-pitch" => Real(Piece("Experiments", "Fixtures", "ode-to-joy.musicxml"), Recording("basic-pitch-melody.mid")),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown case."),
        };

        static (IReadOnlyList<Note>, MeterMap, IReadOnlyList<PlayedNote>) Real(ImportedPiece piece, IReadOnlyList<PlayedNote> played) => (piece.Notes, piece.Meter, played);
    }

    /// <summary>
    /// Every bar of the comparison, a bar a line, kept character for character (Golden/*.txt): whether it is aligned, its
    /// difference and confidence, the notes on each side and the marker in both languages. With MOTIFF_UPDATE_SNAPSHOTS=1 the
    /// golden is written again, when it changes on purpose.
    /// </summary>
    [Theory]
    [InlineData("eight-bars")]
    [InlineData("eight-bars-free-opening")]
    [InlineData("eight-bars-backwards-melody")]
    [InlineData("fur-elise-transkun")]
    [InlineData("kit-elise-transkun")]
    [InlineData("ode-to-joy-basic-pitch")]
    public void Every_bar_is_compared_as_before(string name)
    {
        (IReadOnlyList<Note> score, MeterMap meter, IReadOnlyList<PlayedNote> played) = Case(name);
        PerformanceComparison comparison = PerformanceComparison.Compare(score, meter, played, PerformanceAlignment.Align(score, played));
        static string Listed(IEnumerable<string> items) => $"[{string.Join(' ', items)}]";
        static string Placed(IEnumerable<PlacedNote> notes) => Listed(notes.Select(note => $"{note.Id}:{note.Pitch.MidiNumber}@{note.At.Value}v{note.Velocity}"));
        var text = new StringBuilder().Append($"matched {comparison.MatchedCount}, anchors {comparison.Alignment.Anchors.Count}\n");
        foreach (BarComparison bar in comparison.Bars)
        {
            text.Append($"bar {bar.Bar} {(bar.Aligned ? "aligned" : "free")} {bar.Difference} {bar.Confidence}% «{bar.Marker(Words.English)}» «{bar.Marker(Words.Russian)}»")
                .Append($" | matched {Listed(bar.Matched.Select(match => $"{match.PrintedId}={match.PlayedId}{match.Offset:+0;-0;+0}"))}")
                .Append($" | not heard {Listed(bar.PrintedNotHeard)} | probably not heard {Listed(bar.ProbablyNotHeard)}")
                .Append($" | not printed {Placed(bar.PlayedNotPrinted)} | artefacts {Placed(bar.LikelyArtefacts)}\n");
        }

        TextGoldens.AssertTextGolden("Performance", $"{name}.txt", text.ToString());
    }
}