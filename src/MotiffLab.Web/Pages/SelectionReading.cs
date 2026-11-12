using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Invitations;

namespace MotiffLab.Web.Pages;

/// <summary>
/// What music-core reads about one selection that is not on screen as soon as it is selected (#419): its structure (how it
/// sits among the phrases and where it comes back), all its facts, the context the experiment catalogue reads and the
/// experiments it offers (decision record 0010). Each is made the first time the Insights stream, Ask, the Explore card,
/// the DNA map or Try reads it. On a long score the structure took about half of a selection's time and the offers as
/// long again, with nothing showing them while Insights was closed. Everything here comes from what was read when the
/// passage was selected, so it is the same whenever it is made.
/// </summary>
internal sealed class SelectionReading(
    MotifAnalysis analysis,
    IReadOnlyList<MotifFact> sentence,
    IReadOnlyList<MotifFact> character,
    HarmonyAnalysis harmony,
    Func<StructureAnalysis> readStructure,
    Func<StructureAnalysis, InvitationContext> context)
{
    private StructureAnalysis? _structure;
    private IReadOnlyList<MotifFact>? _facts;
    private InvitationContext? _context;
    private IReadOnlyList<Experiment>? _offers;

    public StructureAnalysis Structure => _structure ??= readStructure();

    /// <summary>Everything music-core found about the selection, for the stream to group by lens.</summary>
    public IReadOnlyList<MotifFact> Facts => _facts ??= [.. analysis.Facts, .. Structure.Facts, .. sentence, .. character, .. harmony.Facts];

    /// <summary>The selection's notes and facts as the experiment catalogue reads them; one context for the selection, as a trial names where it was offered.</summary>
    public InvitationContext Context => _context ??= context(Structure);

    public IReadOnlyList<Experiment> Offers => _offers ??= TryChips.Buttons(Facts, Context);
}