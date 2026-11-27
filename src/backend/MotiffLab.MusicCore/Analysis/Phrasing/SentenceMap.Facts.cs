using MotiffLab.MusicCore.Events;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

/// <summary>The facts about sentences and phrases for Understand (S1-A4), in the structure lens, and why the phrases were grouped as they are, in the map's language.</summary>
public sealed partial class SentenceMap
{
    /// <summary>
    /// The facts about a sentence for Understand (S1-A4), in the structure lens: which phrases it holds,
    /// how each ends, where one starts as the return of another, and, as an interpretation, that the
    /// second answers the first.
    /// </summary>
    public IReadOnlyList<MotifFact> Facts(Sentence sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        var facts = new List<MotifFact>
        {
            new(
                "sentence.phrases",
                FactLens.Structure,
                Certainty(sentence.Found),
                _words.T(
                    $"Sentence {sentence.Letter} holds {sentence.PhrasesIn(_words)}, {_words.Bars(sentence.FirstBar, sentence.LastBar)}.",
                    $"Предложение {sentence.Letter} состоит из {PhrasesGenitive(sentence)}, {_words.Bars(sentence.FirstBar, sentence.LastBar)}."),
                sentence.Basis)
            {
                Places = [.. sentence.Phrases.Select(PlaceOf)],
            },
        };
        facts.AddRange(sentence.Phrases.Select(CloseFact));
        facts.AddRange(sentence.Phrases.Skip(1).Select(RepeatFact).OfType<MotifFact>());
        if (AnswerFact(sentence) is { } answer)
        {
            facts.Add(answer);
        }

        return facts;
    }

    /// <summary>The facts about a phrase for Understand: the sentence it belongs to, how it ends, and the reading that it answers the phrase before.</summary>
    public IReadOnlyList<MotifFact> Facts(Phrase phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        Sentence sentence = Of(phrase);
        var facts = new List<MotifFact>
        {
            new(
                "sentence.of",
                FactLens.Structure,
                Certainty(sentence.Found),
                _words.T(
                    $"Phrase {phrase.Number} belongs to sentence {sentence.Letter} ({sentence.PhrasesIn(_words)}, {_words.Bars(sentence.FirstBar, sentence.LastBar)}).",
                    $"Фраза {phrase.Number} входит в предложение {sentence.Letter} ({sentence.PhrasesIn(_words)}, {_words.Bars(sentence.FirstBar, sentence.LastBar)})."),
                sentence.Basis)
            {
                Places = [.. sentence.Phrases.Select(PlaceOf)],
            },
            CloseFact(phrase),
        };
        if (RepeatFact(phrase) is { } repeat)
        {
            facts.Add(repeat);
        }

        if (AnswerFact(sentence) is { } answer && sentence.Phrases[1] == phrase)
        {
            facts.Add(answer);
        }

        return facts;
    }

    /// <summary>A sentence's basis: the grouping's reasons after each of its phrases, in order; "it" for a sentence of <paramref name="onePhrase"/>.</summary>
    private static string GroupBasis(IEnumerable<GroupReason> reasons, bool onePhrase, Words words) =>
        Words.Capitalize(string.Join("; ", reasons.Select(reason => reason switch
        {
            { Rule: GroupRule.PieceEnds } when onePhrase => words.T("it ends with the piece", "она заканчивается вместе с произведением"),
            { Rule: GroupRule.PieceEnds } => words.T($"phrase {reason.Phrase} ends with the piece", $"фраза {reason.Phrase} заканчивается вместе с произведением"),
            { Rule: GroupRule.PlayerEnded } => words.T($"you ended it after phrase {reason.Phrase}", $"вы завершили его после фразы {reason.Phrase}"),
            { Rule: GroupRule.PlayerJoined } => words.T($"you joined phrase {reason.Next} to it", $"вы присоединили к нему фразу {reason.Next}"),
            { Rule: GroupRule.RepeatSign } => words.T($"a repeat sign stands after phrase {reason.Phrase}", $"после фразы {reason.Phrase} стоит знак репризы"),
            { Rule: GroupRule.Consequent } => words.T(
                $"phrase {reason.Next} starts where phrase {reason.Phrase} comes back note for note, so it reads as the consequent",
                $"фраза {reason.Next} начинается там, где фраза {reason.Phrase} возвращается нота в ноту, поэтому читается как ответное построение"),
            { Rule: GroupRule.FullClose } => words.T($"phrase {reason.Phrase} ends on the tonic, a full close", $"фраза {reason.Phrase} заканчивается на тонике, полная каденция"),
            { Rule: GroupRule.HalfClose } => words.T($"phrase {reason.Phrase} ends on a half close, so it runs on", $"фраза {reason.Phrase} заканчивается половинной каденцией, поэтому продолжается"),
            _ => words.T($"phrase {reason.Phrase} reads no close, so it runs on", $"во фразе {reason.Phrase} каденция не читается, поэтому она продолжается"),
        }))) + ".";

