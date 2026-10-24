using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Export;

public class MidiExporterTests
{
    private const long Quarter = 480;

    private static readonly MeterMap s_threeFour = new([new MeterChange(Tick.Zero, new TimeSignature(3, 4))]);

    private static Note Note(string id, int pitch, long onset, long duration, Hand? hand = null, int track = 0, int velocity = 80) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = velocity,
        Track = track,
        Hand = hand is { } value ? new Attributed<Hand>(value, Attribution.Imported) : null,
        Attribution = Attribution.Imported,
    };

    private static MidiImport ImportFixture(string name) =>
        MidiImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "Midi", "Fixtures", name)));

    private static IEnumerable<(long Onset, long Duration, int Pitch, int Velocity)> Sounds(IEnumerable<Note> notes, long shift = 0) => notes
        .Select(note => (note.Span.Onset.Value - shift, note.Span.Duration, note.Pitch.MidiNumber, note.Velocity))
        .Order();

    /// <summary>AC-S1-19: a variation exports with the right tempo, one track per hand and the variation's notes.</summary>
    [Fact]
    public void A_variation_round_trips_through_the_importer_with_its_tempo_one_track_per_hand_and_its_notes()
    {
        Note[] piece =
        [
            Note("r1", 72, 0, Quarter, Hand.Right), Note("r2", 74, Quarter, Quarter, Hand.Right),
            Note("r3", 76, 2 * Quarter, Quarter, Hand.Right), Note("r4", 79, 3 * Quarter, Quarter, Hand.Right),
            Note("l1", 48, 0, 2 * Quarter, Hand.Left, velocity: 60), Note("l2", 43, 2 * Quarter, 2 * Quarter, Hand.Left, velocity: 60),
        ];
        MotifSelection selection = MotifSelector.Select(piece, MeterMap.Default, new TickSpan(Tick.Zero, 4 * Quarter), SelectionFilter.All);
        Variation variation = RhythmReshape.Instance.Apply(piece, selection, new HashSet<KeepProperty>(), new RhythmReshapeParameters(RhythmReshapeKind.Syncopate));
        PlaybackTempo[] tempos = [new(Tick.Zero, 625_000)];

        MidiImport back = MidiImporter.Import(MidiExporter.Export(variation.Notes, tempos, MeterMap.Default, "Study"));

        Assert.Equal(1, back.Format);
        Assert.Equal(480, back.TimeBase.TicksPerQuarter);
        Assert.Equal(["Study", "Right hand", "Left hand"], back.Tracks.Select(track => track.Name));
        Assert.Equal(new MidiTempoChange(Tick.Zero, 0, 625_000, false), Assert.Single(back.Tempos));
        Assert.Equal(new MidiTimeSignature(Tick.Zero, 0, 4, 4, false), Assert.Single(back.TimeSignatures));
        Assert.Equal(Sounds(variation.Notes.Where(note => note.Hand!.Value == Hand.Right)), Sounds(back.Notes.Where(note => note.Track == 1)));
        Assert.Equal(Sounds(variation.Notes.Where(note => note.Hand!.Value == Hand.Left)), Sounds(back.Notes.Where(note => note.Track == 2)));
        Assert.Empty(back.Report.Issues);
    }

    [Fact]
    public void A_motif_starts_at_its_bar_line_with_the_tempo_and_meter_in_effect_there()
    {
        MidiImport source = ImportFixture("two-hands-type1.mid");
        Note[] bar3 = [.. source.Notes.Where(note => note.Span.Onset.Value >= 2 * 3 * Quarter && note.Span.Onset.Value < 3 * 3 * Quarter)];
        PlaybackTempo[] tempos = [.. source.Tempos.Select(tempo => new PlaybackTempo(tempo.At, tempo.MicrosecondsPerQuarter))];

        MidiImport back = MidiImporter.Import(MidiExporter.Export(bar3, tempos, source.Meter, "Waltz", from: new Tick((2 * 3 * Quarter) + Quarter)));

        Assert.Equal(new MidiTimeSignature(Tick.Zero, 0, 3, 4, false), Assert.Single(back.TimeSignatures));
        Assert.Equal(60_000_000 / 72, Assert.Single(back.Tempos).MicrosecondsPerQuarter);
        Assert.Equal(Sounds(bar3, shift: 2 * 3 * Quarter), Sounds(back.Notes));
    }

    [Fact]
    public void Notes_without_hands_get_one_track_per_source_track()
    {
        Note[] notes = [Note("a", 60, 0, Quarter, track: 0), Note("b", 64, 0, Quarter, track: 2), Note("c", 67, Quarter, Quarter, track: 2)];

        MidiImport back = MidiImporter.Import(MidiExporter.Export(notes, [], s_threeFour, "Parts", trackName: track => track == 2 ? "Violin" : null));

        Assert.Equal(["Parts", "Track 1", "Violin"], back.Tracks.Select(track => track.Name));
        Assert.Equal(PlaybackTempo.DefaultMicrosecondsPerQuarter, Assert.Single(back.Tempos).MicrosecondsPerQuarter);
        Assert.Equal(2, back.Tracks[2].NoteCount);
    }

    [Fact]
    public void A_key_struck_again_while_it_sounds_is_released_first()
    {
        Note[] notes = [Note("a", 60, 0, 2 * Quarter, Hand.Right), Note("b", 60, Quarter, Quarter, Hand.Right)];

        MidiImport back = MidiImporter.Import(MidiExporter.Export(notes, [], MeterMap.Default, "Again"));

        Assert.Equal([(0L, Quarter), (Quarter, Quarter)], back.Notes.Select(note => (note.Span.Onset.Value, note.Span.Duration)).Order());
        Assert.Empty(back.Report.Issues);
    }

    [Fact]
    public void A_whole_imported_file_round_trips()
    {
        MidiImport source = ImportFixture("performance-click.mid");
        PlaybackTempo[] tempos = [.. source.Tempos.Select(tempo => new PlaybackTempo(tempo.At, tempo.MicrosecondsPerQuarter))];

        byte[] first = MidiExporter.Export(source.Notes, tempos, source.Meter, "Played");
        MidiImport back = MidiImporter.Import(first);

        Assert.Equal(Sounds(source.Notes), Sounds(back.Notes));
        Assert.Equal(first, MidiExporter.Export(source.Notes, tempos, source.Meter, "Played"));
    }

    [Theory]
    [InlineData("Für Elise", "bars 1–2", "Syncopate", "Für Elise – bars 1–2 – Syncopate.mid")]
    [InlineData("Für Elise", null, null, "Für Elise.mid")]
    [InlineData("a/b: c?", "bar 3", " ", "a_b_ c_ – bar 3.mid")]
    [InlineData("  ", null, null, "Motiff Lab.mid")]
    public void Files_are_named_after_the_project_motif_and_variation(string project, string? motif, string? variation, string expected) =>
        Assert.Equal(expected, MidiExporter.FileName(project, motif, variation));

    [Fact]
    public void Nothing_to_export_is_refused()
    {
        Assert.Throws<ArgumentException>(() => MidiExporter.Export([], [], MeterMap.Default, "Empty"));
        Assert.Throws<ArgumentException>(() => MidiExporter.Export([Note("a", 60, 0, Quarter)], [], MeterMap.Default, "Late", from: new Tick(4 * Quarter)));
    }
}