using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade.Sessions;

/// <summary>
/// What was understood about the selection (understand): what music-core read for it, the facts about the phrase or the
/// sentence it is, and the Try chips each fact offers.
/// </summary>
internal sealed record SelectionUnderstanding(InvitationContext Context, IReadOnlyList<MotifFact> SentenceFacts)
{
    /// <summary>The Try chips <paramref name="fact"/> offers on the selection (0018, section 7).</summary>
    public IReadOnlyList<TryChip> Chips(MotifFact fact) => TryChips.Offer(fact.Id, Context);
}

/// <summary>The circle of fifths for the selection (circle), from the key and chords read for it, and where the selection starts.</summary>
internal sealed record SelectionCircle(KeyCircleView? View, Tick At);