using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>The doubtful bars of a transcription, each with its reason, for the Check layer (decision record 0018, section 9; preparation item 12).</summary>
public class TranscriptionCheckTests
{
    private const long Quarter = 480;
    private const long Bar = 4 * Quarter;

    private static Note Note(string id, int pitch, long onset, long duration = Quarter) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    /// <summary>The engine's MIDI for the notes, at 120 BPM in 4/4 as an engine writes it, and the piece as opened (no bars worked out).</summary>
    private static (byte[] Midi, ImportedPiece Piece) Heard(IEnumerable<Note> notes)
    {
        byte[] midi = MidiExporter.Export([.. notes], [], MeterMap.Default, "recording");
        ImportedPiece piece = Transcribed.Mark(PieceImporter.Import("recording.mid", midi).Piece!);
        return (midi, piece);
    }

    /// <summary>Sixteen bars of a quarter-note melody, one note at a time, rising and falling within an octave.</summary>
    private static List<Note> Melody() =>
        [.. Enumerable.Range(0, 64).Select(i => Note($"m{i:D2}", 67 + Math.Abs((i % 8) - 4), i * Quarter))];

    private static (int Bar, CheckReason Reason)[] Flags(IReadOnlyList<CheckFlag> flags) => [.. flags.Select(flag => (flag.Bar, flag.Reason))];

    [Fact]
    public void A_seam_flags_the_bars_of_the_notes_either_side_of_it()
    {
        (byte[] midi, ImportedPiece piece) = Heard(Melody());

        // At 120 BPM a quarter is 500 ms: 9 s is the middle of bar 5, 16 s the start of bar 9.
        IReadOnlyList<CheckFlag> flags = CheckFlag.FromTranscription(new TranscriptionOrigin("transkun 2") { Seams = [9_000, 16_000] }, midi, piece);

        Assert.Equal([(5, CheckReason.PartSeam), (8, CheckReason.PartSeam), (9, CheckReason.PartSeam)], Flags(flags));
        Assert.All(flags, flag => Assert.Equal((CheckSource.Transcription, false), (flag.Source, flag.Resolved)));
        Assert.Empty(CheckFlag.FromTranscription(new TranscriptionOrigin("transkun 2"), midi, piece));
        Assert.Equal("Здесь склеены две части записи: нота могла потеряться или удвоиться.", flags[0].Describe(Words.Russian));
    }

    [Fact]
    public void Notes_crowded_within_a_16th_or_seconds_struck_together_are_a_cluster()
    {
        List<Note> notes = Melody();
        // Bar 3: four onsets within a 16th. Bar 6: C, D and E struck together.
        notes.AddRange([Note("r1", 80, (2 * Bar) + 30, 60), Note("r2", 81, (2 * Bar) + 60, 60), Note("r3", 82, (2 * Bar) + 90, 60)]);
        notes.AddRange([Note("c1", 60, 5 * Bar), Note("c2", 62, 5 * Bar), Note("c3", 64, 5 * Bar)]);
        (byte[] midi, ImportedPiece piece) = Heard(notes);

        IReadOnlyList<CheckFlag> flags = CheckFlag.FromTranscription(new TranscriptionOrigin("transkun 2"), midi, piece);

        Assert.Equal([3, 6], flags.Where(flag => flag.Reason == CheckReason.DenseCluster).Select(flag => flag.Bar));
    }

    [Fact]
    public void A_reason_found_in_more_than_a_quarter_of_the_bars_is_left_out()
    {
        // Seconds struck together in every bar: a cluster that says nothing about any bar in particular.
        List<Note> notes = [.. Enumerable.Range(0, 16).SelectMany(bar => new[] { Note($"a{bar}", 60, bar * Bar, Bar), Note($"b{bar}", 61, bar * Bar, Bar), Note($"c{bar}", 62, bar * Bar, Bar) })];
        (byte[] midi, ImportedPiece piece) = Heard(notes);

        Assert.DoesNotContain(CheckFlag.FromTranscription(new TranscriptionOrigin("transkun 2"), midi, piece), flag => flag.Reason == CheckReason.DenseCluster);
    }

