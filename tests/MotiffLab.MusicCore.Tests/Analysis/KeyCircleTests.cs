using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// The circle of fifths for a selection (Andrei, 2026-11-06): the twelve places with their signatures, the key read for
/// the selection and its neighbours, the chords on the sectors of their roots, the notes outside the scale, the scale to
/// hear and the lines, in English and Russian, all from the key reading and the chord finder.
/// </summary>
public class KeyCircleTests
{
    private static ImportedPiece Piece(string name) =>
        PieceImporter.Import(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name))).Piece!;

    private static KeyCircleView FurEliseOpening(Words words)
    {
        ImportedPiece piece = Piece("fur-elise-original.musicxml");
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, 0, 4, piece.End), SelectionFilter.All);
        KeySignature? signature = KeySignature.InForce(piece.Keys, selection.Span.Onset);
        IReadOnlyList<PhraseBoundary> phrases = PhraseBoundaryFinder.Find(piece.Notes, piece.Meter, PhraseMarks.None);
        MotifAnalysis motif = MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, signature, accents: [], phrases, words);
        HarmonyAnalysis harmony = HarmonyAnalyzer.Analyze(piece.Notes, piece.Meter, selection, motif.Key?.Key, words);
        return KeyCircle.For(selection, piece.Notes, piece.Meter, motif.Key, harmony, words)!;
    }

    [Fact]
    public void The_circle_has_twelve_places_by_fifths_with_the_relative_minor_inside()
    {
        IReadOnlyList<CircleSector> sectors = KeyCircle.Sectors;

        Assert.Equal(12, sectors.Count);
        Assert.Equal(["C", "G", "D", "A", "E", "B", "F♯", "D♭", "A♭", "E♭", "B♭", "F"], sectors.Select(sector => sector.Major));
        Assert.Equal(["Am", "Em", "Bm", "F♯m", "C♯m", "G♯m", "D♯m", "B♭m", "Fm", "Cm", "Gm", "Dm"], sectors.Select(sector => sector.Minor));
        Assert.Equal(["0", "1♯", "2♯", "3♯", "4♯", "5♯", "6♯", "5♭", "4♭", "3♭", "2♭", "1♭"], sectors.Select(sector => sector.Signature));
        Assert.Equal(KeyCircle.PositionOf(new MusicalKey(-3, KeyMode.Major)), KeyCircle.PositionOf(new MusicalKey(-3, KeyMode.Minor)));
    }

    [Fact]
    public void Für_Elise_opens_in_A_minor_beside_E_minor_and_D_minor()
    {
        KeyCircleView circle = FurEliseOpening(Words.English);

        Assert.Equal(new MusicalKey(0, KeyMode.Minor), circle.Key);
        Assert.Equal(0, circle.Position);
        CircleNeighbour Of(KeyRelation relation) => circle.Neighbours.Single(neighbour => neighbour.Relation == relation);
        Assert.Equal("E minor", Of(KeyRelation.Dominant).Name);
        Assert.Equal(1, Of(KeyRelation.Dominant).Position);
        Assert.Equal("D minor", Of(KeyRelation.Subdominant).Name);
        Assert.Equal(11, Of(KeyRelation.Subdominant).Position);
        Assert.Equal("C major", Of(KeyRelation.Relative).Name);
        Assert.Equal("A major", Of(KeyRelation.Parallel).Name);
        Assert.Equal(3, Of(KeyRelation.Parallel).Position);
        Assert.Equal([57, 59, 60, 62, 64, 65, 67, 69], circle.ScalePitches);
    }

    [Fact]
    public void The_chords_sit_on_their_roots_with_their_degrees_and_the_notes_outside_the_scale_are_one_place()
    {
        KeyCircleView circle = FurEliseOpening(Words.English);

        Assert.NotEmpty(circle.Chords);
        Assert.Equal(Enumerable.Range(1, circle.Chords.Count), circle.Chords.Select(chord => chord.Number));
        CircleChord am = circle.Chords.First(chord => chord.Symbol.StartsWith("Am", StringComparison.Ordinal));
        Assert.Equal((0, KeyMode.Minor, "i"), (am.Position, am.Ring, am.Degree));
        CircleChord e = circle.Chords.First(chord => chord.Symbol.StartsWith('E'));
        Assert.Equal((4, KeyMode.Major), (e.Position, e.Ring));
        Assert.StartsWith("V", e.Degree, StringComparison.Ordinal);
        Assert.All(circle.Chords, chord => Assert.NotEmpty(chord.Place.NoteIds));

        // D♯ and G♯ are outside A minor's natural scale.
        Assert.NotNull(circle.Outside);
        Assert.Matches(@"^\d+ notes? outside the scale$", circle.Outside!.Label);
        Assert.NotEmpty(circle.Outside.NoteIds);
    }

    [Fact]
    public void The_lines_say_the_signature_the_neighbours_and_the_chords_in_both_languages()
    {
        KeyCircleView english = FurEliseOpening(Words.English);
        KeyCircleView russian = FurEliseOpening(Words.Russian);

        Assert.Equal("A minor: no sharps or flats, the same as its relative key, C major.", english.Lines[0]);
        Assert.Contains("E minor (dominant) and D minor (subdominant)", english.Lines[1], StringComparison.Ordinal);
        Assert.StartsWith("The chords of the passage: Am (i)", english.Lines[2], StringComparison.Ordinal);
        Assert.Equal("Ля минор: без диезов и бемолей, как у параллельной тональности, до мажор.", russian.Lines[0]);
        Assert.Contains("ми минор (доминанта) и ре минор (субдоминанта)", russian.Lines[1], StringComparison.Ordinal);
        Assert.StartsWith("Аккорды отрывка:", russian.Lines[2], StringComparison.Ordinal);
        Assert.Equal("параллельная", russian.Neighbours.Single(neighbour => neighbour.Relation == KeyRelation.Relative).Label);
        Assert.Equal("одноимённая", russian.Neighbours.Single(neighbour => neighbour.Relation == KeyRelation.Parallel).Label);
        Assert.Matches(@"вне звукоряда$", russian.Outside!.Label);
    }

    [Fact]
    public void The_scale_to_hear_is_one_beat_a_note_and_the_octave_held()
    {
        KeyCircleView circle = FurEliseOpening(Words.English);

        IReadOnlyList<MotiffLab.MusicCore.Events.Note> notes = circle.ScaleNotes(new MotiffLab.MusicCore.Time.Tick(960));

        Assert.Equal(circle.ScalePitches, notes.Select(note => note.Pitch.MidiNumber));
        Assert.Equal(960, notes[0].Span.Onset.Value);
        Assert.Equal(480, notes[0].Span.Duration);
        Assert.Equal(960, notes[^1].Span.Duration);
        Assert.All(notes, note => Assert.StartsWith("circle_", note.Id, StringComparison.Ordinal));

        // Near the end of the piece the scale starts early enough to end with it.
        IReadOnlyList<MotiffLab.MusicCore.Events.Note> late = circle.ScaleNotes(new MotiffLab.MusicCore.Time.Tick(4000), new MotiffLab.MusicCore.Time.Tick(4800));
        Assert.Equal(4800, late[^1].Span.End.Value);
    }

    [Fact]
    public void A_selection_without_a_key_reading_has_no_circle()
    {
        ImportedPiece piece = Piece("fur-elise-original.musicxml");
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, 0, 4, piece.End), SelectionFilter.All);

        Assert.Null(KeyCircle.For(selection, piece.Notes, piece.Meter, null, HarmonyAnalysis.None));
    }
}