    /// <summary>"phrases 1–2" in the genitive, as "состоит из" takes it: "фраз 1–2", "фразы 1".</summary>
    private static string PhrasesGenitive(Sentence sentence) =>
        sentence.Phrases.Count == 1 ? $"фразы {sentence.Phrases[0].Number}" : $"фраз {sentence.Phrases[0].Number}–{sentence.Phrases[^1].Number}";

    private MotifFact CloseFact(Phrase phrase)
    {
        PhraseClose close = CloseOf(phrase);
        string text = close switch
        {
            { Place: null } => _words.T(
                $"Phrase {phrase.Number} has no notes of its own, so no close is read.",
                $"Во фразе {phrase.Number} нет своих нот, поэтому каденция не читается."),
            { Kind: CloseKind.Full } => _words.T(
                $"Phrase {phrase.Number} ends on {close.TopName}, the tonic: a full close.",
                $"Фраза {phrase.Number} заканчивается на {close.TopName}, тонике: полная каденция."),
            { Kind: CloseKind.Half, Degree: { } half } => _words.T(
                $"Phrase {phrase.Number} ends on {close.TopName}, {DegreeName(half, _words)}: a half close.",
                $"Фраза {phrase.Number} заканчивается на {close.TopName}, {DegreeName(half, _words)}: половинная каденция."),
            // A note outside the scale over the dominant in the bass still reads as a half close.
            { Kind: CloseKind.Half } => _words.T(
                $"Phrase {phrase.Number} ends on {close.TopName}, outside the scale, over the dominant in the bass: a half close.",
                $"Фраза {phrase.Number} заканчивается на {close.TopName}, вне звукоряда, над доминантой в басу: половинная каденция."),
            { Degree: { } degree } => _words.T(
                $"Phrase {phrase.Number} ends on {close.TopName}, {DegreeName(degree, _words)}: no close is read.",
                $"Фраза {phrase.Number} заканчивается на {close.TopName}, {DegreeName(degree, _words)}: каденция не читается."),
            _ => _words.T(
                $"Phrase {phrase.Number} ends on {close.TopName}, outside the scale: no close is read.",
                $"Фраза {phrase.Number} заканчивается на {close.TopName}, вне звукоряда: каденция не читается."),
        };
        string basis = _words.T(
            $"The last notes to start in the phrase, read as degrees of {Key.Key.Name} ({KeyBasis()}). The tonic reads as a full close; the dominant, the second degree and the leading note as a half close. The harmony under them is not analysed.",
            $"Последние ноты, начинающиеся во фразе, прочитаны как ступени тональности {Key.Key.NameIn(_words)} ({KeyBasis()}). Тоника читается как полная каденция; доминанта, вторая ступень и вводный тон — как половинная. Гармония под ними не анализируется.");
        return new MotifFact($"sentence.close.{phrase.Number}", FactLens.Structure, Certainty(close.Attribution), text, basis)
        {
            Places = close.Place is { } place ? [place] : [],
        };
    }

    private MotifFact? RepeatFact(Phrase phrase)
    {
        if (phrase.Start is not { } start || !start.Evidence.Contains(PhraseEvidence.Repetition) || phrase.Number == 1)
        {
            return null;
        }

        Sentence sentence = Of(phrase);
        if (sentence.Phrases[0] == phrase)
        {
            return null;
        }

        return new MotifFact(
            $"sentence.repeat.{phrase.Number}",
            FactLens.Structure,
            Certainty(start.Attribution),
            _words.T(
                $"Phrase {phrase.Number} starts where phrase {phrase.Number - 1} comes back note for note.",
                $"Фраза {phrase.Number} начинается там, где фраза {phrase.Number - 1} возвращается нота в ноту."),
            _words.T(
                $"The top line from the start of phrase {phrase.Number - 1} comes back with the same pitches and timing at the start of phrase {phrase.Number} ({PhraseMap.Describe(start, _words)}).",
                $"Верхняя линия начала фразы {phrase.Number - 1} возвращается с теми же высотами и ритмом в начале фразы {phrase.Number} ({PhraseMap.Describe(start, _words)})."))
        {
            Places = [PlaceOf(phrase)],
        };
    }

