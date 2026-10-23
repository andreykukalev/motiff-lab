using System.Globalization;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import;

public class HandInferenceTests
{
    private const long Bar = 4 * 480;

    private static string FixturePath(string folder, string name) => Path.Combine(AppContext.BaseDirectory, "Import", folder, "Fixtures", name);

    private static MidiImport ImportMidi(string name) => MidiImporter.Import(File.ReadAllBytes(FixturePath("Midi", name)));

    private static Note Note(string id, int pitch, long onset, long duration, int track = 0, Attributed<Hand>? hand = null) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = track,
        Hand = hand,
        Attribution = Attribution.Imported,
    };

    /// <summary>The intended hand of each note of piano-single-track.mid, by MIDI tick (PPQ 480, so canonical) and pitch.</summary>
    private static Dictionary<(long Tick, int Pitch), Hand> HandKey() => File.ReadLines(FixturePath("Midi", "piano-single-track.hands.txt"))
        .Where(line => !line.StartsWith('#'))
        .Select(line => line.Split(' '))
        .ToDictionary(
            parts => (long.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture)),
            parts => parts[2] == "L" ? Hand.Left : Hand.Right);

    [Fact]
    public void Single_track_piano_gets_inferred_hands_that_follow_the_hand_key()
    {
        MidiImport result = ImportMidi("piano-single-track.mid");
        Dictionary<(long, int), Hand> key = HandKey();

        Assert.All(result.Notes, note =>
        {
            Assert.NotNull(note.Hand);
            Assert.Equal(Provenance.Inferred, note.Hand.Attribution.Provenance);
            Assert.InRange(note.Hand.Attribution.Confidence, HandInference.MinConfidence, HandInference.MaxConfidence);
            Assert.Equal(Attribution.Imported, note.Attribution); // an inferred hand does not make the note uncertain
        });

        Note[] wrong = [.. result.Notes.Where(note => note.Hand!.Value != key[(note.Span.Onset.Value, note.Pitch.MidiNumber)])];

        // 53 of 55 notes right. The two misses are the left hand's beat-4 notes in bars 2 and 4,
        // which jump up next to the right hand's held note.
        Assert.Equal([(1, 69), (3, 71)], wrong.Select(note => ((int)(note.Span.Onset.Value / Bar), note.Pitch.MidiNumber)));

        // Bars 5 and 6, where the hands share the E4 region and a fixed split point fails, are all right.
        Assert.All(
            result.Notes.Where(note => note.Span.Onset.Value is >= 4 * Bar and < 6 * Bar),
            note => Assert.Equal(key[(note.Span.Onset.Value, note.Pitch.MidiNumber)], note.Hand!.Value));

        // A fixed split at middle C gets 12 wrong.
        Assert.Equal(12, result.Notes.Count(note => (note.Pitch.MidiNumber < 60 ? Hand.Left : Hand.Right) != key[(note.Span.Onset.Value, note.Pitch.MidiNumber)]));
    }

    [Fact]
    public void A_close_call_has_lower_confidence_than_a_clear_one()
    {
        MidiImport result = ImportMidi("piano-single-track.mid");

        Note closeCall = Assert.Single(result.Notes, note => note.Span.Onset.Value == Bar + (3 * 480) && note.Pitch.MidiNumber == 69);
        Note bass = Assert.Single(result.Notes, note => note.Span.Onset == Tick.Zero && note.Pitch.MidiNumber == 36);

        Assert.True(closeCall.Hand!.Attribution.Confidence < 0.6, closeCall.Hand.Attribution.ToString());
        Assert.Equal(HandInference.MaxConfidence, bass.Hand!.Attribution.Confidence);
    }

    [Fact]
    public void Multi_track_files_single_lines_and_grand_staves_keep_their_hands()
    {
        // Two tracks: no hands are invented.
        Assert.All(ImportMidi("two-hands-type1.mid").Notes, note => Assert.Null(note.Hand));
        Assert.All(ImportMidi("performance-click.mid").Notes, note => Assert.Null(note.Hand));

        // One track, but a single line: nothing shows two hands.
        MidiImport scale = ImportMidi("scale-type0.mid");
        Assert.All(scale.Notes, note => Assert.Null(note.Hand));
        Assert.Equal(0, scale.Report.CountOf(MidiIssueCodes.HandsInferred));

        // A grand staff keeps its imported hands.
        MusicXmlImport grandStaff = MusicXmlImporter.Import(File.ReadAllBytes(FixturePath("MusicXml", "grand-staff.musicxml")));
        Assert.False(HandInference.Applies([.. grandStaff.Notes]));
        Assert.Same(grandStaff.Notes, HandInference.Infer(grandStaff.Notes));
        Assert.All(grandStaff.Notes, note => Assert.Equal(Attribution.Imported, note.Hand!.Attribution));
    }

    [Fact]
    public void Notes_on_two_tracks_or_with_a_hand_are_not_inferred()
    {
        Note[] twoTracks = [Note("a", 48, 0, 480), Note("b", 72, 0, 480, track: 1)];
        Note[] oneHanded = [Note("a", 48, 0, 480, hand: new Attributed<Hand>(Hand.Left, Attribution.UserEdited)), Note("b", 72, 0, 480)];

        Assert.Same(twoTracks, HandInference.Infer(twoTracks));
        Assert.Same(oneHanded, HandInference.Infer(oneHanded));
    }

    [Fact]
    public void Inference_keeps_note_order_and_is_deterministic()
    {
        Note[] notes =
        [
            Note("n3", 76, 480, 480),
            Note("n1", 72, 0, 960),
            Note("n2", 36, 0, 1920),
            Note("n4", 55, 480, 480),
        ];

        IReadOnlyList<Note> first = HandInference.Infer(notes);
        IReadOnlyList<Note> again = HandInference.Infer([.. notes.Reverse()]);

        Assert.Equal(["n3", "n1", "n2", "n4"], first.Select(note => note.Id));
        Assert.Equal([Hand.Right, Hand.Right, Hand.Left, Hand.Left], first.Select(note => note.Hand!.Value));
        Assert.Equal(
            first.OrderBy(note => note.Id).Select(note => note.Hand),
            again.OrderBy(note => note.Id).Select(note => note.Hand));
    }

    [Fact]
    public void A_rolled_chord_counts_as_one_group()
    {
        // A slightly spread chord: bass and treble within a 64th note of each other.
        Note[] notes = [Note("a", 40, 0, 960), Note("b", 76, 20, 940), Note("c", 79, 25, 935)];

        Assert.Equal([Hand.Left, Hand.Right, Hand.Right], HandInference.Infer(notes).Select(note => note.Hand!.Value));
    }

    [Fact]
    public void The_player_can_reassign_a_note_and_the_change_is_user_edited()
    {
        IReadOnlyList<Note> inferred = ImportMidi("piano-single-track.mid").Notes;
        Note target = Assert.Single(inferred, note => note.Span.Onset.Value == Bar + (3 * 480) && note.Pitch.MidiNumber == 69);

        IReadOnlyList<Note> edited = HandInference.Reassign(inferred, target.Id, Hand.Left);

        Note changed = Assert.Single(edited, note => note.Id == target.Id);
        Assert.Equal(new Attributed<Hand>(Hand.Left, Attribution.UserEdited), changed.Hand);
        Assert.Equal(inferred.Count, edited.Count);
        Assert.All(edited.Where(note => note.Id != target.Id), note => Assert.Equal(Provenance.Inferred, note.Hand!.Attribution.Provenance));
        Assert.Equal(Hand.Right, target.Hand!.Value); // the original list is unchanged

        // An edited hand is no longer up for inference.
        Assert.False(HandInference.Applies([.. edited]));
        Assert.Throws<ArgumentException>(() => HandInference.Reassign(inferred, "missing", Hand.Left));
    }
}