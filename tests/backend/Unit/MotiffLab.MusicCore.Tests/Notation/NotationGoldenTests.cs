using System.Globalization;
using System.Text;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Tests.Golden;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Notation;

/// <summary>
/// The notation written for real pieces, kept character for character (Golden/*.txt): the MEI and every count the
/// generator reports, for a score, a scan, a MIDI file and a recording in each way the facade writes it, an excerpt with
/// ossias, a transposed piece and compound meters. A change to how notes are written shows here line by line; with
/// MOTIFF_UPDATE_SNAPSHOTS=1 the goldens are written again, when it changes on purpose.
/// </summary>
public class NotationGoldenTests
{
    private static byte[] Fixture(params string[] path) => File.ReadAllBytes(Path.Combine([AppContext.BaseDirectory, .. path]));

    private static MusicXmlImport Score(string folder, string name) => MusicXmlImporter.Import(Fixture("Import", folder, "Fixtures", name));

    private static MidiImport Midi(string folder, string name) => MidiImporter.Import(Fixture("Import", folder, "Fixtures", name));

    private static KeyChange[] Keys(MidiImport import) => [.. import.KeySignatures.Select(key => new KeyChange(key.At, Math.Clamp(key.Sharps, -7, 7)))];

    private static KeyChange[] Keys(MusicXmlImport import) => [.. import.KeySignatures.Where(key => key.Track == 0).Select(key => new KeyChange(key.At, Math.Clamp(key.Fifths, -7, 7)))];

    /// <summary>
    /// How many bars of the recording the recording cases write: their notes start in the first 16 bars, and they write 17
    /// measures, because the bars reach the last note's end, which is held past the 16th bar line.
    /// </summary>
    private const int RecordingBars = 16;

    private static MusicXmlImport FurElise() => MusicXmlImporter.Import(Fixture("Experiments", "Fixtures", "fur-elise-original.musicxml"));

    [Theory]
    [InlineData("Experiments", "fur-elise-original.musicxml", "score-fur-elise")]
    [InlineData("MusicXml", "ties-triplets-pickup.musicxml", "score-ties-triplets-pickup")]
    [InlineData("MusicXml", "grand-staff.musicxml", "score-grand-staff")]
    [InlineData("MusicXml", "septuplets.musicxml", "score-septuplets")]
    [InlineData("MusicXml", "importer-two-parts.musicxml", "score-two-parts")]
    [InlineData("Scan", "homr-meter-change-mid-page.musicxml", "scan-meter-change-mid-page")]
    [InlineData("Scan", "homr-grace-eighths-4-4.musicxml", "scan-grace-eighths")]
    public void A_score_is_written_as_before(string folder, string fixture, string golden)
    {
        MusicXmlImport import = folder == "Experiments" ? FurElise() : Score(folder, fixture);

        AssertIs(golden, NotationGenerator.FromMusicXml(import, fixture, PlaybackScore.FromMusicXml(import).Tempos));
    }

    [Theory]
    [InlineData("two-hands-type1.mid", false, "midi-two-hands")]
    [InlineData("piano-single-track.mid", false, "midi-piano-single-track")]
    [InlineData("performance-click.mid", true, "midi-performance-as-heard")]
    [InlineData("free-tempo.mid", false, "midi-free-tempo")]
    public void A_midi_file_is_written_as_before(string fixture, bool asHeard, string golden)
    {
        MidiImport import = Midi("Midi", fixture);

        AssertIs(golden, NotationGenerator.FromMidi(import, fixture, asHeard, PlaybackScore.FromMidi(import).Tempos));
    }

    /// <summary>
    /// A recording's first <see cref="RecordingBars"/> bars in the three ways the facade writes one: for reading (notes held
    /// to the next, cut at it, a grace note), for reading with its marks, and as played.
    /// </summary>
    [Theory]
    [InlineData("recording-readable")]
    [InlineData("recording-reading-marks")]
    [InlineData("recording-as-played")]
    public void A_recording_is_written_as_before(string golden)
    {
        MidiImport import = Midi("Transcription", "transkun-fur-elise.mid");
        PlaybackScore played = PlaybackScore.FromMidi(import);
        KeyChange[] keys = Keys(import);
        Tick end = import.Meter.GetBar(RecordingBars + 1).Span.Onset;
        Note[] notes = [.. import.Notes.Where(note => note.Span.Onset < end)];

        GeneratedNotation notation = golden switch
        {
            "recording-readable" => NotationGenerator.Generate(notes, import.Meter, keys, end, golden, asHeard: true, tempos: played.Tempos),
            "recording-reading-marks" => NotationGenerator.Generate(notes, import.Meter, keys, end, golden, asHeard: true, playedTempos: played.Tempos, marks: PlayedMarks.Significant, tempos: played.Tempos),
            _ => NotationGenerator.Generate(notes, import.Meter, keys, end, golden, playedTempos: played.Tempos, pedal: played.Pedal.Select(held => held.Span), tempos: played.Tempos),
        };

        AssertIs(golden, notation);
    }

