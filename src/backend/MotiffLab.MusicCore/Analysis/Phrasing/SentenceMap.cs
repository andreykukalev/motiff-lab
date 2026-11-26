using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

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
/// <para>The map is pure: the same phrases, notes, key signature and language give the same sentences. Its sentences are written in the <see cref="Words"/> it was asked for (#134).</para>
/// </remarks>
public sealed partial class SentenceMap
{
    /// <summary>A phrase ending on the tonic often closes a sentence, but the harmony under it is not read, so a close is not certain.</summary>
    public const double CloseConfidence = 0.6;

    /// <summary>Phrases that pair up by a return or by their closes often make a sentence, but other groupings are possible.</summary>
    internal const double GroupConfidence = 0.6;

    /// <summary>A sentence whose edges are the piece's or repeat signs is read with little doubt.</summary>
    internal const double MarkedConfidence = 0.9;

    private readonly Dictionary<int, Sentence> _ofPhrase;
    private readonly Dictionary<int, PhraseClose> _closes;
    private readonly Words _words;
    private readonly PhraseMap _phrases;

    private SentenceMap(PhraseMap phrases, IReadOnlyList<Sentence> sentences, IReadOnlyList<PhraseClose> closes, KeyReading key, PhraseEdits edits, Words words)
    {
        _phrases = phrases;
        Sentences = sentences;
        Closes = closes;
        Key = key;
        Edits = edits;
        _words = words;
        _ofPhrase = sentences.SelectMany(sentence => sentence.Phrases.Select(phrase => (phrase.Number, sentence))).ToDictionary(pair => pair.Number, pair => pair.sentence);
        _closes = closes.ToDictionary(close => close.Phrase.Number);
    }

    /// <summary>The sentences, in time order, covering every phrase; at least one.</summary>
    public IReadOnlyList<Sentence> Sentences { get; }

    /// <summary>How each phrase ends, in phrase order.</summary>
    public IReadOnlyList<PhraseClose> Closes { get; }

    /// <summary>The key the closes are read in, with how sure it is.</summary>
    public KeyReading Key { get; }

    /// <summary>
    /// The player's changes at this level (decision record 0018, section 3): phrase boundaries where a sentence
    /// must end (<see cref="PhraseEdits.Added"/>) and phrase boundaries the grouping must run on across
    /// (<see cref="PhraseEdits.Removed"/>). An edit at a tick that is not a phrase boundary in effect is left out.
    /// </summary>
    public PhraseEdits Edits { get; }

    /// <summary>The sentences over <paramref name="phrases"/>, read from the piece's <paramref name="notes"/> and its key signature at the start, if any, written in <paramref name="words"/>.</summary>
    public static SentenceMap Suggest(PhraseMap phrases, IEnumerable<Note> notes, KeySignature? signature, Words words = default) =>
        Suggest(phrases, notes, signature, null, words);

    /// <summary>The sentences over <paramref name="phrases"/> with the player's <paramref name="edits"/> at the sentence level.</summary>
    public static SentenceMap Suggest(PhraseMap phrases, IEnumerable<Note> notes, KeySignature? signature, PhraseEdits? edits, Words words = default)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        ArgumentNullException.ThrowIfNull(notes);

        Note[] all = [.. notes];
        KeyReading key = MotifAnalyzer.ReadKey(all, signature);
        IReadOnlyList<PhraseClose> closes = [.. phrases.Phrases.Select(phrase => ReadClose(phrase, all, key, words))];
        return new SentenceMap(phrases, Group(phrases.Phrases, closes, edits ?? PhraseEdits.None, words), closes, key, edits ?? PhraseEdits.None, words);
    }

    /// <summary>The same phrases and notes with other edits at the sentence level.</summary>
    public SentenceMap With(PhraseEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return new SentenceMap(_phrases, Group(_phrases.Phrases, Closes, edits, _words), Closes, Key, edits, _words);
    }

    /// <summary>The sentence that holds a position; the last sentence for the end of the piece or later.</summary>
    public Sentence At(Tick position) => Sentences.LastOrDefault(sentence => sentence.Span.Onset <= position) ?? Sentences[0];

    /// <summary>
    /// The edits that make <paramref name="span"/> one sentence, given that its edges are phrase boundaries or
    /// the piece's ends: the sentence ends at both edges and runs on across every phrase boundary inside.
    /// </summary>
    public PhraseEdits MakeSentence(TickSpan span) => Editor().Make(span.Onset, span.End);

    /// <summary>The edits that join a sentence with the one after it: the grouping runs on across the phrase boundary between them. Unchanged for the last sentence.</summary>
    public PhraseEdits JoinWithNext(Sentence sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        return sentence.Span.End >= _phrases.End ? Edits : Editor().Join(sentence.Span.End);
    }

    /// <summary>The edits that end a sentence at the phrase boundary <paramref name="at"/>; unchanged when no phrase starts there.</summary>
    public PhraseEdits SplitAt(Tick at) => Editor().Split(at);

    /// <summary>
    /// The sentence edits at the phrase boundaries, the only places a sentence can end: each edit is the player's
    /// decision there, which stands whatever the grouping would read, so it is kept even where the grouping agrees.
    /// </summary>
    private LevelEditor Editor()
    {
        Tick[] boundaries = [.. _phrases.Boundaries.Select(static boundary => boundary.At)];
        var at = boundaries.ToHashSet();
        return new LevelEditor(Edits, boundaries, at.Contains, null);
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
}