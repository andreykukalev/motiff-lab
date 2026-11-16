using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Notation;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// The Reference's scale cards (part 2): the major, the three minors, the church modes and the pentatonics from any of the
/// seventeen roots, spelled by letter with their degrees, heard up and down; the piece's key on the circle; the facade's calls.
/// </summary>
public class ScaleReferenceTests
{
    [Theory]
    [InlineData(0, 0, ScaleForm.Major, "C D E F G A B", "1 2 3 4 5 6 7")]
    [InlineData(5, 0, ScaleForm.MelodicMinor, "A B C D E F♯ G♯", "1 2 ♭3 4 5 6 7")]
    [InlineData(1, 0, ScaleForm.Dorian, "D E F G A B C", "1 2 ♭3 4 5 6 ♭7")]
    [InlineData(2, 0, ScaleForm.Phrygian, "E F G A B C D", "1 ♭2 ♭3 4 5 ♭6 ♭7")]
    [InlineData(3, 0, ScaleForm.Lydian, "F G A B C D E", "1 2 3 ♯4 5 6 7")]
    [InlineData(4, 0, ScaleForm.Mixolydian, "G A B C D E F", "1 2 3 4 5 6 ♭7")]
    [InlineData(6, 0, ScaleForm.Locrian, "B C D E F G A", "1 ♭2 ♭3 4 ♭5 ♭6 ♭7")]
    [InlineData(1, 1, ScaleForm.Lydian, "D♯ E♯ F𝄪 G𝄪 A♯ B♯ C𝄪", "1 2 3 ♯4 5 6 7")]
    [InlineData(4, -1, ScaleForm.Locrian, "G♭ A𝄫 B𝄫 C♭ D𝄫 E𝄫 F♭", "1 ♭2 ♭3 4 ♭5 ♭6 ♭7")]
    [InlineData(0, 0, ScaleForm.MajorPentatonic, "C D E G A", "1 2 3 5 6")]
    [InlineData(5, 0, ScaleForm.MinorPentatonic, "A C D E G", "1 ♭3 4 5 ♭7")]
    public void A_scale_takes_its_letters_from_the_tonic_with_its_degrees(int step, int alter, ScaleForm form, string notes, string formula)
    {
        ScaleCard card = ScaleReference.For(new SpelledName(step, alter), form);

        Assert.Equal(notes, string.Join(" ", card.Notes));
        Assert.Equal(formula, string.Join(" ", card.Formula));
    }

    [Fact]
    public void Every_root_and_scale_stays_within_a_double_sharp_or_flat()
    {
        foreach (SpelledName root in KeyReference.Roots)
        {
            foreach (ScaleForm form in Enum.GetValues<ScaleForm>())
            {
                Assert.All(ScaleReference.Spell(root, form), note => Assert.InRange(note.Alter, -2, 2));
            }
        }
    }

    [Fact]
    public void A_scale_is_heard_up_and_down_a_quaver_a_note_and_the_keyboard_marks_the_way_up()
    {
        ScaleCard card = ScaleReference.For(new SpelledName(0, 0), ScaleForm.Major);
        IReadOnlyList<Note> notes = KeyReference.Notes(card.Sound);

        Assert.Equal([60, 62, 64, 65, 67, 69, 71, 72, 71, 69, 67, 65, 64, 62, 60], card.Sound.Pitches);
        Assert.Equal([60, 62, 64, 65, 67, 69, 71, 72], card.Sound.Keys);
        Assert.Equal(240, notes[1].Span.Onset.Value);
        Assert.Equal(240, notes[1].Span.Duration);
        Assert.Equal(960, notes[^1].Span.Duration);
        Assert.Equal((14 * 240) + 960, card.Sound.Length);
        Assert.Empty(card.Descending);
    }

