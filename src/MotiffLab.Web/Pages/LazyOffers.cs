using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Invitations;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The experiments music-core offers for a selection (decision record 0010), from the facts and the context read when it
/// was selected, made the first time something shows or tries them: the Insights stream, the Explore card, Try (#419).
/// On a long score, working out every way to change a passage took about half of a selection's time, with nothing on screen
/// to show it while Insights was closed. The same facts and context give the same offers whenever they are made.
/// </summary>
internal sealed class LazyOffers(IReadOnlyList<MotifFact> facts, InvitationContext? context)
{
    private IReadOnlyList<Experiment>? _made;

    /// <summary>No selection, so no offers.</summary>
    public static LazyOffers None { get; } = new([], null);

    public IReadOnlyList<Experiment> Value => _made ??= context is null ? [] : TryChips.Buttons(facts, context);
}