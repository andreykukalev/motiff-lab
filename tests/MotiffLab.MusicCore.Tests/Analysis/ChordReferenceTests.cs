using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// The Reference's chord cards (part 2; Andrei, 2026-11-11): any of the seventeen roots with any chord, spelled by letter,
/// its formula, root position and each inversion to hear struck or broken; in English and Russian; and the facade's calls.
/// </summary>
public class ChordReferenceTests
{
    private static SpelledName Root(int step, int alter = 0) => new(step, alter);

    private static string Letters(ChordCard card) => string.Join(" ", ChordReference.Spell(card.Root, card.Type).Select(note => note.Letter));

    [Theory]
    [InlineData(0, 0, ChordType.Major, "C", "C E G", "1 3 5")]
    [InlineData(3, 1, ChordType.Minor, "F♯m", "F♯ A C♯", "1 ♭3 5")]
    [InlineData(6, 0, ChordType.Diminished, "Bdim", "B D F", "1 ♭3 ♭5")]
    [InlineData(4, 1, ChordType.Augmented, "G♯+", "G♯ B♯ D𝄪", "1 3 ♯5")]
    [InlineData(1, 0, ChordType.Sus2, "Dsus2", "D E A", "1 2 5")]
    [InlineData(1, 0, ChordType.Sus4, "Dsus4", "D G A", "1 4 5")]
    [InlineData(4, 0, ChordType.Dominant7, "G7", "G B D F", "1 3 5 ♭7")]
    [InlineData(2, -1, ChordType.Major7, "E♭maj7", "E♭ G B♭ D", "1 3 5 7")]
    [InlineData(5, 0, ChordType.Minor7, "Am7", "A C E G", "1 ♭3 5 ♭7")]
    [InlineData(6, 0, ChordType.HalfDiminished7, "Bm7♭5", "B D F A", "1 ♭3 ♭5 ♭7")]
    [InlineData(4, 1, ChordType.Diminished7, "G♯dim7", "G♯ B D F", "1 ♭3 ♭5 𝄫7")]
    [InlineData(3, 0, ChordType.Major6, "F6", "F A C D", "1 3 5 6")]
    [InlineData(1, 0, ChordType.Minor6, "Dm6", "D F A B", "1 ♭3 5 6")]
    [InlineData(0, 0, ChordType.Add9, "Cadd9", "C E G D", "1 3 5 9")]
    public void A_chord_is_spelled_on_its_letters_with_its_symbol_and_formula(int step, int alter, ChordType type, string symbol, string notes, string formula)
    {
        ChordCard card = ChordReference.For(Root(step, alter), type);

        Assert.Equal(symbol, card.Symbol);
        Assert.Equal(notes, string.Join(" ", card.Notes));
        Assert.Equal(formula, string.Join(" ", card.Formula));
    }

    [Fact]
    public void Every_root_and_chord_stays_within_a_double_sharp_or_flat()
    {
        foreach (SpelledName root in KeyReference.Roots)
        {
            foreach (ChordType type in Enum.GetValues<ChordType>())
            {
                Assert.All(ChordReference.Spell(root, type), note => Assert.InRange(note.Alter, -2, 2));
            }
        }
    }

    [Fact]
    public void The_seventeen_roots_spell_each_black_key_both_ways()
    {
        Assert.Equal(
            "C C♯ D♭ D D♯ E♭ E F F♯ G♭ G G♯ A♭ A A♯ B♭ B",
            string.Join(" ", KeyReference.Roots.Select(root => root.Letter)));
    }

    [Fact]
    public void A_triad_has_root_position_and_two_inversions_from_around_middle_C()
    {
        ChordCard card = ChordReference.For(Root(0), ChordType.Major);

        Assert.Equal([0, 1, 2], card.Positions.Select(position => position.Inversion));
        Assert.Equal([60, 64, 67], card.Positions[0].Block.Pitches);
        Assert.Equal([64, 67, 72], card.Positions[1].Block.Pitches);
        Assert.Equal([67, 72, 76], card.Positions[2].Block.Pitches);
        Assert.Equal(["E", "G", "C"], card.Positions[1].Notes);
        Assert.Equal(["root position", "first inversion", "second inversion"], card.Positions.Select(position => position.Title));
        Assert.Equal([null, "6", "6/4"], card.Positions.Select(position => position.Figure));
    }

