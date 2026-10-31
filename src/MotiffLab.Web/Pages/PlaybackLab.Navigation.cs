using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Back to the wider context (#126, step 7 of docs/08-central-product-model.md): the selection run on into
/// a neighbouring phrase to hear the transition, and the piece played on from the phrase, or from the
/// start, with a way back to the phrase as it was. Previous and next phrase are in the phrase strip.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>While the piece plays on from the phrase, whether the phrase was looping before; null otherwise.</summary>
    private bool? _playOnLooped;

    /// <summary>Whether the piece plays from its start rather than from the phrase.</summary>
    private bool _playOnWhole;

    /// <summary>The selection run on into the phrase before (-1) or after (1) it, or null.</summary>
    private PhraseExtension? Extension(int direction) =>
        _phrases is { } map && _selection is { } selection ? map.ExtendInto(selection.Span, direction) : null;

    /// <summary>Selects the selection with its neighbouring phrase; the loop, the tempo and what is heard carry on.</summary>
    private Task ExtendSelectionAsync(int direction)
    {
        if (Extension(direction) is not { } extension)
        {
            return Task.CompletedTask;
        }

        string? note = extension.Shortened
            ? Text.Format("selection.extendCapped", MotifSelection.MaxBars, _meter!.BarAt(direction > 0 ? new Tick(extension.Span.End.Value - 1) : extension.Span.Onset).Number)
            : null;
        return SelectAsync(extension.Span, note);
    }

    /// <summary>
    /// Plays on from the start of the phrase to the end of the piece, or the whole piece from its start,
    /// with every note; the selection stays, so the roll and the score keep showing where the phrase is.
    /// </summary>
    private async Task PlayOnAsync(bool wholePiece)
    {
        if (_selection is not { } selection)
        {
            return;
        }

        _playOnLooped ??= Looping;
        _playOnWhole = wholePiece;
        if (Looping)
        {
            await StopLoopingAsync();
        }

        await Player.SeekAsync(wholePiece ? Tick.Zero : selection.Span.Onset);
        if (Player.State == PlaybackState.Stopped)
        {
            await PlayAsync();
        }
    }

    /// <summary>Stops playing on and goes back to the start of the phrase, looping it again when it looped before.</summary>
    private async Task BackToPhraseAsync()
    {
        bool loop = _playOnLooped == true;
        _playOnLooped = null;
        await Player.PauseAsync();
        if (_selection is not { } selection)
        {
            return;
        }

        if (loop && selection.NoteIds.Count > 0)
        {
            _looping = true;
            TickSpan range = await LoopSelectionAsync(selection);
            await Player.SeekAsync(range.Onset);
        }
        else
        {
            await Player.SeekAsync(selection.Span.Onset);
        }
    }

    /// <summary>What plays while the piece plays on, for the selection text; null otherwise.</summary>
    private string? PlayOnText => _playOnLooped is null
        ? null
        : Text[_playOnWhole ? "selection.playingWhole" : "selection.playingOn"];

    /// <summary>"phrases 1–2, bars 0–8" when the selection runs exactly from one phrase start to a later phrase's end; otherwise null.</summary>
    private string? SpannedPhrases(MotifSelection selection)
    {
        if (_phrases is not { } map
            || map.Phrases.FirstOrDefault(phrase => phrase.Span.Onset == selection.Span.Onset) is not { } first
            || map.Phrases.FirstOrDefault(phrase => phrase.Span.End == selection.Span.End) is not { } last
            || last.Number <= first.Number)
        {
            return null;
        }

        return first.FirstBar == last.LastBar
            ? Text.Format("selection.phrasesBar", first.Number, last.Number, first.FirstBar)
            : Text.Format("selection.phrasesBars", first.Number, last.Number, first.FirstBar, last.LastBar);
    }
}