namespace MotiffLab.MusicCore.Analysis.Phrasing;

/// <summary>The three levels the piece is read at (decision record 0018, section 3), finest first.</summary>
public enum PhraseLevel
{
    /// <summary>Motifs and half-phrases of 1–4 bars (<see cref="ShortMap"/>).</summary>
    Short,

    /// <summary>Phrases of 4–8 bars, the default (<see cref="PhraseMap"/>).</summary>
    Phrase,

    /// <summary>Sentences of 8–16 or more bars (<see cref="SentenceMap"/>).</summary>
    Sentence,
}