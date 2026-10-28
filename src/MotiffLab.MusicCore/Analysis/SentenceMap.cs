using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>How a phrase ends, read from the last notes to start in it (decision record 0013, #133).</summary>
public enum CloseKind
{
    /// <summary>The phrase ends on the tonic: a full close.</summary>
    Full,

    /// <summary>The phrase ends on the dominant, the second degree or the leading note: a half close, so it asks to go on.</summary>
    Half,

    /// <summary>The phrase ends on another note, or has no notes, so no close is read.</summary>
    None,
}

/// <summary>How a phrase ends: its last notes read as scale degrees of the key.</summary>
/// <param name="Phrase">The phrase.</param>
/// <param name="Kind">The close read from the degrees.</param>
/// <param name="TopName">The highest of the last notes, spelt in the key; null when the phrase has no notes.</param>
/// <param name="Degree">The scale degree of that note, 1 to 7; null when it is outside the scale or there is none.</param>
/// <param name="Attribution">Inferred, with <see cref="SentenceMap.CloseConfidence"/>, or less when the key itself is uncertain.</param>
/// <param name="Place">The last notes to start in the phrase; null when it has none.</param>
public sealed record PhraseClose(Phrase Phrase, CloseKind Kind, string? TopName, int? Degree, Attribution Attribution, FactPlace? Place);

/// <summary>
/// One sentence of the piece: a run of phrases that close together (#133). A suggestion, never a
/// fact: <see cref="Found"/> says how sure the grouping is and <see cref="Basis"/> why.
/// </summary>
/// <param name="Number">1 for the first sentence of the piece, then in order; <see cref="Letter"/> names it.</param>
/// <param name="Span">From the first phrase's start to the last phrase's end.</param>
/// <param name="Phrases">The phrases it holds, in order; at least one.</param>
/// <param name="Found">Detected when both edges are marked in the file or are the piece's; else inferred, with the weakest confidence of the readings it rests on.</param>
/// <param name="Basis">Why the phrases make one sentence, in a sentence.</param>
public sealed record Sentence(int Number, TickSpan Span, int FirstBar, int LastBar, IReadOnlyList<Phrase> Phrases, Attribution Found, string Basis)
{
    /// <summary>"A" for the first sentence, "B" for the second; numbers after Z.</summary>
    public string Letter => Number <= 26 ? ((char)('A' + Number - 1)).ToString() : Number.ToString();

    /// <summary>The number of bars the sentence touches, counting partly covered bars.</summary>
    public int BarCount => LastBar - FirstBar + 1;

    /// <summary>"phrase 1" or "phrases 1–2".</summary>
    public string PhrasesText => Phrases.Count == 1 ? $"phrase {Phrases[0].Number}" : $"phrases {Phrases[0].Number}–{Phrases[^1].Number}";

    /// <summary>Whether the span lies within the sentence.</summary>
    public bool Contains(TickSpan span) => Span.Onset <= span.Onset && span.End <= Span.End;
}

/// <summary>
/// The piece as sentences over its phrases (#133, decision record 0013): the level above the phrase,
/// so a phrase has a parent and a sentence has children. Phrases are grouped by what the notes and
/// the file say, in this order: a repeat sign ends a sentence; a phrase that starts where the
/// previous one comes back note for note is its consequent and joins it; a phrase that ends on the
/// tonic (a full close) ends the sentence; a phrase that ends on the dominant (a half close) or on
/// another note runs on. The last phrase ends the last sentence.
/// </summary>
/// <remarks>
/// <para>
/// Closes are read from the last notes to start in a phrase, as scale degrees of the key the piece
/// states or that its pitches fit best; the harmony is not analysed, so a close is inferred with
/// <see cref="CloseConfidence"/>, and uncertain when the key is. Every sentence and close carries its
/// attribution and basis, and the facts for Understand say which kind of claim each one is: the
/// grouping and the closes are inferred, and that one phrase "answers" another is an interpretation.
/// </para>
/// <para>The map is pure: the same phrases, notes and key signature give the same sentences.</para>
/// </remarks>
public sealed class SentenceMap
{
    /// <summary>A phrase ending on the tonic often closes a sentence, but the harmony under it is not read, so a close is not certain.</summary>
    public const double CloseConfidence = 0.6;

    /// <summary>Phrases that pair up by a return or by their closes often make a sentence, but other groupings are possible.</summary>
    public const double GroupConfidence = 0.6;

    /// <summary>A sentence whose edges are the piece's or repeat signs is read with little doubt.</summary>
    public const double MarkedConfidence = 0.9;

    private readonly Dictionary<int, Sentence> _ofPhrase;
    private readonly Dictionary<int, PhraseClose> _closes;

    private SentenceMap(IReadOnlyList<Sentence> sentences, IReadOnlyList<PhraseClose> closes, KeyReading key)
    {
        Sentences = sentences;
        Closes = closes;
        Key = key;
        _ofPhrase = sentences.SelectMany(sentence => sentence.Phrases.Select(phrase => (phrase.Number, sentence))).ToDictionary(pair => pair.Number, pair => pair.sentence);
        _closes = closes.ToDictionary(close => close.Phrase.Number);
    }

    /// <summary>The sentences, in time order, covering every phrase; at least one.</summary>
    public IReadOnlyList<Sentence> Sentences { get; }

    /// <summary>How each phrase ends, in phrase order.</summary>
    public IReadOnlyList<PhraseClose> Closes { get; }

    /// <summary>The key the closes are read in, with how sure it is.</summary>
    public KeyReading Key { get; }

    /// <summary>The sentences over <paramref name="phrases"/>, read from the piece's <paramref name="notes"/> and its key signature at the start, if any.</summary>
    public static SentenceMap Suggest(PhraseMap phrases, IEnumerable<Note> notes, KeySignature? signature)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        ArgumentNullException.ThrowIfNull(notes);

        Note[] all = [.. notes];
        KeyReading key = MotifAnalyzer.ReadKey(all, signature);
        IReadOnlyList<PhraseClose> closes = [.. phrases.Phrases.Select(phrase => ReadClose(phrase, all, key))];
        return new SentenceMap(Group(phrases.Phrases, closes), closes, key);
    }

    /// <summary>The sentence whose span is exactly <paramref name="span"/>, or null.</summary>
    public Sentence? Matching(TickSpan span) => Sentences.FirstOrDefault(sentence => sentence.Span == span);

    /// <summary>The sentence <paramref name="span"/> lies within, or null when it crosses sentences.</summary>
    public Sentence? Containing(TickSpan span) => Sentences.FirstOrDefault(sentence => sentence.Contains(span));

    /// <summary>The sentence a phrase belongs to.</summary>
    public Sentence Of(Phrase phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        return _ofPhrase.TryGetValue(phrase.Number, out Sentence? sentence) ? sentence : throw new ArgumentException("The phrase is not in this map.", nameof(phrase));
    }

    /// <summary>How a phrase ends.</summary>
    public PhraseClose CloseOf(Phrase phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        return _closes.TryGetValue(phrase.Number, out PhraseClose? close) ? close : throw new ArgumentException("The phrase is not in this map.", nameof(phrase));
    }

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
                $"Sentence {sentence.Letter} holds {sentence.PhrasesText}, {Bars(sentence.FirstBar, sentence.LastBar)}.",
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
                $"Phrase {phrase.Number} belongs to sentence {sentence.Letter} ({sentence.PhrasesText}, {Bars(sentence.FirstBar, sentence.LastBar)}).",
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

    private MotifFact CloseFact(Phrase phrase)
    {
        PhraseClose close = CloseOf(phrase);
        string text = close switch
        {
            { Place: null } => $"Phrase {phrase.Number} has no notes of its own, so no close is read.",
            { Kind: CloseKind.Full } => $"Phrase {phrase.Number} ends on {close.TopName}, the tonic: a full close.",
            { Kind: CloseKind.Half } => $"Phrase {phrase.Number} ends on {close.TopName}, {DegreeName(close.Degree!.Value)}: a half close.",
            { Degree: { } degree } => $"Phrase {phrase.Number} ends on {close.TopName}, {DegreeName(degree)}: no close is read.",
            _ => $"Phrase {phrase.Number} ends on {close.TopName}, outside the scale: no close is read.",
        };
        string basis = $"The last notes to start in the phrase, read as degrees of {Key.Key.Name} ({KeyBasis()}). The tonic reads as a full close; the dominant, the second degree and the leading note as a half close. The harmony under them is not analysed.";
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
            $"Phrase {phrase.Number} starts where phrase {phrase.Number - 1} comes back note for note.",
            $"The top line from the start of phrase {phrase.Number - 1} comes back with the same pitches and timing at the start of phrase {phrase.Number} ({PhraseMap.Describe(start)}).")
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
            (true, true) => $"phrase {second.Number} starts like phrase {first.Number} and ends more firmly (a half close, then a full close)",
            (true, false) => $"phrase {second.Number} starts like phrase {first.Number} and ends differently",
            _ => $"phrase {first.Number} ends on a half close and phrase {second.Number} on a full close",
        };
        return new MotifFact(
            $"sentence.answer.{sentence.Number}",
            FactLens.Structure,
            FactCertainty.Interpretation,
            $"Phrase {second.Number} answers phrase {first.Number}.",
            $"A reading, not a measurement: {why}. The notes do not decide it; hear whether the second phrase feels like a reply.")
        {
            Places = [PlaceOf(first), PlaceOf(second)],
        };
    }

    private string KeyBasis() => Key.Certainty switch
    {
        FactCertainty.Detected => "stated by the file",
        FactCertainty.Uncertain => $"a weak reading of the pitches; {Key.RunnerUp?.Name} fits almost as well",
        _ => "read from the pitches",
    };

    private static FactPlace PlaceOf(Phrase phrase) => new($"phrase {phrase.Number}", phrase.Span, []);

    private static FactCertainty Certainty(Attribution attribution) => attribution.Provenance switch
    {
        Provenance.Imported or Provenance.Detected or Provenance.UserConfirmed or Provenance.UserEdited => FactCertainty.Detected,
        _ => attribution.Confidence < CloseConfidence ? FactCertainty.Uncertain : FactCertainty.Inferred,
    };

    private static string DegreeName(int degree) => degree switch
    {
        1 => "the tonic",
        2 => "the second degree",
        3 => "the third degree",
        4 => "the fourth degree",
        5 => "the dominant",
        6 => "the sixth degree",
        _ => "the leading note",
    };

    private static string Bars(int first, int last) => first == last ? $"bar {first}" : $"bars {first}–{last}";

    private static PhraseClose ReadClose(Phrase phrase, Note[] notes, KeyReading key)
    {
        double confidence = key.Certainty == FactCertainty.Uncertain ? CloseConfidence / 2 : CloseConfidence;
        var attribution = new Attribution(Provenance.Inferred, confidence);
        Note[] inside = [.. notes.Where(note => phrase.Span.Contains(note.Span.Onset))];
        if (inside.Length == 0)
        {
            return new PhraseClose(phrase, CloseKind.None, null, null, attribution, null);
        }

        Tick last = inside.Max(note => note.Span.Onset);
        Note[] lastNotes = [.. inside.Where(note => note.Span.Onset == last).OrderByDescending(note => note.Pitch.MidiNumber)];
        Note top = lastNotes[0];
        Note? bass = lastNotes.Length > 1 ? lastNotes[^1] : null;
        int? topDegree = Degree(key.Key, top.Pitch.MidiNumber);
        int? bassDegree = bass is null ? null : Degree(key.Key, bass.Pitch.MidiNumber);
        CloseKind kind = (topDegree, bassDegree) switch
        {
            (1, _) => CloseKind.Full,
            (3 or 5, 1) => CloseKind.Full,
            (5 or 2 or 7, _) => CloseKind.Half,
            (_, 5) => CloseKind.Half,
            _ => CloseKind.None,
        };
        string name = key.Key.Spell(top.Pitch.MidiNumber);
        return new PhraseClose(phrase, kind, name, topDegree, attribution, FactPlace.Of($"the last notes of phrase {phrase.Number}, {name}", lastNotes));
    }

    /// <summary>The scale degree, 1 to 7, of a note in the key; null for a note outside the scale.</summary>
    private static int? Degree(MusicalKey key, int midiNumber)
    {
        ScalePosition position = ScaleSteps.Locate(key, midiNumber);
        return position.Alteration == 0 ? (((position.Step % ScaleSteps.StepsPerOctave) + ScaleSteps.StepsPerOctave) % ScaleSteps.StepsPerOctave) + 1 : null;
    }

    private static IReadOnlyList<Sentence> Group(IReadOnlyList<Phrase> phrases, IReadOnlyList<PhraseClose> closes)
    {
        var sentences = new List<Sentence>();
        var current = new List<Phrase>();
        var reasons = new List<string>();
        double confidence = 1;
        bool detected = true;
        for (int i = 0; i < phrases.Count; i++)
        {
            Phrase phrase = phrases[i];
            current.Add(phrase);
            Phrase? next = i + 1 < phrases.Count ? phrases[i + 1] : null;
            PhraseClose close = closes[i];
            bool ends;
            if (next is null)
            {
                ends = true;
                reasons.Add(current.Count == 1 ? "it ends with the piece" : $"phrase {phrase.Number} ends with the piece");
            }
            else if (next.Start?.Evidence.Contains(PhraseEvidence.RepeatSign) == true)
            {
                ends = true;
                reasons.Add($"a repeat sign stands after phrase {phrase.Number}");
            }
            else if (next.Start?.Evidence.Contains(PhraseEvidence.Repetition) == true && current.Count == 1)
            {
                ends = false;
                detected = false;
                confidence = Math.Min(confidence, Math.Min(GroupConfidence, next.Start.Attribution.Confidence));
                reasons.Add($"phrase {next.Number} starts where phrase {phrase.Number} comes back note for note, so it reads as the consequent");
            }
            else if (close.Kind == CloseKind.Full)
            {
                ends = true;
                detected = false;
                confidence = Math.Min(confidence, Math.Min(GroupConfidence, close.Attribution.Confidence));
                reasons.Add($"phrase {phrase.Number} ends on the tonic, a full close");
            }
            else
            {
                ends = false;
                detected = false;
                confidence = Math.Min(confidence, Math.Min(GroupConfidence, close.Attribution.Confidence));
                reasons.Add(close.Kind == CloseKind.Half
                    ? $"phrase {phrase.Number} ends on a half close, so it runs on"
                    : $"phrase {phrase.Number} reads no close, so it runs on");
            }

            if (ends)
            {
                Phrase first = current[0];
                bool startsMarked = first.Number == 1 || first.Start?.Evidence.Contains(PhraseEvidence.RepeatSign) == true;
                Attribution found = detected && startsMarked
                    ? new Attribution(Provenance.Detected, MarkedConfidence)
                    : new Attribution(Provenance.Inferred, Math.Min(confidence, GroupConfidence));
                string basis = Capitalize(string.Join("; ", reasons)) + ".";
                sentences.Add(new Sentence(sentences.Count + 1, TickSpan.FromBounds(first.Span.Onset, phrase.Span.End), first.FirstBar, phrase.LastBar, [.. current], found, basis));
                current.Clear();
                reasons.Clear();
                confidence = 1;
                detected = true;
            }
        }

        return sentences;
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