    [Fact]
    public void The_melodic_minor_comes_down_natural()
    {
        ScaleCard card = ScaleReference.For(new SpelledName(5, 0), ScaleForm.MelodicMinor);

        Assert.Equal("A G F E D C B A", string.Join(" ", card.Descending));
        Assert.Equal([57, 59, 60, 62, 64, 66, 68, 69, 67, 65, 64, 62, 60, 59, 57], card.Sound.Pitches);
        Assert.True(card.Sound.Sounds(66));
        Assert.False(card.Sound.Sounds(65));
    }

    [Fact]
    public void The_card_is_written_in_Russian_with_syllables()
    {
        ScaleCard card = ScaleReference.For(new SpelledName(1, 0), ScaleForm.Dorian, NoteNaming.Syllables, Words.Russian);

        Assert.Equal("ре дорийский", card.Name);
        Assert.Equal("ре ми фа соль ля си до", string.Join(" ", card.Notes));
        Assert.Equal(
            "мажор, натуральный минор, гармонический минор, мелодический минор, дорийский, фригийский, лидийский, миксолидийский, локрийский, мажорная пентатоника, минорная пентатоника",
            string.Join(", ", ScaleReference.Forms(Words.Russian).Select(choice => choice.Name)));
    }

    [Fact]
    public void A_key_cards_scales_name_their_tonic_and_form_for_their_own_card()
    {
        KeyCard card = KeyReference.For(new MusicalKey(3, KeyMode.Minor));

        Assert.All(card.Scales, scale => Assert.Equal("F♯", scale.Tonic.Letter));
        Assert.Equal("F♯ G♯ A B C♯ D♯ E♯", string.Join(" ", ScaleReference.For(card.Scales[0].Tonic, ScaleForm.MelodicMinor).Notes));
    }

    [Fact]
    public void The_facade_answers_a_scale_card_and_sounds_it()
    {
        var facade = new MusicCoreFacade();
        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "reference.scale", """{"step": 4, "alter": 0, "form": "mixolydian", "language": "en"}"""));
        using JsonDocument sound = JsonDocument.Parse(facade.Call(0, "reference.sound", """{"step": 4, "alter": 0, "form": "mixolydian", "id": "scale"}"""));
        using JsonDocument wrong = JsonDocument.Parse(facade.Call(0, "reference.scale", """{"form": "blues"}"""));

        Assert.Equal(11, answer.RootElement.GetProperty("forms").GetArrayLength());
        JsonElement card = answer.RootElement.GetProperty("card");
        Assert.Equal("G Mixolydian", card.GetProperty("name").GetString());
        Assert.Equal(8, card.GetProperty("sound").GetProperty("keys").GetArrayLength());
        Assert.Equal(15, sound.RootElement.GetProperty("notes").GetArrayLength());
        Assert.Equal("badRequest", wrong.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void The_facade_answers_the_key_an_open_piece_starts_in()
    {
        var facade = new MusicCoreFacade();
        int piece = Open(facade);
        using JsonDocument open = JsonDocument.Parse(facade.Call(piece, "reference.key", """{"language": "en"}"""));
        using JsonDocument none = JsonDocument.Parse(facade.Call(0, "reference.key", """{"language": "en"}"""));

        JsonElement key = open.RootElement.GetProperty("pieceKey");
        Assert.Equal(0, key.GetProperty("fifths").GetInt32());
        Assert.Equal("minor", key.GetProperty("mode").GetString());
        Assert.Equal("A minor", key.GetProperty("name").GetString());
        Assert.False(key.GetProperty("inferred").GetBoolean());
        Assert.Equal(12, open.RootElement.GetProperty("sectors").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, none.RootElement.GetProperty("pieceKey").ValueKind);
    }

    /// <summary>Für Elise, whose file writes A minor, open as a piece.</summary>
    private static int Open(MusicCoreFacade facade)
    {
        byte[] file = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", "fur-elise-original.musicxml"));
        using JsonDocument opened = JsonDocument.Parse(facade.Open("fur-elise-original.musicxml", file));
        return opened.RootElement.GetProperty("piece").GetInt32();
    }
}