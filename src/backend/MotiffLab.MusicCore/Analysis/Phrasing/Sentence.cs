using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis.Phrasing;

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
/// <param name="Basis">Why the phrases make one sentence, in a sentence, in the language the map was asked for.</param>
public sealed record Sentence(int Number, TickSpan Span, int FirstBar, int LastBar, IReadOnlyList<Phrase> Phrases, Attribution Found, string Basis)
{
    /// <summary>"A" for the first sentence, "B" for the second; numbers after Z.</summary>
    public string Letter => Number <= 26 ? ((char)('A' + Number - 1)).ToString() : Number.ToString();

    /// <summary>The number of bars the sentence touches, counting partly covered bars.</summary>
    public int BarCount => LastBar - FirstBar + 1;

    /// <summary>"phrase 1" or "phrases 1–2", in English.</summary>
    public string PhrasesText => PhrasesIn(Words.English);

    /// <summary>"phrase 1" or "phrases 1–2"; "фраза 1" or "фразы 1–2".</summary>
    public string PhrasesIn(Words words) =>
        Phrases.Count == 1 ? $"{words.T("phrase", "фраза")} {Phrases[0].Number}" : $"{words.T("phrases", "фразы")} {Phrases[0].Number}–{Phrases[^1].Number}";

    /// <summary>Whether the span lies within the sentence.</summary>
    public bool Contains(TickSpan span) => Span.Onset <= span.Onset && span.End <= Span.End;
}