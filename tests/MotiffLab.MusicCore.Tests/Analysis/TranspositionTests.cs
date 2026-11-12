using System.Xml.Linq;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// The piece in another key (version 0.5, #351): the notes moved by the interval between the tonics, nothing else about
/// them changed, the key signatures moved by as many fifths, and the moved notes written as the original ones were.
/// </summary>
public class TranspositionTests
{
    private static readonly XNamespace s_mei = "http://www.music-encoding.org/ns/mei";

    private static readonly MusicalKey s_aMinor = new(0, KeyMode.Minor);

    private static MusicXmlImport FurElise() =>
        MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", "fur-elise-original.musicxml")));

    private static KeyChange[] Keys(MusicXmlImport piece) => [.. piece.KeySignatures.Where(key => key.Track == 0).Select(key => new KeyChange(key.At, Math.Clamp(key.Fifths, -7, 7)))];

    /// <summary>The first five notes of the melody as written: letter, accidental shown or implied, octave.</summary>
    private static string[] Opening(TransposedPiece moved, MusicXmlImport piece)
    {
        GeneratedNotation notation = NotationGenerator.Generate(moved.Notes, piece.Meter, moved.Keys, piece.End, spelling: moved.Spelling);
        Dictionary<string, XElement> written = XDocument.Parse(notation.Mei).Descendants(s_mei + "note")
            .ToDictionary(note => (string)note.Attribute(XNamespace.Xml + "id")!);
        return [.. moved.Notes.OrderBy(note => note.Span.Onset).ThenByDescending(note => note.Pitch.MidiNumber).DistinctBy(note => note.Span.Onset).Take(5)
            .Select(note => written[note.Id])
            .Select(note => $"{(string?)note.Attribute("pname")}{(string?)note.Attribute("accid") ?? (string?)note.Attribute("accid.ges")}{(string?)note.Attribute("oct")}")];
    }

    [Theory]
    [InlineData(-3, 3)]
    [InlineData(2, 2)]
    [InlineData(3, -3)]
    [InlineData(-6, -6)]
    public void A_minor_moves_the_nearer_way(int fifths, int semitones)
    {
        Assert.Equal(semitones, Transposition.Semitones(s_aMinor, new MusicalKey(fifths, KeyMode.Minor)));
    }

    [Fact]
    public void Fur_elise_in_c_minor_moves_up_a_minor_third_and_only_its_pitches_change()
    {
        MusicXmlImport piece = FurElise();

        TransposedPiece moved = Transposition.Of(piece.Notes, Keys(piece), s_aMinor, new MusicalKey(-3, KeyMode.Minor));

        Assert.Equal(3, moved.Semitones);
        Assert.Equal(piece.Notes.Count, moved.Notes.Count);
        Assert.All(piece.Notes.Zip(moved.Notes), pair =>
        {
            Assert.Equal(pair.First.Id, pair.Second.Id);
            Assert.Equal(pair.First.Span, pair.Second.Span);
            Assert.Equal(pair.First.Hand, pair.Second.Hand);
            Assert.Equal(pair.First.Attribution, pair.Second.Attribution);
            Assert.Equal(pair.First.Pitch.MidiNumber + 3, pair.Second.Pitch.MidiNumber);
        });
        Assert.Equal([-3], moved.Keys.Select(key => key.Fifths));
    }

    [Fact]
    public void The_moved_notes_keep_their_spelling_e_d_sharp_becomes_g_f_sharp_in_c_minor_and_f_sharp_e_sharp_in_b_minor()
    {
        MusicXmlImport piece = FurElise();

        Assert.Equal(["e5", "ds5", "e5", "ds5", "e5"], Opening(Transposition.Of(piece.Notes, Keys(piece), s_aMinor, s_aMinor), piece));
        Assert.Equal(["g5", "fs5", "g5", "fs5", "g5"], Opening(Transposition.Of(piece.Notes, Keys(piece), s_aMinor, new MusicalKey(-3, KeyMode.Minor)), piece));
        Assert.Equal(["fs5", "es5", "fs5", "es5", "fs5"], Opening(Transposition.Of(piece.Notes, Keys(piece), s_aMinor, new MusicalKey(2, KeyMode.Minor)), piece));
    }

    [Fact]
    public void The_key_signature_follows_the_new_key()
    {
        MusicXmlImport piece = FurElise();
        TransposedPiece moved = Transposition.Of(piece.Notes, Keys(piece), s_aMinor, new MusicalKey(2, KeyMode.Minor));

        XDocument mei = XDocument.Parse(NotationGenerator.Generate(moved.Notes, piece.Meter, moved.Keys, piece.End, spelling: moved.Spelling).Mei);

        Assert.Equal("2s", (string?)mei.Descendants(s_mei + "scoreDef").First().Attribute("keysig"));
    }

    [Fact]
    public void A_section_beyond_seven_sharps_is_written_in_its_enharmonic_key_one_letter_apart()
    {
        // E major with a section in C♯ major, moved to F♯ major: the section would need nine sharps, so it is E♭ major.
        Note sharp = Note(61, 1920);
        TransposedPiece moved = Transposition.Of([Note(64, 0), sharp], [new KeyChange(Tick.Zero, 4), new KeyChange(new Tick(1920), 7)], new MusicalKey(4, KeyMode.Major), new MusicalKey(6, KeyMode.Major));

        Assert.Equal([6, -3], moved.Keys.Select(key => key.Fifths));
        SpelledPitch section = moved.Spelling.Spell(moved.Notes[1], -3);
        Assert.Equal(new SpelledPitch(2, -1, 4), section);
        Assert.Equal(new SpelledPitch(3, 1, 4), moved.Spelling.Spell(moved.Notes[0], 6));
    }

    [Fact]
    public void A_change_of_mode_is_not_a_transposition()
    {
        Assert.Throws<ArgumentException>(() => Transposition.Of([], [], s_aMinor, new MusicalKey(0, KeyMode.Major)));
    }

    [Fact]
    public void The_same_piece_gives_the_same_result()
    {
        MusicXmlImport piece = FurElise();
        MusicalKey cMinor = new(-3, KeyMode.Minor);

        Assert.Equal(Opening(Transposition.Of(piece.Notes, Keys(piece), s_aMinor, cMinor), piece), Opening(Transposition.Of(piece.Notes, Keys(piece), s_aMinor, cMinor), piece));
    }

    private static Note Note(int midi, long onset) => new()
    {
        Id = $"n_{onset}",
        Pitch = new Pitch(midi),
        Span = new TickSpan(new Tick(onset), 480),
        Velocity = 64,
        Track = 0,
        Attribution = Attribution.Imported,
    };
}