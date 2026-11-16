using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// The Reference's key cards (the Reference experiment, Andrei, 2026-11-11): key signatures, scales spelled by letter, the
/// relative, parallel and enharmonic keys, and the three main chords, in English and Russian; and the facade's calls for them.
/// </summary>
public class KeyReferenceTests
{
    private static string Spelled(MusicalKey key, ScaleForm form) => string.Join(" ", KeyReference.ScaleOf(key, form).Select(note => note.Letter));

    [Theory]
    [InlineData(0, "C D E F G A B")]
    [InlineData(6, "F♯ G♯ A♯ B C♯ D♯ E♯")]
    [InlineData(7, "C♯ D♯ E♯ F♯ G♯ A♯ B♯")]
    [InlineData(-6, "G♭ A♭ B♭ C♭ D♭ E♭ F")]
    [InlineData(-7, "C♭ D♭ E♭ F♭ G♭ A♭ B♭")]
    [InlineData(-3, "E♭ F G A♭ B♭ C D")]
    public void A_major_scale_takes_each_letter_once(int fifths, string notes) =>
        Assert.Equal(notes, Spelled(new MusicalKey(fifths, KeyMode.Major), ScaleForm.Major));

    [Theory]
    [InlineData(0, ScaleForm.NaturalMinor, "A B C D E F G")]
    [InlineData(0, ScaleForm.HarmonicMinor, "A B C D E F G♯")]
    [InlineData(6, ScaleForm.HarmonicMinor, "D♯ E♯ F♯ G♯ A♯ B C𝄪")]
    [InlineData(7, ScaleForm.HarmonicMinor, "A♯ B♯ C♯ D♯ E♯ F♯ G𝄪")]
    [InlineData(-5, ScaleForm.HarmonicMinor, "B♭ C D♭ E♭ F G♭ A")]
    [InlineData(-7, ScaleForm.NaturalMinor, "A♭ B♭ C♭ D♭ E♭ F♭ G♭")]
    public void A_minor_scale_raises_its_seventh_in_the_harmonic_form(int fifths, ScaleForm form, string notes) =>
        Assert.Equal(notes, Spelled(new MusicalKey(fifths, KeyMode.Minor), form));

    [Theory]
    [InlineData(5, "F♯ C♯ G♯ D♯ A♯", "8 5 9 6 3")]
    [InlineData(-4, "B♭ E♭ A♭ D♭", "4 7 3 6")]
    [InlineData(0, "", "")]
    public void A_signature_lists_its_accidentals_in_order_with_their_place_on_the_treble_staff(int fifths, string accidentals, string steps)
    {
        SignatureCard signature = KeyReference.Signature(fifths);

        Assert.Equal(accidentals, string.Join(" ", signature.Accidentals));
        Assert.Equal(steps, string.Join(" ", signature.TrebleSteps));
    }

    [Fact]
    public void The_lookup_offers_none_then_seven_sharps_then_seven_flats()
    {
        IReadOnlyList<SignatureChoice> choices = KeyReference.Signatures(Words.Russian);

        Assert.Equal(15, choices.Count);
        Assert.Equal("0 1♯ 2♯ 3♯ 4♯ 5♯ 6♯ 7♯ 1♭ 2♭ 3♭ 4♭ 5♭ 6♭ 7♭", string.Join(" ", choices.Select(choice => choice.Label)));
        Assert.Equal("5 диезов", choices.Single(choice => choice.Fifths == 5).Spoken);
        Assert.Equal("2 бемоля", choices.Single(choice => choice.Fifths == -2).Spoken);
        Assert.Equal("без знаков", choices[0].Spoken);
    }

    [Fact]
    public void Five_sharps_are_B_major_and_G_sharp_minor_with_C_flat_major_beside()
    {
        KeyCard card = KeyReference.For(new MusicalKey(5, KeyMode.Major));

        Assert.Equal("B major", card.Name);
        Assert.Equal("G♯ minor", card.RelativeName);
        Assert.Equal("B minor", card.ParallelName);
        Assert.Equal("C♭ major", card.EnharmonicName);
        Assert.Equal(5, card.Position);
        Assert.Equal("5 sharps", card.Signature.Count);
        Assert.Equal("B C♯ D♯ E F♯ G♯ A♯", string.Join(" ", card.Scales.Single().Notes));
    }

    [Fact]
    public void The_main_chords_of_a_major_key_are_its_tonic_subdominant_and_dominant()
    {
        KeyCard card = KeyReference.For(new MusicalKey(6, KeyMode.Major));

        Assert.Equal(["I", "IV", "V"], card.Chords.Select(chord => chord.Numeral));
        Assert.Equal(["F♯", "B", "C♯"], card.Chords.Select(chord => chord.Symbol));
        Assert.Equal(["F♯ A♯ C♯", "B D♯ F♯", "C♯ E♯ G♯"], card.Chords.Select(chord => string.Join(" ", chord.Notes)));
        Assert.Equal(["tonic", "subdominant", "dominant"], card.Chords.Select(chord => chord.FunctionName));
        Assert.Equal([66, 70, 73], card.Chords[0].Sound.Pitches);
        Assert.True(card.Chords[0].Sound.Together);
    }

    [Fact]
    public void The_dominant_of_a_minor_key_is_major_from_the_harmonic_minor()
    {
        KeyCard card = KeyReference.For(new MusicalKey(6, KeyMode.Minor));

        Assert.Equal(["i", "iv", "V"], card.Chords.Select(chord => chord.Numeral));
        Assert.Equal(["D♯m", "G♯m", "A♯"], card.Chords.Select(chord => chord.Symbol));
        Assert.Equal("A♯ C𝄪 E♯", string.Join(" ", card.Chords[2].Notes));
        Assert.Equal([ScaleForm.NaturalMinor, ScaleForm.HarmonicMinor], card.Scales.Select(scale => scale.Form));
    }

