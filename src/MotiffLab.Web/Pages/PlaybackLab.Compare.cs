using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Return to the original and compare (#43, step 6 of docs/08-central-product-model.md): Original and
/// Variation switch both what is heard and what the score shows, keeping the variation and its steps;
/// One after the other loops the original phrase, then the variation, with the views following the one
/// that sounds. The switching between the two is the engine's (an alternating loop, decision record
/// 0007), so it lands on the exact frame and does not depend on the page.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The Hear choice for the original, then the variation. Not an engine source: it starts the alternating loop.</summary>
    private const string OneAfterTheOther = "one-after-the-other";

    /// <summary>Whether the original and the variation loop one after the other. Only while the selection loops.</summary>
    private bool _alternating;

    /// <summary>Whether Original put the score on the original, so the next audition of the variation puts it back.</summary>
    private bool _scoreOnOriginal;

    /// <summary>Whether the player chose to hear the original phrase on its own.</summary>
    private bool ComparingOriginal => !_alternating && _hear is [Player.OriginalSource];

    /// <summary>Whether the player chose to hear the variation on its own.</summary>
    private bool ComparingVariation => !_alternating && _hear is [VariationSource];

    /// <summary>While the two loop one after the other: true while the variation sounds, false while the original does; otherwise null.</summary>
    private bool? SoundingVariation => _alternating && Player.Passes is not null ? Player.Pass == 1 : null;

    /// <summary>The score of the original: as written when the file has one and no note was corrected, else generated from the notes.</summary>
    private ScoreChoice OriginalScore => _asWritten is not null && _edits.IsEmpty ? ScoreChoice.AsWritten : ScoreChoice.Generated;

    /// <summary>While the two loop one after the other, the score of the one sounding; otherwise null, and the player's choice shows.</summary>
    private ScoreChoice? SoundingScore => SoundingVariation switch
    {
        true => ScoreChoice.Variation,
        false => OriginalScore,
        null => null,
    };

    /// <summary>While the two loop one after the other, the range of the one sounding, for the roll and the score.</summary>
    private TickSpan? SoundingRange => SoundingVariation is null ? null : Player.Loop;

    /// <summary>Which one sounds, for the selection text.</summary>
    private string? SoundingText => SoundingVariation switch
    {
        true => " " + Text["compare.nowVariation"],
        false => " " + Text["compare.nowOriginal"],
        null => null,
    };

    /// <summary>
    /// Original or Variation (the inspector head and the Explore card): hears it and shows it in the
    /// score, keeping the variation and its steps. A running loop keeps looping, at the same place,
    /// with the new choice; otherwise the passage loops from its start.
    /// </summary>
    private async Task CompareAsync(bool variation)
    {
        if (_selection is not { NoteIds.Count: > 0 } || (variation && _variation is null))
        {
            return;
        }

        _scoreChoice = variation ? ScoreChoice.Variation : OriginalScore;
        _scoreOnOriginal = !variation;
        if (!Looping)
        {
            await AuditionAsync(variation);
            return;
        }

        _looping = true;
        await HearAsync(variation ? [VariationSource] : [Player.OriginalSource]);
        if (Player.State == PlaybackState.Stopped)
        {
            await PlayAsync();
        }
    }

    /// <summary>Whether the Hear select shows this choice: the original, then the variation, while they alternate; else what the player chose.</summary>
    private bool HearShown(string[] ids) => _alternating ? ids is [OneAfterTheOther] : _hear.SequenceEqual(ids);

    /// <summary>A choice in the Hear select: sources to hear, or the original, then the variation.</summary>
    private Task HearOrAlternateAsync(string[] ids) => ids is [OneAfterTheOther] ? StartOneAfterTheOtherAsync() : HearAsync(ids);

    /// <summary>
    /// One after the other: loops the original phrase, then the variation, then again. Pressed again,
    /// the selection keeps looping what Hear said before.
    /// </summary>
    private async Task ToggleOneAfterTheOtherAsync()
    {
        if (!_alternating)
        {
            await StartOneAfterTheOtherAsync();
        }
        else if (_selection is { NoteIds.Count: > 0 } selection)
        {
            _alternating = false;
            await LoopSelectionAsync(selection);
        }
    }

    private async Task StartOneAfterTheOtherAsync()
    {
        if (_selection is not { NoteIds.Count: > 0 } selection || _variation is not { Notes.Count: > 0 })
        {
            return;
        }

        _looping = true;
        _alternating = true;
        await LoopSelectionAsync(selection);
        if (Player.State == PlaybackState.Stopped)
        {
            await PlayAsync();
        }
    }

    /// <summary>
    /// Hands the engine the two passes, from the start of the original's: the selected notes over the
    /// selection, then the variation over the range it takes (<see cref="PlayRange(MotifSelection, string[])"/>).
    /// Returns the original's range.
    /// </summary>
    private async Task<TickSpan> AlternateAsync(MotifSelection selection)
    {
        TickSpan original = PlayRange(selection, [Player.OriginalSource]);
        await Player.AlternateAsync(
        [
            new LoopPass([SelectionSource], original),
            new LoopPass([VariationSource], PlayRange(selection, [VariationSource])),
        ]);
        return original;
    }

    /// <summary>An audition is a choice of its own: the two stop alternating, and after Original the variation brings its score back.</summary>
    private void Auditioned(bool variation)
    {
        _alternating = false;
        if (variation && _scoreOnOriginal)
        {
            _scoreChoice = ScoreChoice.Variation;
            _scoreOnOriginal = false;
        }
    }
}