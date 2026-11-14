using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.Web.Explore;
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
            // The facts and offers as they are now, read only when the stream or the card shows them (#419).
            ExploreTrial? trial = ActiveTrial;
            SelectionReading? reading = _reading;
            IReadOnlyList<Experiment> Card() => trial?.Offers ?? reading?.Offers ?? [];
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
                // Understand links its facts only to offers for the same selection: none while an experiment offered for another
                // selection runs (the motif's return), as its offers rest on other facts.
                new WorkspaceVariation(_chain, _variation, trial, Card, () => trial is { } tried && !ReferenceEquals(tried.OfferedIn, reading?.Context) ? [] : Card()),
                new WorkspaceFacts(() => reading?.Facts ?? [], _highlight?.Choice, () => reading?.Analysis.Circle),
                new WorkspaceIdeas(KeptRows, KeptOnCard, KeptText));
        }
    }
}