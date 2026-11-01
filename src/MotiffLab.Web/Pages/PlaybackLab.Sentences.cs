using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Sentences as the level above the phrase (#133, decision record 0013): music-core suggests them over the
/// phrase map, the hierarchy line and the strip show the selection's parents and children as context, a
/// sentence is selected whole, and ‹ › step among the siblings of the selected level. The page never
/// groups phrases itself; it reads <see cref="SentenceMap"/>.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The sentences over the phrases, suggested again whenever the phrases or the notes change.</summary>
    private SentenceMap? _sentences;

    /// <summary>The facts about the selected sentence or phrase from the sentence map, in the structure lens.</summary>
    private IReadOnlyList<MotifFact> _sentenceFacts = [];

    /// <summary>
    /// The sentence the selection is, exactly; null for a phrase, a range or nothing. A sentence of one phrase is
    /// that phrase: the phrase is the unit of work (0013), so the selection reads as the phrase with the sentence as its context.
    /// </summary>
    private Sentence? CurrentSentence => _selection is { } selection && CurrentPhrase is null ? _sentences?.Matching(selection.Span) : null;

    /// <summary>The sentence the selection lies within, as its context; the selection itself when it is one.</summary>
    private Sentence? SentenceAround => _selection is { } selection ? _sentences?.Containing(selection.Span) : null;

    /// <summary>The phrase the selection lies within, as its context; the selection itself when it is one; null across phrases.</summary>
    private Phrase? PhraseAround =>
        CurrentPhrase ?? (_selection is { } selection && _phrases is { } map && map.Covering(selection.Span) is { } phrase && phrase.Span.Onset <= selection.Span.Onset && selection.Span.End <= phrase.Span.End ? phrase : null);

    /// <summary>The sibling sentence ‹ (-1) or › (1) leads to while a sentence is selected; null otherwise or at either end.</summary>
    private Sentence? SentenceTarget(int direction) =>
        CurrentSentence is { } sentence && _sentences is { } map && sentence.Number + direction is int number && number >= 1 && number <= map.Sentences.Count
            ? map.Sentences[number - 1]
            : null;

    /// <summary>Whether ‹ or › leads somewhere at the selected level.</summary>
    private bool CanStep(int direction) => CurrentSentence is not null ? SentenceTarget(direction) is not null : StepTarget(direction) is not null;

    /// <summary>Suggests the sentences over the current phrases and notes; nothing without a phrase map.</summary>
    private void SuggestSentences() =>
        _sentences = _phrases is { } map && _notes is { } notes ? SentenceMap.Suggest(map, notes, KeySignature.InForce(_keys, Tick.Zero), Words) : null;

    /// <summary>The most bars a selection of <paramref name="span"/> may hold: a structure the maps know is selected whole (0013).</summary>
    private int SelectionCap(TickSpan span) =>
        _sentences?.Matching(span) is not null || _phrases?.Phrases.Any(phrase => phrase.Span == span) == true ? MotifSelection.MaxStructureBars : MotifSelection.MaxBars;

    /// <summary>Selects a sentence whole as the working context; the tab, the tempo, the loop and what is heard carry on.</summary>
    private Task PickSentenceAsync(Sentence sentence) => SelectAsync(sentence.Span, note: null);

    /// <summary>Selects a span a crumb of the hierarchy line names: a sentence or a phrase the selection lies within.</summary>
    private Task PickSpanAsync(TickSpan span) => SelectAsync(span, note: null);

    /// <summary>The facts the sentence map has about the selection: about the selected sentence, or about the selected phrase's place in its sentence.</summary>
    private IReadOnlyList<MotifFact> SentenceFactsFor(MotifSelection selection)
    {
        if (_sentences is not { } map)
        {
            return [];
        }

        if (_phrases?.Matching(selection.Span) is { } phrase)
        {
            return map.Facts(phrase);
        }

        return map.Matching(selection.Span) is { } sentence ? map.Facts(sentence) : [];
    }
}