using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.Web.Playback;
using MotiffLab.Web.Workspace;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The shared workspace state (decision record 0013, section 6): one snapshot of the page's selection,
/// playback, variation, facts and ideas, built for each render and cascaded to the tabs. The page is the
/// only writer; the tabs read the snapshot and ask for changes through their callbacks.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The state the tabs read, as it is now.</summary>
    private WorkspaceState State
    {
        get
        {
            IReadOnlyList<MotifFact> facts = Facts;
            MotifFact? key = FactRanking.KeyObservation(facts);
            // The recommended next step stays on the selection when an offer does; one elsewhere (the motif's return) only otherwise.
            IReadOnlyList<Experiment> offers = UnderstandOffers;
            Experiment? recommended = offers.FirstOrDefault(offer => offer.Passage.Span == _selection?.Span) ?? offers.FirstOrDefault();
            return new(
                new WorkspaceSelection(
                    _selection,
                    _filter,
                    _phrases,
                    CurrentPhrase,
                    StepTarget(-1),
                    StepTarget(1),
                    _sentences?.Sentences ?? [],
                    CurrentSentence,
                    SentenceAround,
                    PhraseAround,
                    SentenceTarget(-1),
                    SentenceTarget(1)),
                new WorkspacePlayback(
                    Playing: Player.State != PlaybackState.Stopped,
                    Looping: _looping,
                    PlayingOn: _playOnLooped is not null,
                    TempoPercent: TempoPercent,
                    ListenTo: _listenTo,
                    ListenChoices: ListenChoices,
                    HandsNote: HandsNote,
                    Layered: _layered,
                    OriginalHeard: ComparingOriginal,
                    VariationHeard: ComparingVariation,
                    Alternating: _alternating,
                    SoundingVariation: SoundingVariation,
                    AnswerLooping: _answerLoop is not null),
                new WorkspaceVariation(_chain, _variation, ActiveTrial, CardOffers, UnderstandOffers),
                new WorkspaceFacts(facts, _highlight?.Choice, key, key is null ? null : FactRanking.Reason(key), recommended),
                new WorkspaceIdeas(KeptRows, KeptOnCard, KeptText));
        }
    }
}