    /// <summary>Some bars of a piece with two ossias above: a variation an octave up, and a recording's notes written as played.</summary>
    [Fact]
    public void An_excerpt_with_ossias_is_written_as_before()
    {
        MusicXmlImport piece = FurElise();
        MidiImport recording = Midi("Transcription", "transkun-fur-elise.mid");
        PlaybackScore played = PlaybackScore.FromMidi(recording);
        Note[] higher = [.. piece.Notes.Select(note => note with { Pitch = new Pitch(note.Pitch.MidiNumber + 12) })];
        NotationOssia[] ossias =
        [
            new("Variation", higher),
            new("As played", recording.Notes, AsHeard: true, PlayedTempos: played.Tempos, Pedal: played.Pedal.Select(held => held.Span)),
        ];

        AssertIs("excerpt-ossias", NotationGenerator.Generate(piece.Notes, piece.Meter, Keys(piece), piece.End, "excerpt", bars: new BarRange(3, 9), ossias: ossias));
    }

    [Fact]
    public void A_transposed_piece_is_written_as_before()
    {
        MusicXmlImport piece = FurElise();
        TransposedPiece moved = Transposition.Of(piece.Notes, Keys(piece), new MusicalKey(0, KeyMode.Minor), new MusicalKey(-3, KeyMode.Minor));

        AssertIs("transposed-c-minor", NotationGenerator.Generate(moved.Notes, piece.Meter, moved.Keys, piece.End, "transposed", spelling: moved.Spelling));
    }

    /// <summary>A recording's first notes in 6/8, 12/8, 2/2, 9/8 and 5/8 after a pickup, with key changes, as compound, cut and odd meters write them.</summary>
    [Fact]
    public void Compound_meters_are_written_as_before()
    {
        Note[] notes = [.. Midi("Transcription", "transkun-fur-elise.mid").Notes.Where(note => note.Span.Onset.Value < 240 + 16320)];
        var meter = new MeterMap(
        [
            new MeterChange(Tick.Zero, new TimeSignature(6, 8)),
            new MeterChange(new Tick(240 + 2880), new TimeSignature(12, 8)),
            new MeterChange(new Tick(240 + 5760), new TimeSignature(2, 2)),
            new MeterChange(new Tick(240 + 9600), new TimeSignature(9, 8)),
            new MeterChange(new Tick(240 + 13920), new TimeSignature(5, 8)),
        ], pickupDuration: 240);
        KeyChange[] keys = [new(Tick.Zero, 2), new(new Tick(1440), -2), new(new Tick(6000), 3)];

        AssertIs("compound-meters", NotationGenerator.Generate(notes, meter, keys, new Tick(240 + 16320), "compound"));
    }

