using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Variations;
using MotiffLab.Web.Explore;
using MotiffLab.Web.Observations;
using MotiffLab.Web.Understand;

namespace MotiffLab.Web.Workspace;

/// <summary>
/// The shared workspace state (decision record 0013, section 6): what the workspace page holds behind
/// Practise, Understand, Explore and Tools, in one shape the tabs read. The page is its only writer:
/// it builds a fresh snapshot for each render and cascades it, and a tab asks for a change through its
/// callbacks; so no tab can reset another's part, and switching tabs changes nothing in the state.
/// The score and the roll are views of the same selection, playback and variation.
/// </summary>
/// <param name="Selection">The primary selection and its context in the phrase map.</param>
/// <param name="Playback">What plays, loops and is heard.</param>
/// <param name="Variation">The active chain and step, the experiment tried on it, and the compare choice.</param>
/// <param name="Facts">The facts of the selection and the one highlighted.</param>
/// <param name="Ideas">What the player kept for the selection.</param>
public sealed record WorkspaceState(
    WorkspaceSelection Selection,
    WorkspacePlayback Playback,
    WorkspaceVariation Variation,
    WorkspaceFacts Facts,
    WorkspaceIdeas Ideas);

/// <summary>
/// The primary selection (0013, section 3): exactly one, with its parents and siblings as context.
/// </summary>
/// <param name="Motif">The selection resolved to notes, or null when nothing is selected.</param>
/// <param name="Filter">Which notes count: all, a hand, a track.</param>
/// <param name="Phrases">The project's phrase map, once the piece has loaded.</param>
/// <param name="Phrase">The phrase the selection is, when it matches one exactly.</param>
/// <param name="Previous">The phrase before the selection's, when there is one.</param>
/// <param name="Next">The phrase after the selection's, when there is one.</param>
/// <param name="Sentences">The sentences over the phrases (#133), once the piece has loaded.</param>
/// <param name="Sentence">The sentence the selection is, when it matches one exactly.</param>
/// <param name="SentenceAround">The sentence the selection lies within: the selection itself when it is one, else its parent as context.</param>
/// <param name="PhraseAround">The phrase the selection lies within: the selection itself when it is one, else its parent as context; null when it crosses phrases.</param>
/// <param name="PreviousSentence">The sentence before the selection's, when there is one.</param>
/// <param name="NextSentence">The sentence after the selection's, when there is one.</param>
public sealed record WorkspaceSelection(
    MotifSelection? Motif,
    SelectionFilter Filter,
    PhraseMap? Phrases,
    Phrase? Phrase,
    Phrase? Previous,
    Phrase? Next,
    IReadOnlyList<Sentence> Sentences,
    Sentence? Sentence,
    Sentence? SentenceAround,
    Phrase? PhraseAround,
    Sentence? PreviousSentence,
    Sentence? NextSentence)
{
    /// <summary>Whether the selection has notes to play, describe or vary.</summary>
    public bool HasNotes => Motif is { NoteIds.Count: > 0 };

    /// <summary>Whether the selection could be made a phrase: it has notes and is not one yet.</summary>
    public bool CanMakePhrase => HasNotes && Phrase is null;

    /// <summary>The level the selection is at (decision record 0013, section 3): ‹ › step among the siblings of this level.</summary>
    public SelectionLevel Level => Motif is null ? SelectionLevel.None : Sentence is not null ? SelectionLevel.Sentence : Phrase is not null ? SelectionLevel.Phrase : SelectionLevel.Range;

    /// <summary>Whether ‹ (-1) or › (1) leads somewhere at the selected level: a sibling sentence, else a neighbouring phrase.</summary>
    public bool CanStep(int direction) => Level == SelectionLevel.Sentence
        ? (direction < 0 ? PreviousSentence : NextSentence) is not null
        : (direction < 0 ? Previous : Next) is not null;
}

/// <summary>The level of the primary selection.</summary>
public enum SelectionLevel
{
    None,
    Sentence,
    Phrase,