    [Fact]
    public void Every_position_of_a_chord_sits_on_the_same_stretch_of_keyboard()
    {
        ChordCard card = ChordReference.For(Root(0), ChordType.Dominant7);

        Assert.Equal([70, 72, 76, 79], card.Positions[3].Block.Pitches);
        Assert.All(card.Positions, position =>
        {
            Assert.Equal(60, position.Block.KeyboardLow);
            Assert.Equal(83, position.Block.KeyboardHigh);
            Assert.Equal(position.Block.KeyboardLow, position.Arpeggio.KeyboardLow);
        });
        Assert.Equal([null, "6/5", "4/3", "2"], card.Positions.Select(position => position.Figure));
    }

    [Fact]
    public void An_added_ninth_stays_on_top_of_the_turned_triad()
    {
        ChordCard card = ChordReference.For(Root(0), ChordType.Add9);

        Assert.Equal(3, card.Positions.Count);
        Assert.Equal([60, 64, 67, 74], card.Positions[0].Block.Pitches);
        Assert.Equal([64, 67, 72, 74], card.Positions[1].Block.Pitches);
        Assert.Equal([67, 72, 76, 86], card.Positions[2].Block.Pitches);
        Assert.Equal(["G", "C", "E", "D"], card.Positions[2].Notes);
    }

    [Fact]
    public void A_chord_from_G_to_B_starts_below_middle_C()
    {
        ChordCard card = ChordReference.For(Root(5, -1), ChordType.Minor);

        Assert.Equal("A♭m", card.Symbol);
        Assert.Equal([56, 59, 63], card.Positions[0].Block.Pitches);
        Assert.Equal("A♭ C♭ E♭", Letters(card));
    }

    [Fact]
    public void A_block_chord_is_struck_at_once_and_a_broken_one_rings_a_quaver_a_note()
    {
        ChordPosition position = ChordReference.For(Root(0), ChordType.Major).Positions[0];
        var block = KeyReference.Notes(position.Block, new Tick(480));
        var broken = KeyReference.Notes(position.Arpeggio, new Tick(480));

        Assert.Equal("block.0", position.Block.Id);
        Assert.Equal("arpeggio.0", position.Arpeggio.Id);
        Assert.All(block, note => Assert.Equal(new TickSpan(new Tick(480), 960), note.Span));
        Assert.Equal([480, 720, 960], broken.Select(note => note.Span.Onset.Value));
        Assert.All(broken, note => Assert.Equal(480 + 240 + 240 + 960, note.Span.End.Value));
        Assert.Equal(240 + 240 + 960, position.Arpeggio.Length);
        Assert.All(broken, note => Assert.Equal(MusicCore.Events.Provenance.Inferred, note.Attribution.Provenance));
    }

    [Fact]
    public void The_card_is_written_in_Russian_with_syllables_and_the_classical_names_of_inversions()
    {
        ChordCard seventh = ChordReference.For(Root(4), ChordType.Dominant7, NoteNaming.Syllables, Words.Russian);
        ChordCard triad = ChordReference.For(Root(1, -1), ChordType.Minor, NoteNaming.Syllables, Words.Russian);
        ChordCard sus = ChordReference.For(Root(1), ChordType.Sus4, NoteNaming.Syllables, Words.Russian);

        Assert.Equal("G7", seventh.Symbol);
        Assert.Equal("малый мажорный септаккорд", seventh.Name);
        Assert.Equal(["соль", "си", "ре", "фа"], seventh.Notes);
        Assert.Equal(["основной вид", "квинтсекстаккорд", "терцквартаккорд", "секундаккорд"], seventh.Positions.Select(position => position.Title));
        Assert.Equal(["основной вид", "секстаккорд", "квартсекстаккорд"], triad.Positions.Select(position => position.Title));
        Assert.Equal(["ре♭", "фа♭", "ля♭"], triad.Notes);
        Assert.Equal("второе обращение", sus.Positions[2].Title);
        Assert.Null(sus.Positions[2].Figure);
    }