    [Fact]
    public void On_the_bars_worked_out_from_a_real_recording_the_flags_are_few_sorted_and_the_same_every_time()
    {
        byte[] midi = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Transcription", "Fixtures", "transkun-fur-elise.mid"));
        ImportedPiece engine = PieceImporter.Import("transkun-fur-elise.mid", midi).Piece!;
        BarGrid grid = BarGrid.Find(engine.Midi!)!;
        ImportedPiece piece = Transcribed.Mark(grid.Apply(engine));
        var origin = new TranscriptionOrigin("transkun 2", grid) { Seams = [20_000] };

        IReadOnlyList<CheckFlag> flags = CheckFlag.FromTranscription(origin, midi, piece);

        int bars = piece.Meter.BarsBefore(piece.End).Count();
        Assert.Contains(flags, flag => flag.Reason == CheckReason.PartSeam);
        foreach (IGrouping<CheckReason, CheckFlag> reason in flags.GroupBy(flag => flag.Reason).Where(group => group.Key != CheckReason.PartSeam))
        {
            Assert.InRange(reason.Count(), 1, Math.Max(1, (int)(bars * CheckFlag.MostFlaggedShare)));
        }

        Assert.All(flags, flag => Assert.InRange(flag.Bar, piece.Meter.FirstBarNumber, bars));
        Assert.Equal(flags.OrderBy(flag => flag.Bar).ThenBy(flag => flag.Reason), flags);
        Assert.Equal(flags.Count, flags.Distinct().Count());
        Assert.Equal(flags, CheckFlag.FromTranscription(origin, midi, piece));

        // Hands guessed: bars with at least two notes whose hand was inferred with little confidence.
        int[] guessed = [.. piece.Notes
            .Where(note => note.Hand is { Attribution: { Provenance: Provenance.Inferred, Confidence: < CheckFlag.GuessedHandConfidence } })
            .GroupBy(note => piece.Meter.BarAt(note.Span.Onset).Number)
            .Where(bar => bar.Count() >= CheckFlag.FewestGuessedHands)
            .Select(bar => bar.Key)
            .Order()];
        int[] flagged = [.. flags.Where(flag => flag.Reason == CheckReason.HandsGuessed).Select(flag => flag.Bar)];
        Assert.True(flagged.Length == 0 ? guessed.Length > bars * CheckFlag.MostFlaggedShare || guessed.Length == 0 : flagged.SequenceEqual(guessed));
    }

    [Fact]
    public void Seams_are_kept_with_the_project_and_an_older_file_opens_with_its_flags_worked_out()
    {
        (byte[] midi, _) = Heard(Melody());
        Project project = Project.Create("p_heard", "recording.mid", midi, DateTimeOffset.Parse("2026-11-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture)) with
        {
            Transcription = new TranscriptionOrigin("transkun 2") { Seams = [9_000, 16_000] },
        };

        Project read = ProjectFile.Read(ProjectFile.Write(project));
        Assert.Equal([9_000L, 16_000L], read.Transcription!.Seams);
        Assert.Empty(ProjectFile.Read(ProjectFile.Write(project with { Transcription = new TranscriptionOrigin("transkun 2") })).Transcription!.Seams);
        Assert.Throws<ArgumentException>(() => new TranscriptionOrigin("transkun 2") { Seams = [16_000, 9_000] });

        // Check is null for a file from before version 12: opening works the flags out from the piece.
        OpenedProject opened = OpenedProject.Open(project with { Check = null });
        Assert.Equal([(5, CheckReason.PartSeam), (8, CheckReason.PartSeam), (9, CheckReason.PartSeam)], Flags(opened.Project.Check!));
    }
}