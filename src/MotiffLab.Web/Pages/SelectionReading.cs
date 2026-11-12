using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Invitations;

namespace MotiffLab.Web.Pages;

/// <summary>
/// What music-core reads about one selection, made the first time something on screen reads it (#419, #433): the motif,
/// harmony and character readings and the circle of fifths from them, its structure (how it sits among the phrases and
/// where it comes back), all its facts, the context the experiment catalogue reads and the experiments it offers (decision
/// record 0010). The Insights stream, Ask, the Explore card, the circle, the DNA map, Try and a variation read them; a card
/// turned by playback in phrase focus shows none of them, so turning it no longer waits for them. Everything here comes
/// from what was read when the passage was selected, so it is the same whenever it is made.
/// </summary>
internal sealed class SelectionReading(
    Func<SelectionAnalysis> read,
    IReadOnlyList<MotifFact> sentence,
    Func<StructureAnalysis> readStructure,
    Func<SelectionAnalysis, StructureAnalysis, InvitationContext> context)
{
    private SelectionAnalysis? _analysis;
    private StructureAnalysis? _structure;
    private IReadOnlyList<MotifFact>? _facts;
    private InvitationContext? _context;
    private IReadOnlyList<Experiment>? _offers;

    /// <summary>The motif, harmony and character readings and the circle of fifths.</summary>
    public SelectionAnalysis Analysis => _analysis ??= read();

    public StructureAnalysis Structure => _structure ??= readStructure();

    /// <summary>Everything music-core found about the selection, for the stream to group by lens.</summary>
    public IReadOnlyList<MotifFact> Facts => _facts ??=
        [.. Analysis.Motif.Facts, .. Structure.Facts, .. sentence, .. Analysis.Character, .. Analysis.Harmony.Facts];

    /// <summary>The selection's notes and facts as the experiment catalogue reads them; one context for the selection, as a trial names where it was offered.</summary>
    public InvitationContext Context => _context ??= context(Analysis, Structure);

    public IReadOnlyList<Experiment> Offers => _offers ??= TryChips.Buttons(Facts, Context);
}

/// <summary>A selection's motif, harmony and character readings, and the circle of fifths drawn from them.</summary>
internal sealed record SelectionAnalysis(MotifAnalysis Motif, HarmonyAnalysis Harmony, IReadOnlyList<MotifFact> Character, KeyCircleView? Circle);