namespace MotiffLab.MusicCore.Analysis;

/// <summary>How a fact was reached, so the app never presents a guess as something it read.</summary>
public enum FactCertainty
{
    /// <summary>Read or counted directly from the notes or stated by the file.</summary>
    Detected,

    /// <summary>Worked out by a rule from the notes, for example the top line of chords or a key from the pitches.</summary>
    Inferred,

    /// <summary>A rule's best guess on thin or conflicting evidence.</summary>
    Uncertain,
}

/// <summary>The lens a fact belongs to on the Understand screen (N4).</summary>
public enum FactLens
{
    Rhythm,
    Melody,
    Key,

    /// <summary>Repetition and phrasing around the motif (S1-A4).</summary>
    Structure,
}

/// <summary>One plain fact about a motif, with how sure it is and why.</summary>
/// <param name="Id">Stable within a motif's analysis, for example "melody.intervals"; explanations cite it.</param>
/// <param name="Text">The fact in a sentence, stating only what was measured.</param>
/// <param name="Basis">Where the fact comes from, and why it is inferred or uncertain when it is.</param>
public sealed record MotifFact(string Id, FactLens Lens, FactCertainty Certainty, string Text, string Basis);