    [Fact]
    public void A_parallel_beyond_seven_signs_is_written_as_its_enharmonic_key()
    {
        Assert.Equal("B♭ major", KeyReference.For(new MusicalKey(7, KeyMode.Minor)).ParallelName);
        Assert.Equal("B minor", KeyReference.For(new MusicalKey(-7, KeyMode.Major)).ParallelName);
        Assert.Null(KeyReference.For(new MusicalKey(4, KeyMode.Major)).Enharmonic);
    }

    [Fact]
    public void The_card_is_written_in_Russian_with_syllables()
    {
        KeyCard card = KeyReference.For(new MusicalKey(-3, KeyMode.Minor), NoteNaming.Syllables, Words.Russian);

        Assert.Equal("до минор", card.Name);
        Assert.Equal("ми-бемоль мажор", card.RelativeName);
        Assert.Equal("до мажор", card.ParallelName);
        Assert.Equal("3 бемоля", card.Signature.Count);
        Assert.Equal(["си♭", "ми♭", "ля♭"], card.Signature.Accidentals);
        Assert.Equal("до ре ми♭ фа соль ля♭ си", string.Join(" ", card.Scales[1].Notes));
        Assert.Equal(["натуральный минор", "гармонический минор"], card.Scales.Select(scale => scale.Title));
        Assert.Equal(["тоника", "субдоминанта", "доминанта"], card.Chords.Select(chord => chord.FunctionName));
        Assert.Equal("Cm", card.Chords[0].Symbol);
    }

    [Fact]
    public void A_scale_is_heard_a_beat_a_note_from_the_tonic_to_its_octave_on_a_keyboard_from_C_to_B()
    {
        ReferenceSound sound = KeyReference.For(new MusicalKey(1, KeyMode.Major)).Scales[0].Sound;
        IReadOnlyList<Note> notes = KeyReference.Notes(sound, new Tick(960));

        Assert.Equal("scale.major", sound.Id);
        Assert.Equal([55, 57, 59, 60, 62, 64, 66, 67], sound.Pitches);
        Assert.Equal(48, sound.KeyboardLow);
        Assert.Equal(71, sound.KeyboardHigh);
        Assert.Equal(960, notes[0].Span.Onset.Value);
        Assert.Equal(960 + (7 * 480), notes[^1].Span.Onset.Value);
        Assert.Equal(960, notes[^1].Span.Duration);
        Assert.All(notes, note => Assert.Equal(Provenance.Inferred, note.Attribution.Provenance));
    }

    [Fact]
    public void The_same_key_gives_the_same_card()
    {
        KeyCard first = KeyReference.For(new MusicalKey(-2, KeyMode.Minor), NoteNaming.Letters, Words.English);
        KeyCard second = KeyReference.For(new MusicalKey(-2, KeyMode.Minor), NoteNaming.Letters, Words.English);

        Assert.Equal(first.Chords.Select(chord => string.Join(" ", chord.Notes)), second.Chords.Select(chord => string.Join(" ", chord.Notes)));
        Assert.Equal(first.Scales.Select(scale => string.Join(" ", scale.Notes)), second.Scales.Select(scale => string.Join(" ", scale.Notes)));
    }

    [Fact]
    public void The_facade_answers_the_circle_and_a_card_on_piece_0()
    {
        var facade = new MusicCoreFacade();
        using JsonDocument empty = JsonDocument.Parse(facade.Call(0, "reference.key", """{"language": "en"}"""));
        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "reference.key", """{"fifths": -1, "mode": "minor", "language": "ru", "naming": "syllables"}"""));

        Assert.Equal(15, empty.RootElement.GetProperty("signatures").GetArrayLength());
        Assert.Equal(12, empty.RootElement.GetProperty("sectors").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, empty.RootElement.GetProperty("card").ValueKind);
        JsonElement card = answer.RootElement.GetProperty("card");
        Assert.Equal("ре минор", card.GetProperty("name").GetString());
        Assert.Equal("фа мажор", card.GetProperty("relative").GetProperty("name").GetString());
        Assert.Equal("1 бемоль", card.GetProperty("signature").GetProperty("count").GetString());
        Assert.Equal("chord.V", card.GetProperty("chords")[2].GetProperty("id").GetString());
        Assert.Equal(11, answer.RootElement.GetProperty("circle").GetProperty("position").GetInt32());
    }

    [Fact]
    public void The_facade_sounds_a_chord_of_the_card_as_a_playback_score()
    {
        var facade = new MusicCoreFacade();
        using JsonDocument answer = JsonDocument.Parse(facade.Call(0, "reference.sound", """{"fifths": 0, "mode": "major", "id": "chord.V"}"""));
        using JsonDocument wrong = JsonDocument.Parse(facade.Call(0, "reference.sound", """{"fifths": 0, "id": "chord.ii"}"""));

        Assert.Equal([55, 59, 62], answer.RootElement.GetProperty("notes").EnumerateArray().Select(note => note.GetProperty("pitch").GetInt32()));
        Assert.Equal(KeyReference.MicrosecondsPerQuarter, answer.RootElement.GetProperty("tempos")[0].GetProperty("microsecondsPerQuarter").GetInt32());
        Assert.Equal("badRequest", wrong.RootElement.GetProperty("error").GetString());
    }
}