    /// <summary>Bars or notes that are not a structure the maps know.</summary>
    Range,
}

/// <summary>What plays, loops and is heard; the same whichever tab is open.</summary>
/// <param name="Playing">Whether the engine is playing (or paused mid-way), rather than stopped.</param>
/// <param name="Looping">Whether the selection loops.</param>
/// <param name="PlayingOn">Whether playback carries on past the selection, into the piece (#126).</param>
/// <param name="TempoPercent">The tempo, as a percentage of the piece's.</param>
/// <param name="ListenTo">What is heard alone while the selection loops: everything, a hand, a layer.</param>
/// <param name="ListenChoices">The choices the selection allows, with their labels.</param>
/// <param name="HandsNote">A note that the hands are a reading of the notes, when they are.</param>
/// <param name="Layered">Whether the selection has a melody over an accompaniment.</param>
/// <param name="OriginalHeard">Whether the player chose to hear the original on its own.</param>
/// <param name="VariationHeard">Whether the player chose to hear the variation on its own.</param>
/// <param name="Alternating">Whether the original and the variation loop one after the other.</param>
/// <param name="SoundingVariation">While alternating: true while the variation sounds, false while the original does; else null.</param>
/// <param name="AnswerLooping">Whether the motif and its answer loop together, for an experiment elsewhere.</param>
public sealed record WorkspacePlayback(
    bool Playing,
    bool Looping,
    bool PlayingOn,
    int TempoPercent,
    ListenTo ListenTo,
    IReadOnlyList<(ListenTo Choice, string Label)> ListenChoices,
    string? HandsNote,
    bool Layered,
    bool OriginalHeard,
    bool VariationHeard,
    bool Alternating,
    bool? SoundingVariation,
    bool AnswerLooping);

/// <summary>The active variation and the experiment that made it.</summary>
/// <param name="Chain">The chain of steps on the selection, or null before the first.</param>
/// <param name="Current">The variation the chain gives, or null when it has no result.</param>
/// <param name="Trial">The experiment tried on the chain, while it is the chain's; see <see cref="ExploreTrial"/>.</param>
/// <param name="Offers">The experiments the Explore card offers: the trial's, or those for the selection.</param>
/// <param name="UnderstandOffers">The offers Understand links its facts to: the card's, unless the trial was offered for another passage.</param>
public sealed record WorkspaceVariation(
    VariationChain? Chain,
    Variation? Current,
    ExploreTrial? Trial,
    IReadOnlyList<Experiment> Offers,
    IReadOnlyList<Experiment> UnderstandOffers)
{
    /// <summary>What changed, from the diff, when there is a variation.</summary>
    public string? Explanation => Current?.Explanation;
}

/// <summary>The facts about the selection (S1-A1 to S1-A5), the one to hear first, and the one chosen to highlight.</summary>
/// <param name="All">The facts of the selection from every analysis, in music-core's order within each lens.</param>
/// <param name="Chosen">The fact highlighted in the score and on the roll, with the place when one is.</param>
/// <param name="KeyObservation">The one fact to hear first, as <see cref="FactRanking"/> ranks them (#132); null without facts.</param>
/// <param name="KeyReason">Why that fact comes first, from the ranking's own criteria.</param>
/// <param name="Recommended">The one experiment recommended next: the first the catalogue offers for the selection, so it can always run; null without offers.</param>
public sealed record WorkspaceFacts(IReadOnlyList<MotifFact> All, FactChoice? Chosen, MotifFact? KeyObservation, string? KeyReason, Experiment? Recommended);

/// <summary>What the player kept about the selection.</summary>
/// <param name="Rows">The observations kept for the selected phrase, with where each one comes from.</param>
/// <param name="OnCard">The observation the Explore card's Keep made, while its trial shows.</param>
/// <param name="KeptText">What Keep says: with the project, or only until the page is left.</param>
public sealed record WorkspaceIdeas(IReadOnlyList<ObservationRow> Rows, Observation? OnCard, string KeptText);