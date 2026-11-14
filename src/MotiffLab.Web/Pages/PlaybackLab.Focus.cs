using Microsoft.JSInterop;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;
using MotiffLab.Web.Playback;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Phrase focus (decision record 0018, section 4; design/kit/docs/01-ux-concept.md, Phrase focus): a reading lamp on one
/// phrase. The phrase lifts into a card across the screen with the page dimmed behind it; the focus bar carries Back, where
/// the phrase is, Original / Variation / Alternate when there is a variation and Reading / As played for a transcription.
/// A variation is engraved as an ossia above the original, bar for bar, from music-core's excerpt (<see cref="NotationGenerator"/>);
/// the variations kept for the phrase are chips above the card that pick the one heard and shown. Expand is a focused
/// practice view bound to the exact selection it was opened on (#521 19): nothing in it steps to another phrase or changes
/// the selection, and Play loops the passage, from the playhead inside it or from its start, with no Loop to turn off; the
/// roll's playhead is taken and dragged inside the passage, heard, and playback goes on from where it is let go (#533 2).
/// Enter with Focus in the contextual menu (only with a selection); leave with Back, a tap on the dimmed page or Escape, and
/// the loop is as it was before.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>Whether the selected phrase is lifted into focus.</summary>
    private bool _focus;

    /// <summary>The passage focus was entered on or stepped to; a kept variation opened on part of it keeps the card on the whole.</summary>
    private TickSpan? _focusSpan;

    private PhraseCardSource? _focusCard;
    private object? _focusCardKey;

    /// <summary>Whether the page shows phrase focus: a passage is selected and lifted.</summary>
    private bool Focused => _focus && _selection is not null && _score is not null && _meter is not null;

    /// <summary>What the card shows: the passage focus is on while the selection lies within it, else the selection.</summary>
    private TickSpan? FocusSpan => _selection is { } selection
        ? _focusSpan is { } span && span.Onset <= selection.Span.Onset && selection.Span.End <= span.End ? span : selection.Span
        : null;

    /// <summary>The unit of the level shown that the card is, or holds the card's start.</summary>
    private PhraseUnit? FocusUnit => FocusSpan is { } span && Levels is { } levels ? levels.Matching(_level, span) ?? levels.At(_level, span.Onset) : null;

    /// <summary>"bars 9–16 · phrase 3 of 16", or the bars alone for a passage that is not a unit.</summary>
    private string FocusPosition
    {
        get
        {
            if (FocusSpan is not { } span || _meter is null)
            {
                return "";
            }

            string bars = BarsOf(span);
            return Levels is { } levels && levels.Matching(_level, span) is { } unit
                ? Text.Format("focus.position", bars, PhraseLevels.Describe(unit.Level, Words), unit.Number, levels.Units(_level).Count)
                : bars;
        }
    }

    /// <summary>Whether the passage has a variation to hear: Original / Variation / Alternate shows only then (0018 section 7).</summary>
    private bool FocusHasVariation => _variation is { Notes.Count: > 0 };

    /// <summary>The variations kept for the passage in focus, as chips above the card.</summary>
    private IReadOnlyList<Observation> FocusVariations => FocusSpan is { } span
        ? [.. Kept.Where(observation => observation.Variation is not null && observation.IsAbout(span))]
        : [];

    /// <summary>The name of the variation heard: a kept one's, the experiment's, or "Variation".</summary>
    private string VariationName =>
        HeardKeptId is { } id && Kept.FirstOrDefault(observation => observation.Id == id) is { } kept ? kept.Name
        : ActiveTrial is { } trial ? TrialName(trial)
        : Text["focus.variationName"];

    /// <summary>What the card lights up as it sounds: the original's staves, the ossia's, or both.</summary>
    private string FocusSounding => SoundingVariation switch
    {
        true => "variation",
        false => "original",
        null when VariationChosen && (_hear.Contains(Player.OriginalSource) || _hear.Contains(InPlaceSource)) => "both",
        null when VariationChosen || VersionChosen => "variation",
        _ => "original",
    };

    /// <summary>
    /// The card's engraving: the passage's bars on their own with the variation as an ossia above when it plays there,
    /// written for reading or as played like the page. Engraved again only when one of those changes.
    /// </summary>
    private PhraseCardSource? FocusCard
    {
        get
        {
            if (!Focused || FocusSpan is not { } span || _notes is null)
            {
                return null;
            }

            Variation? variation = _variation is { Notes.Count: > 0 } heard && heard.Notes.Any(note => note.Span.Overlaps(span)) ? heard : null;
            // Without a variation, another version of the piece stands above as the performer has it (0022, section 5).
            IReadOnlyList<Note>? version = variation is null ? VersionNotes : null;
            string? name = variation is not null ? VariationName : version is not null ? Text.Format("focus.version", ShownPerformance!.Name) : null;
            var key = (span, _notes, variation, version, _readable, SettingsStore.IsOn(ReadingMarksSetting), name, Text.Culture.Name);
            if (_focusCard is not null && Equals(key, _focusCardKey))
            {
                return _focusCard;
            }

            // The card names the ossia in its overline: Verovio's label at its left would only push the staves right.
            NotationOssia[] ossias = variation is not null ? [new NotationOssia("", variation.Notes, AsHeard: _heard && _readable)]
                : version is not null ? [new NotationOssia("", version, AsHeard: true)]
                : [];
            Tick end = variation is null ? _score!.End : new Tick(Math.Max(_score!.End.Value, variation.Notes.Max(note => note.Span.End.Value)));
            BarRange bars = BarRange.Of(_meter!, span);
            GeneratedNotation notation = !_heard
                ? NotationGenerator.Generate(_notes, _meter!, NotationKeys(), end, bars: bars, ossias: ossias)
                : _readable
                    ? ReadableNotation(_notes, _meter!, NotationKeys(), end, null, _score, bars, ossias)
                    : NotationGenerator.Generate(_notes, _meter!, NotationKeys(), end, playedTempos: _score.Tempos, pedal: _score.Pedal.Select(held => held.Span), bars: bars, ossias: ossias);
            _focusCard = PhraseCardSource.From(notation, _meter!.GetBar(bars.FirstBar).Span.Onset, _ => variation is not null ? Text.Format("focus.ossia", name!) : name!);
            _focusCardKey = key;
            return _focusCard;
        }
    }

    /// <summary>Whether the selection looped before Expand, so that the way back leaves the loop as it was (#521 17).</summary>
    private bool _loopingBeforeFocus;

    /// <summary>
    /// Lifts the selection into focus, from the score or (<paramref name="fromRoll"/>) from the roll, bound to it (#521 19):
    /// playing, the music plays on round the passage, from its start when it was elsewhere (#533 2).
    /// </summary>
    private async Task EnterFocusAsync(bool fromRoll = false)
    {
        if (_selection is null || _penOn || _focus)
        {
            return;
        }

        _focus = true;
        _focusFromRoll = fromRoll;
        _focusSpan = _selection.Span;
        _menuMore = false;
        _popover = Popover.None;
        _pillMenu = PillMenu.None;
        _loopingBeforeFocus = _looping;
        if (!Focused)
        {
            return;
        }

        // The playback model lets go in focus (v0.5.2): the passage is the loop here (#533 2).
        await SyncModelAsync();
        await KeepWithinPassageAsync();
    }

    /// <summary>The passage as what plays (#533 2): round and round, from the playhead while it is inside, else from its start.</summary>
    private async Task KeepWithinPassageAsync()
    {
        if (_answerLoop is not null || _selection is not { } selection)
        {
            return;
        }

        if (selection.NoteIds.Count > 0)
        {
            _looping = true;
            await LoopSelectionAsync(selection);
            return;
        }

        TickSpan range = selection.Span;
        await Player.SetLoopAsync(range);
        if (!range.Contains(Player.Position))
        {
            await Player.SeekAsync(range.Onset);
        }
    }

    /// <summary>
    /// Play in Expand (#533 2): the passage round and round, from the playhead inside it or from its start; while it plays, a
    /// pause.
    /// </summary>
    private async Task PlayPassageAsync()
    {
        if (Player.State != PlaybackState.Stopped || Transposed is not null)
        {
            await TogglePlayAsync();
            return;
        }

        await KeepWithinPassageAsync();
        await PlayAsync();
    }

    /// <summary>The pill's Play: in Expand the passage in a loop, elsewhere play or pause.</summary>
    private Task PillPlayAsync() => Focused ? PlayPassageAsync() : TogglePlayAsync();

    /// <summary>Back to the page, with the selection as it is; a loop that led in plays the selection alone again.</summary>
    private async Task LeaveFocusAsync()
    {
        if (!_focus)
        {
            return;
        }

        _focus = false;
        _focusFromRoll = false;
        _focusSpan = null;
        _focusCard = null;
        _focusCardKey = null;
        ReopenRollAfterFocus();
        // The other version is heard only in focus: the page goes back to the notes.
        bool versionHeard = VersionChosen || (_alternating && _variation is not { Notes.Count: > 0 });
        if (versionHeard)
        {
            _alternating = false;
            _hear = [Player.OriginalSource];
        }

        if (!_loopingBeforeFocus && _answerLoop is null && !_scaleHeard && (_looping || Player.Loop is not null))
        {
            // Expand's passage looped: the loop that was not there before goes (#521 17).
            _looping = false;
            _alternating = false;
            await Player.SetLoopAsync(null);
            await Player.SelectSourcesAsync(_hear);
        }

        // The playback model holds again out of focus (v0.5.2): it takes the selection and loops it as Цикл says.
        await SyncModelAsync();
        if (_looping && _selection is { NoteIds.Count: > 0 } selection && _answerLoop is null)
        {
            await LoopSelectionAsync(selection);
        }
    }

    /// <summary>Escape, from the keyboard (Scripts/phrases/phrase-keys.ts).</summary>
    [JSInvokable("LeaveFocusAsync")]
    public async Task LeaveFocusFromKeyAsync()
    {
        if (_focus)
        {
            await LeaveFocusAsync();
            StateHasChanged();
        }
    }

    /// <summary>The menu's Focus.</summary>
    private Task MenuFocusAsync() => EnterFocusAsync();

    /// <summary>A kept variation's chip: it is heard and shown as the ossia.</summary>
    private Task ShowKeptVariationAsync(Observation observation) =>
        observation.Id == HeardKeptId ? Task.CompletedTask : OpenObservationAsync(observation);
}