    [Fact]
    public void A_key_cards_main_chords_name_their_root_and_type_for_their_own_card()
    {
        KeyCard card = KeyReference.For(new MusicalKey(-3, KeyMode.Minor));

        Assert.Equal([ChordType.Minor, ChordType.Minor, ChordType.Major], card.Chords.Select(chord => chord.Type));
        Assert.Equal(["C", "F", "G"], card.Chords.Select(chord => chord.Root.Letter));
        Assert.Equal("G B D", string.Join(" ", ChordReference.For(card.Chords[2].Root, card.Chords[2].Type).Notes));
    }

    [Fact]
    public void The_same_chord_gives_the_same_card()
    {
        ChordCard first = ChordReference.For(Root(2, -1), ChordType.HalfDiminished7, NoteNaming.Letters, Words.English);
        ChordCard second = ChordReference.For(Root(2, -1), ChordType.HalfDiminished7, NoteNaming.Letters, Words.English);

        Assert.Equal(first.Positions.Select(position => string.Join(" ", position.Block.Pitches)), second.Positions.Select(position => string.Join(" ", position.Block.Pitches)));
        Assert.Equal(first.Notes, second.Notes);
    }

    [Fact]
    public void The_facade_answers_the_roots_the_chords_and_a_card_on_piece_0()
    {
        var facade = new MusicCoreFacade();
        using JsonDocument empty = JsonDocument.Parse(facade.Call(0, "reference.chord", """{"language": "en"}"""));
        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "reference.chord", """{"step": 3, "alter": 1, "type": "minor7", "language": "ru", "naming": "syllables"}"""));
        using JsonDocument wrong = JsonDocument.Parse(facade.Call(0, "reference.chord", """{"step": 6, "alter": 1, "type": "major"}"""));

        Assert.Equal(17, empty.RootElement.GetProperty("roots").GetArrayLength());
        Assert.Equal(14, empty.RootElement.GetProperty("types").GetArrayLength());
        Assert.Equal("m7♭5", empty.RootElement.GetProperty("types")[9].GetProperty("suffix").GetString());
        Assert.Equal(JsonValueKind.Null, empty.RootElement.GetProperty("card").ValueKind);
        JsonElement card = answer.RootElement.GetProperty("card");
        Assert.Equal("F♯m7", card.GetProperty("symbol").GetString());
        Assert.Equal("фа♯", card.GetProperty("root").GetProperty("name").GetString());
        Assert.Equal("1 ♭3 5 ♭7", string.Join(" ", card.GetProperty("formula").EnumerateArray().Select(degree => degree.GetString())));
        Assert.Equal(4, card.GetProperty("positions").GetArrayLength());
        Assert.Equal("arpeggio.2", card.GetProperty("positions")[2].GetProperty("arpeggio").GetProperty("id").GetString());
        Assert.Equal("badRequest", wrong.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void The_facade_sounds_a_position_of_a_chord_card()
    {
        var facade = new MusicCoreFacade();
        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "reference.sound", """{"step": 0, "alter": 0, "type": "major", "id": "block.1"}"""));
        using JsonDocument wrong = JsonDocument.Parse(facade.Call(0, "reference.sound", """{"step": 0, "alter": 0, "type": "major", "id": "block.3"}"""));

        Assert.Equal([64, 67, 72], answer.RootElement.GetProperty("notes").EnumerateArray().Select(note => note.GetProperty("pitch").GetInt32()));
        Assert.Equal("badRequest", wrong.RootElement.GetProperty("error").GetString());
    }
}