    /// <summary>
    /// A made-up playing over ten bars of 4/4, one track over both clefs, with what each mark is read from: an accent,
    /// triplets played a little late, a crescendo within a bar, legato, five overlapping notes, a grace note into a spread
    /// chord, the pedal, the bars slowing down and one beat held long.
    /// </summary>
    private static (Note[] Notes, PlaybackTempo[] Tempos, TickSpan[] Pedal) Playing()
    {
        var notes = new List<Note>();
        void Add(int pitch, long onset, long duration, int velocity) => notes.Add(new Note
        {
            Id = $"n_{notes.Count + 1:D4}",
            Pitch = new Pitch(pitch),
            Span = new TickSpan(new Tick(onset), duration),
            Velocity = velocity,
            Track = 0,
            Attribution = Attribution.Imported,
        });
        const long Bar = 1920;
        for (int bar = 0; bar < 10; bar++)
        {
            Add(48 + (bar % 3), bar * Bar, 960, 60);
            Add(43, (bar * Bar) + 960, 900, 60);
        }

        for (int beat = 0; beat < 4; beat++)
        {
            Add(72 + beat, beat * 480L, 480, beat == 1 ? 115 : 40);
        }

        for (int third = 0; third < 12; third++)
        {
            Add(74 + (third % 4), Bar + (third * 160L) + 7, 150, 50);
        }

        for (int eighth = 0; eighth < 8; eighth++)
        {
            Add(76 - (eighth % 3), (2 * Bar) + (eighth * 240L), 240, 50 + (7 * eighth));
        }

        for (int beat = 0; beat < 12; beat++)
        {
            Add(79 - (beat % 5), (3 * Bar) + (beat * 480L), beat % 4 == 3 ? 300 : 480, 95);
        }

        for (int voice = 0; voice < 5; voice++)
        {
            Add(84 - voice, (4 * Bar) + 960 + (voice * 60L), 700, 80);
        }

        Add(81, (6 * Bar) + 450, 20, 70);
        for (int spread = 0; spread < 3; spread++)
        {
            Add(76 + (4 * spread), (6 * Bar) + 480 + (spread * 20L), 900, 75);
        }

        for (int beat = 0; beat < 12; beat++)
        {
            Add(72 + (beat % 4), (7 * Bar) + (beat * 480L), 470, 70);
        }

        PlaybackTempo[] tempos = [new(Tick.Zero, 500_000), new(new Tick(7 * Bar), 700_000), new(new Tick(9 * Bar), 2_000_000), new(new Tick((9 * Bar) + 480), 700_000)];
        TickSpan[] pedal = [TickSpan.FromBounds(Tick.Zero, new Tick(960)), TickSpan.FromBounds(new Tick(3 * Bar), new Tick(4 * Bar)), TickSpan.FromBounds(new Tick((6 * Bar) + 960), new Tick((7 * Bar) + 480))];
        return ([.. notes], tempos, pedal);
    }

    [Theory]
    [InlineData("playing-written")]
    [InlineData("playing-as-played")]
    [InlineData("playing-reading-marks")]
    public void A_playing_is_written_with_its_marks_as_before(string golden)
    {
        (Note[] notes, PlaybackTempo[] tempos, TickSpan[] pedal) = Playing();
        Tick end = new(10 * 1920);

        GeneratedNotation notation = golden switch
        {
            "playing-written" => NotationGenerator.Generate(notes, MeterMap.Default, [], end, golden),
            "playing-as-played" => NotationGenerator.Generate(notes, MeterMap.Default, [], end, golden, playedTempos: tempos, pedal: pedal, tempos: tempos),
            _ => NotationGenerator.Generate(notes, MeterMap.Default, [], end, golden, asHeard: true, playedTempos: tempos, pedal: pedal, marks: PlayedMarks.Significant, tempos: tempos),
        };

        AssertIs(golden, notation);
    }

    /// <summary>That <paramref name="notation"/> is the golden (<see cref="TextGoldens.AssertTextGolden"/>).</summary>
    private static void AssertIs(string name, GeneratedNotation notation) => TextGoldens.AssertTextGolden("Notation", name + ".txt", Describe(notation));

    /// <summary>The counts, staves and ossias of a notation, one to a line, then its MEI as written, a bar to a line.</summary>
    private static string Describe(GeneratedNotation notation)
    {
        var text = new StringBuilder();
        void Line(string name, object? value) => text.Append(name).Append(": ").Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('\n');
        Line("notes", notation.NoteCount);
        Line("approximated", notation.ApproximatedNoteCount);
        Line("shortened", notation.ShortenedNoteCount);
        Line("lengthened", notation.LengthenedNoteCount);
        Line("cut", notation.CutNoteCount);
        Line("graces", notation.GraceNoteCount);
        Line("as heard", notation.WrittenAsHeard);
        Line("accents", notation.AccentCount);
        Line("dynamics", notation.DynamicCount);
        Line("tempo words", notation.TempoWordCount);
        Line("hairpins", notation.HairpinCount);
        Line("fermatas", notation.FermataCount);
        Line("tempo marks", notation.TempoMarkCount);
        Line("triplets", notation.TripletCount);
        Line("slurs", notation.SlurCount);
        Line("pedals", notation.PedalCount);
        Line("excerpt", notation.Excerpt);
        foreach (NotationStaff staff in notation.Staves)
        {
            Line("staff", staff);
        }

        foreach (GeneratedOssia ossia in notation.Ossias)
        {
            Line("ossia", $"{ossia.Label} {ossia.IdPrefix} {ossia.NoteCount} notes, staves {string.Join(", ", ossia.Staves)}");
        }

        // A line a bar, so that a changed golden shows which bars changed.
        Line("mei", notation.Mei.Replace("<measure ", "\n<measure ", StringComparison.Ordinal));
        return text.ToString();
    }
}