    /// <summary>"Phrase 2 answers phrase 1": a reading of a two-phrase sentence whose second phrase returns or closes more firmly; never a measurement.</summary>
    private MotifFact? AnswerFact(Sentence sentence)
    {
        if (sentence.Phrases.Count != 2)
        {
            return null;
        }

        Phrase first = sentence.Phrases[0];
        Phrase second = sentence.Phrases[1];
        bool returns = second.Start?.Evidence.Contains(PhraseEvidence.Repetition) == true;
        PhraseClose firstClose = CloseOf(first);
        PhraseClose secondClose = CloseOf(second);
        bool closesFirmer = firstClose.Kind == CloseKind.Half && secondClose.Kind == CloseKind.Full;
        if (!returns && !closesFirmer)
        {
            return null;
        }

        string why = (returns, closesFirmer) switch
        {
            (true, true) => _words.T(
                $"phrase {second.Number} starts like phrase {first.Number} and ends more firmly (a half close, then a full close)",
                $"фраза {second.Number} начинается как фраза {first.Number} и заканчивается твёрже (половинная каденция, затем полная)"),
            (true, false) => _words.T(
                $"phrase {second.Number} starts like phrase {first.Number} and ends differently",
                $"фраза {second.Number} начинается как фраза {first.Number} и заканчивается иначе"),
            _ => _words.T(
                $"phrase {first.Number} ends on a half close and phrase {second.Number} on a full close",
                $"фраза {first.Number} заканчивается половинной каденцией, а фраза {second.Number} — полной"),
        };
        return new MotifFact(
            $"sentence.answer.{sentence.Number}",
            FactLens.Structure,
            FactCertainty.Interpretation,
            _words.T($"Phrase {second.Number} answers phrase {first.Number}.", $"Фраза {second.Number} отвечает фразе {first.Number}."),
            _words.T(
                $"A reading, not a measurement: {why}. The notes do not decide it; hear whether the second phrase feels like a reply.",
                $"Интерпретация, а не измерение: {why}. Ноты этого не решают; послушайте, звучит ли вторая фраза как ответ."))
        {
            Places = [PlaceOf(first), PlaceOf(second)],
        };
    }

    private string KeyBasis() => Key.Certainty switch
    {
        FactCertainty.Detected => _words.T("stated by the file", "указана в файле"),
        FactCertainty.Uncertain => _words.T(
            $"a weak reading of the pitches; {Key.RunnerUp?.Name} fits almost as well",
            $"слабое чтение по высотам; {Key.RunnerUp?.NameIn(_words)} подходит почти так же"),
        _ => _words.T("read from the pitches", "прочитана по высотам"),
    };

    private FactPlace PlaceOf(Phrase phrase) => new($"{_words.T("phrase", "фраза")} {phrase.Number}", phrase.Span, []);

    private static FactCertainty Certainty(Attribution attribution) => attribution.Provenance switch
    {
        Provenance.Imported or Provenance.Detected or Provenance.UserConfirmed or Provenance.UserEdited => FactCertainty.Detected,
        _ => attribution.Confidence < CloseConfidence ? FactCertainty.Uncertain : FactCertainty.Inferred,
    };

    /// <summary>The degree as the sentence names it after "ends on E5, …": "the dominant"; "доминанте" (the dative, after "на").</summary>
    private static string DegreeName(int degree, Words words) => degree switch
    {
        1 => words.T("the tonic", "тонике"),
        2 => words.T("the second degree", "второй ступени"),
        3 => words.T("the third degree", "третьей ступени"),
        4 => words.T("the fourth degree", "четвёртой ступени"),
        5 => words.T("the dominant", "доминанте"),
        6 => words.T("the sixth degree", "шестой ступени"),
        _ => words.T("the leading note", "вводном тоне"),
    };
}