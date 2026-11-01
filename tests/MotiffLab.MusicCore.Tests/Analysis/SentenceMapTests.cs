using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>Sentences over phrases (#133, decision record 0013): grouped by returns, closes and repeat signs, with their claim kinds.</summary>
public class SentenceMapTests
{
    private const long Quarter = 480;
    private const long Bar = 4 * Quarter;

    private static readonly KeySignature s_cMajor = new(Tick.Zero, 0, KeyMode.Major);

    private static Note Note(string id, int pitch, long onset, long duration) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Attribution = Attribution.Imported,
    };

    /// <summary>A phrase of two bars: quarter notes on <paramref name="pitches"/>, ending a beat early so a rest marks the next phrase.</summary>
    private static IEnumerable<Note> PhraseOf(int number, long start, params int[] pitches)
    {
        for (int i = 0; i < pitches.Length; i++)
        {
            yield return Note($"p{number}n{i}", pitches[i], start + (i * Quarter), i == pitches.Length - 1 ? Quarter : Quarter);
        }
    }

    /// <summary>Two phrases in C major: the first ends on G (the dominant), the second on C (the tonic).</summary>
    private static (Note[] Notes, PhraseMap Phrases) QuestionAndAnswer()
    {
        Note[] notes =
        [
            .. PhraseOf(1, 0, 60, 64, 67, 69, 67, 65, 67),           // seven quarters: bar 1 and three beats of bar 2, then a beat of rest
            .. PhraseOf(2, 2 * Bar, 60, 64, 67, 69, 67, 62, 60),     // bar 3 and three beats of bar 4, ending on C
        ];
        PhraseMap phrases = PhraseMap.Suggest(notes, MeterMap.Default, PhraseMarks.None, new Tick(4 * Bar));
        return (notes, phrases);
    }

    [Fact]
    public void A_half_close_then_a_full_close_make_one_sentence_with_an_answer_interpretation()
    {
        (Note[] notes, PhraseMap phrases) = QuestionAndAnswer();
        Assert.Equal(2, phrases.Phrases.Count);

        SentenceMap map = SentenceMap.Suggest(phrases, notes, s_cMajor);

        Sentence only = Assert.Single(map.Sentences);
        Assert.Equal("A", only.Letter);
        Assert.Equal([1, 2], only.Phrases.Select(phrase => phrase.Number));
        Assert.Equal(TickSpan.FromBounds(Tick.Zero, new Tick(4 * Bar)), only.Span);
        Assert.Equal((1, 4), (only.FirstBar, only.LastBar));
        Assert.Equal(Provenance.Inferred, only.Found.Provenance);
        Assert.Equal(SentenceMap.GroupConfidence, only.Found.Confidence);
        Assert.Equal("Phrase 1 ends on a half close, so it runs on; phrase 2 ends with the piece.", only.Basis);

        Assert.Equal([CloseKind.Half, CloseKind.Full], map.Closes.Select(close => close.Kind));
        Assert.Equal(("G4", 5), (map.Closes[0].TopName, map.Closes[0].Degree));
        Assert.Equal(["p1n6"], map.Closes[0].Place!.NoteIds);

        IReadOnlyList<MotifFact> facts = map.Facts(only);
        Assert.Equal(["sentence.phrases", "sentence.close.1", "sentence.close.2", "sentence.answer.1"], facts.Select(fact => fact.Id));
        Assert.All(facts, fact => Assert.Equal(FactLens.Structure, fact.Lens));
        Assert.Equal("Sentence A holds phrases 1–2, bars 1–4.", facts[0].Text);
        Assert.Equal(FactCertainty.Inferred, facts[0].Certainty);
        Assert.Equal("Phrase 1 ends on G4, the dominant: a half close.", facts[1].Text);
        Assert.Equal("Phrase 2 ends on C4, the tonic: a full close.", facts[2].Text);
        Assert.Equal(FactCertainty.Inferred, facts[2].Certainty);
        Assert.Contains("stated by the file", facts[2].Basis);
        Assert.Equal(FactCertainty.Interpretation, facts[3].Certainty);
        Assert.Equal("Phrase 2 answers phrase 1.", facts[3].Text);
        Assert.StartsWith("A reading, not a measurement:", facts[3].Basis);
        Assert.Equal(["phrase 1", "phrase 2"], facts[3].Places.Select(place => place.Label));
    }

    [Fact]
    public void The_player_ends_a_sentence_at_a_phrase_boundary_or_joins_across_one()
    {
        (Note[] notes, PhraseMap phrases) = QuestionAndAnswer();
        SentenceMap map = SentenceMap.Suggest(phrases, notes, s_cMajor);
        Tick boundary = phrases.Phrases[1].Span.Onset;

        SentenceMap split = map.With(map.SplitAt(boundary));
        Assert.Equal(2, split.Sentences.Count);
        Assert.Equal([boundary], split.Edits.Added);
        Assert.Equal("You ended it after phrase 1.", split.Sentences[0].Basis);
        Assert.Equal(Attribution.UserEdited, split.Sentences[0].Found);
        Assert.Equal(FactCertainty.Detected, split.Facts(split.Sentences[0])[0].Certainty);

        SentenceMap joined = split.With(split.JoinWithNext(split.Sentences[0]));
        Assert.Single(joined.Sentences);
        Assert.Equal([boundary], joined.Edits.Removed);
        Assert.Empty(joined.Edits.Added);
        Assert.Equal("You joined phrase 2 to it; phrase 2 ends with the piece.", joined.Sentences[0].Basis);

        // An edit at a tick that is no phrase boundary changes nothing, and reset restores the suggestion.
        Assert.Equal(map.Edits, map.SplitAt(new Tick(Quarter)));
        Assert.Equal(map.Sentences.Select(sentence => sentence.Basis), joined.With(PhraseEdits.None).Sentences.Select(sentence => sentence.Basis));
        Assert.Equal(2, SentenceMap.Suggest(phrases, notes, s_cMajor, new PhraseEdits([boundary], []), Words.Russian).Sentences.Count);
    }

    [Fact]
    public void The_facts_about_a_phrase_name_its_sentence_and_its_close()
    {
        (Note[] notes, PhraseMap phrases) = QuestionAndAnswer();
        SentenceMap map = SentenceMap.Suggest(phrases, notes, s_cMajor);

        IReadOnlyList<MotifFact> first = map.Facts(phrases.Phrases[0]);
        IReadOnlyList<MotifFact> second = map.Facts(phrases.Phrases[1]);

        Assert.Equal(["sentence.of", "sentence.close.1"], first.Select(fact => fact.Id));
        Assert.Equal("Phrase 1 belongs to sentence A (phrases 1–2, bars 1–4).", first[0].Text);
        Assert.Equal(["sentence.of", "sentence.close.2", "sentence.answer.1"], second.Select(fact => fact.Id));
        Assert.Same(map.Sentences[0], map.Of(phrases.Phrases[1]));
        Assert.Same(map.Sentences[0], map.Matching(map.Sentences[0].Span));
        Assert.Same(map.Sentences[0], map.Containing(phrases.Phrases[1].Span));
        Assert.Null(map.Matching(phrases.Phrases[1].Span));
    }

    [Fact]
    public void A_full_close_ends_a_sentence_and_the_next_phrase_starts_another()
    {
        Note[] notes =
        [
            .. PhraseOf(1, 0, 60, 64, 67, 69, 67, 62, 60),            // ends on C: full close
            .. PhraseOf(2, 2 * Bar, 64, 65, 67, 69, 71, 72, 71),      // ends on B, the leading note: half close, then the piece ends
        ];
        PhraseMap phrases = PhraseMap.Suggest(notes, MeterMap.Default, PhraseMarks.None, new Tick(4 * Bar));

        SentenceMap map = SentenceMap.Suggest(phrases, notes, s_cMajor);

        Assert.Equal(2, map.Sentences.Count);
        Assert.Equal(["A", "B"], map.Sentences.Select(sentence => sentence.Letter));
        Assert.Equal("Phrase 1 ends on the tonic, a full close.", map.Sentences[0].Basis);
        Assert.Equal("It ends with the piece.", map.Sentences[1].Basis);
        Assert.Null(map.Facts(map.Sentences[0]).FirstOrDefault(fact => fact.Certainty == FactCertainty.Interpretation));
    }

    [Fact]
    public void A_repeat_sign_ends_a_sentence_as_a_detected_boundary()
    {
        MusicXmlImport score = MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "phrases-repeats.musicxml")));
        PhraseMap phrases = PhraseMap.Suggest(score.Notes, score.Meter, score.PhraseMarks, new Tick(5 * Bar));
        Assert.Equal(3, phrases.Phrases.Count);

        SentenceMap map = SentenceMap.Suggest(phrases, score.Notes, null);

        Sentence last = map.Sentences[^1];
        Assert.Same(phrases.Phrases[2], last.Phrases[0]);
        Assert.Equal(Provenance.Detected, last.Found.Provenance);
        Sentence before = map.Of(phrases.Phrases[1]);
        Assert.Equal(phrases.Phrases[1].Span.End, before.Span.End);
        Assert.Contains("repeat sign stands after phrase 2", before.Basis);
        Assert.All(map.Sentences, sentence => Assert.NotEmpty(map.Facts(sentence)));
    }

    [Fact]
    public void An_uncertain_key_makes_the_closes_uncertain()
    {
        (Note[] notes, PhraseMap phrases) = QuestionAndAnswer();
        Note[] thin = [.. notes.Take(3), .. notes.Skip(7).Take(3)];
        PhraseMap few = PhraseMap.Suggest(thin, MeterMap.Default, PhraseMarks.None, new Tick(4 * Bar));

        SentenceMap map = SentenceMap.Suggest(few, thin, null);

        Assert.Equal(FactCertainty.Uncertain, map.Key.Certainty);
        Assert.All(map.Closes, close => Assert.Equal(SentenceMap.CloseConfidence / 2, close.Attribution.Confidence));
        Assert.All(map.Sentences.SelectMany(map.Facts).Where(fact => fact.Id.StartsWith("sentence.close", StringComparison.Ordinal)), fact => Assert.Equal(FactCertainty.Uncertain, fact.Certainty));
    }
}