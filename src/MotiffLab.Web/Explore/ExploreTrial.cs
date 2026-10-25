using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.Web.Explore;

/// <summary>Which layer of the passage is heard while it loops; the layers are read from the texture, not the hands.</summary>
public enum ListenTo
{
    Everything,
    Melody,
    Accompaniment,
}

/// <summary>A request from the Explore card to try one alternative of an experiment.</summary>
/// <param name="Entry">How the player got there, for analytics: "offer", "question", "surprise", "another" or "fact" (from the Understand tab).</param>
public sealed record ExploreRequest(Experiment Experiment, int Alternative, string Entry);

/// <summary>
/// The experiment being tried (decision record 0010): which alternative, the selection and offers it
/// came from, the chain it made, and the player's own chain to go back to.
/// </summary>
/// <param name="OfferedIn">The selection and facts the experiment was offered for; the card keeps showing its offers.</param>
/// <param name="Chain">The variation the alternative made. The trial is current only while the page shows this chain.</param>
/// <param name="Before">The chain the player had before trying, put back by Leave it; null when there was none.</param>
/// <param name="KeepBefore">The player's Keep choices before trying, put back with <paramref name="Before"/>.</param>
/// <param name="Kept">Whether the player kept the variation, which saves it with the project.</param>
public sealed record ExploreTrial(
    Experiment Experiment,
    int Alternative,
    InvitationContext OfferedIn,
    IReadOnlyList<Experiment> Offers,
    VariationChain Chain,
    VariationChain? Before,
    IReadOnlySet<KeepProperty> KeepBefore,
    bool Kept)
{
    /// <summary>The alternative being heard.</summary>
    public ExperimentAlternative Current => Experiment.Alternatives[Alternative];

    /// <summary>The facts music-core found where the experiment was offered, which its fact ids name (#25).</summary>
    public IReadOnlyList<MotifFact> Facts => [.. OfferedIn.Motif.Facts, .. OfferedIn.Structure.Facts, .. OfferedIn.Character];

    /// <summary>Whether the experiment changes another passage than the one it was offered for (the motif's return).</summary>
    public bool Elsewhere => Experiment.Passage.Span != OfferedIn.Selection.Span;
}