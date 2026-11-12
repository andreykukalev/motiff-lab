using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>
/// A piece from a concert (version 0.5): a stretch of a recording heard, by time, cut into a piece of its own whose bars
/// and tempo are found from its stretch alone, with the stretch of the sound it takes; the concert is never changed.
/// </summary>
public class HeardCutTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 12, 16, 0, 0, TimeSpan.Zero);

    private static int s_ids;

    private static PieceSplitOptions Options() => new(s_now, () => $"p_cut{Interlocked.Increment(ref s_ids)}") { FreshBars = true, Device = HoldingDevice.Web };

    /// <summary>When the first piece's beats fall (µs), at 120 BPM; then 4 s of silence; then the second's, at 80 BPM.</summary>
    private static long[] Onsets() =>
    [
        .. Enumerable.Range(0, 48).Select(static beat => beat * 500_000L),
        .. Enumerable.Range(0, 48).Select(static beat => 28_000_000L + (beat * 750_000L)),
    ];

    /// <summary>
    /// A concert heard: two pieces as an engine writes them at 120 BPM in 4/4, the first in C at 120, the second in E flat
    /// at 80, a silence of about 4 s between them, with the bars found over the whole concert and a seam at 40 s.
    /// </summary>
    internal static Project Concert()
    {
        long[] onsets = Onsets();
        Note[] notes = [.. onsets.Select((at, i) => new Note
        {
            Id = $"c{i}",
            Pitch = new Pitch(i < 48 ? (i % 4 == 0 ? 48 : 60 + (i % 3 * 4)) : (i % 4 == 0 ? 51 : 63 + (i % 3 * 3))),
            Span = new TickSpan(new Tick(at * 480 / 500_000), 200),
            Velocity = 70,
            Track = 0,
            Attribution = Attribution.Imported,
        })];
        byte[] midi = MidiExporter.Export(notes, [], MeterMap.Default, "concert");
        BarGrid? grid = BarGrid.Find(PieceImporter.Import("concert.mid", midi).Piece!.Midi!);
        Project project = Project.Create("p_concert", "concert.mid", midi, s_now) with
        {
            Transcription = new TranscriptionOrigin("transkun 2", grid) { Seams = [40_000], At = s_now },
        };
        ImportedPiece piece = OpenedProject.Open(project).Piece;
        return project with { Check = CheckFlag.FromTranscription(project.Transcription!, midi, piece) };
    }

    [Fact]
    public void The_clock_of_a_recording_heard_turns_its_ticks_into_the_time_they_were_played_and_back()
    {
        Project concert = Concert();
        ImportedPiece piece = OpenedProject.Open(concert).Piece;
        HeardClock clock = HeardClock.Of(concert, piece)!;

        Assert.Null(HeardClock.Of(Project.Create("p_score", "two.musicxml", [1], s_now), piece));
        foreach (long at in Onsets())
        {
            Tick tick = clock.TickAt(at);
            Assert.InRange(at - clock.MicrosecondsAt(tick), 0, 2_000);
        }

        Note last = piece.Notes.MaxBy(static note => note.Span.Onset)!;
        Assert.InRange(clock.MillisecondsAt(last.Span.Onset), 63_249, 63_251);
        Assert.Equal(Tick.Zero, clock.TickAt(-5));
    }

    [Fact]
    public void A_piece_cut_from_a_concert_by_time_finds_its_own_bars_and_plays_its_stretch_as_it_was_played()
    {
        Project concert = Concert();
        byte[] before = ProjectFile.Write(concert);

        PieceSplit split = PieceParts.Split(concert, [PartRange.InRecording("A", 0, 25_000), PartRange.InRecording("B", 25_000, 70_000) with { Composer = "Chopin", Tags = ["nocturne"] }], Options());

        Assert.Equal(before, ProjectFile.Write(concert));
        PiecePart second = split.Parts[1];
        Assert.Equal((25_000L, 70_000L), second.Milliseconds);
        Assert.Equal(PartOf.InRecording(PieceParts.OriginalId, 25_000, 70_000), second.Project.PartOf);
        Assert.Equal(48, second.Notes.Count);
        Assert.Equal(Tick.Zero, second.Notes.Min(static note => note.Span.Onset));
        Assert.Equal("Chopin", second.Project.Metadata.Composer);
        Assert.Equal(["nocturne"], second.Project.Metadata.Tags);

        // Its grid is its own, from its first note, and each note sounds when it was played in the concert.
        TranscriptionOrigin heard = second.Project.Transcription!;
        Assert.NotNull(heard.Bars);
        Assert.NotEqual(concert.Transcription!.Bars, heard.Bars);
        Assert.Equal((PieceParts.OriginalId, s_now), (heard.From, heard.At));
        ImportedPiece own = OpenedProject.Open(second.Project).Piece;
        HeardClock clock = HeardClock.Of(second.Project, own)!;
        long[] played = [.. own.Notes.OrderBy(static note => note.Span.Onset).Select(note => clock.MicrosecondsAt(note.Span.Onset) + 25_000_000)];
        long[] expected = [.. Onsets().Skip(48)];
        Assert.All(played.Zip(expected), pair => Assert.InRange(pair.First - pair.Second, -3_000, 3_000));

        // The seam at 40 s is at 15 s of the piece, and its Check flags are worked out for its own bars.
        Assert.Equal([15_000L], heard.Seams);
        Assert.Contains(second.Project.Check!, flag => flag.Reason == CheckReason.PartSeam);
        Assert.Empty(split.Parts[0].Project.Transcription!.Seams);
        Assert.DoesNotContain(split.Parts[0].Project.Check!, flag => flag.Reason == CheckReason.PartSeam);

        Project read = ProjectFile.Read(ProjectFile.Write(second.Project));
        Assert.Equal(second.Notes.Select(static note => note.Span), OpenedProject.Open(read).Notes.Select(static note => note.Span));
    }

    [Fact]
    public void A_piece_of_the_layer_takes_a_second_before_its_first_note_and_two_after_its_last_at_most()
    {
        Project concert = Concert();
        ImportedPiece piece = OpenedProject.Open(concert).Piece;
        HeardClock clock = HeardClock.Of(concert, piece)!;
        Tick boundary = clock.TickAt(28_000_000);

        (long from, long to) = HeardCut.StretchOf(piece, clock, TickSpan.FromBounds(Tick.Zero, boundary));
        Assert.Equal(0, from);
        Assert.InRange(to, 25_700, 25_720);
        (long secondFrom, long secondTo) = HeardCut.StretchOf(piece, clock, TickSpan.FromBounds(boundary, piece.End));
        Assert.InRange(secondFrom, 26_990, 27_010);
        Assert.InRange(secondTo, 65_450, 65_470);
        Assert.True(to <= secondFrom);
    }

    [Fact]
    public void Without_fresh_bars_a_part_by_time_keeps_the_concerts_bars_and_a_stretch_without_notes_is_refused()
    {
        Project concert = Concert();

        PiecePart kept = PieceParts.Split(concert, [PartRange.InRecording("B", 25_000, 70_000)], Options() with { FreshBars = false }).Parts[0];
        Assert.Equal(concert.Transcription!.Bars!.Signature, kept.Project.Transcription!.Bars!.Signature);
        Assert.Throws<ArgumentException>(() => PieceParts.Split(concert, [PartRange.InRecording("Silence", 24_500, 27_500)], Options()));
        Assert.Throws<ArgumentException>(() => PieceParts.Split(concert, [PartRange.InRecording("Back", 9_000, 5_000)], Options()));
        Assert.Throws<ArgumentException>(() => PieceParts.Split(Project.Create("p_score", "two.musicxml", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Analysis", "Fixtures", "two-pieces.musicxml")), s_now), [PartRange.InRecording("A", 0, 5_000)], Options()));
    }

    [Fact]
    public void The_same_concert_and_stretches_give_the_same_files()
    {
        PartRange[] parts = [PartRange.InRecording("A", 0, 25_000) with { Id = "p_a" }, PartRange.InRecording("B", 25_000, 70_000) with { Id = "p_b" }];

        Assert.Equal(
            PieceParts.Split(Concert(), parts, Options()).Parts.Select(static part => ProjectFile.Write(part.Project)),
            PieceParts.Split(Concert(), parts, Options()).Parts.Select(static part => ProjectFile.Write(part.Project)));
    }

    [Fact]
    public void A_pieces_own_sound_is_described_as_kept_on_the_device_that_cut_it()
    {
        PiecePart part = PieceParts.Split(Concert(), [PartRange.InRecording("Nocturne", 25_000, 70_000)], Options()).Parts[0];
        string sha = new('a', 64);

        Project project = PieceParts.WithSound(part.Project, 1_984_000, 45_000, sha, HoldingDevice.Web, s_now);

        PieceOriginal sound = project.FindOriginal(PieceParts.SoundId)!;
        Assert.Equal((OriginalKind.Audio, OriginalKept.Device, "Nocturne.wav"), (sound.Kind, sound.Kept, sound.FileName));
        Assert.Equal((1_984_000L, 45_000L, sha), (sound.Bytes, sound.DurationMs, sound.Sha256));
        Assert.True(sound.IsHeldBy(HoldingDevice.Web));
        Assert.Equal(sound, ProjectFile.Read(ProjectFile.Write(project)).FindOriginal(PieceParts.SoundId));
    }

    [Fact]
    public void The_facade_splits_a_recording_by_time_on_fresh_bars_and_proposes_each_pieces_stretch()
    {
        var facade = new MusicCoreFacade();
        string file = Encoding.UTF8.GetString(ProjectFile.Write(Concert()));
        string request = JsonSerializer.Serialize(new
        {
            file,
            bars = "fresh",
            parts = new object[] { new { title = "B", ms = new { from = 25_000, to = 70_000 }, composer = "Chopin", tags = new[] { "nocturne" } } },
            device = "web",
            now = "2026-11-12T16:00:00Z",
        });

        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "parts.split", request));

        JsonElement part = answer.RootElement.GetProperty("parts")[0];
        Assert.Equal(25_000, part.GetProperty("milliseconds").GetProperty("from").GetInt64());
        Assert.Equal("Chopin", part.GetProperty("project").GetProperty("composer").GetString());
        using JsonDocument wrong = JsonDocument.Parse(facade.Call(0, "parts.split", request.Replace("\"fresh\"", "\"new\"", StringComparison.Ordinal)));
        Assert.Equal("badRequest", wrong.RootElement.GetProperty("error").GetString());

        Assert.Equal(1, JsonDocument.Parse(facade.Open("concert.motiff.json", Encoding.UTF8.GetBytes(file))).RootElement.GetProperty("piece").GetInt32());
        JsonElement[] proposals = [.. JsonDocument.Parse(facade.Call(1, "pieces.find", """{"count": 2}""")).RootElement.GetProperty("proposals").EnumerateArray()];
        Assert.Equal(2, proposals.Length);
        Assert.Equal(0, proposals[0].GetProperty("ms").GetProperty("from").GetInt64());
        Assert.InRange(proposals[1].GetProperty("ms").GetProperty("from").GetInt64(), 26_000, 28_000);
    }
}