namespace MotiffLab.MusicCore.Analysis.Phrasing;

/// <summary>The player's edits at every level, saved with the project; each level's edits stick to the piece until Reset to detected.</summary>
public sealed record LevelEdits(PhraseEdits Short, PhraseEdits Phrase, PhraseEdits Sentence)
{
    public static LevelEdits None { get; } = new(PhraseEdits.None, PhraseEdits.None, PhraseEdits.None);

    public bool IsEmpty => Short.IsEmpty && Phrase.IsEmpty && Sentence.IsEmpty;

    public PhraseEdits this[PhraseLevel level] => level switch
    {
        PhraseLevel.Short => Short,
        PhraseLevel.Phrase => Phrase,
        PhraseLevel.Sentence => Sentence,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown level."),
    };

    /// <summary>These edits with one level's replaced.</summary>
    public LevelEdits With(PhraseLevel level, PhraseEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return level switch
        {
            PhraseLevel.Short => this with { Short = edits },
            PhraseLevel.Phrase => this with { Phrase = edits },
            PhraseLevel.Sentence => this with { Sentence = edits },
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown level."),
        };
    }
}