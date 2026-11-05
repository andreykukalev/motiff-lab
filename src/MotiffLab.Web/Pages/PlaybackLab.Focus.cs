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
/// the variations kept for the phrase are chips above the card that pick the one heard and shown. Previous and next walk
/// the piece at the level shown; Lead-in on the pill plays a bar before and after. Enter with Focus in the contextual menu
/// or a second tap on the selected bracket; leave with Back, a tap on the dimmed page or Escape.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The engine source of the bars around the selection that Lead-in adds.</summary>
    private const string LeadSource = "lead-in";

    /// <summary>Whether the selected phrase is lifted into focus.</summary>
    private bool _focus;

    /// <summary>The passage focus was entered on or stepped to; a kept variation opened on part of it keeps the card on the whole.</summary>
    private TickSpan? _focusSpan;

    /// <summary>Whether play in focus starts a bar early and ends a bar late (0018 section 4, "Hear the transition").</summary>
    private bool _leadIn;

    private PhraseCardSource? _focusCard;
    private object? _focusCardKey;

    /// <summary>Whether the page shows phrase focus: a passage is selected and lifted.</summary>
    private bool Focused => _focus && _selection is not null && _score is not null && _meter is not null;

    /// <summary>What the card shows: the passage focus is on while the selection lies within it, else the selection.</summary>
    private TickSpan? FocusSpan => _selection is { } selection
        ? _focusSpan is { } span && span.Onset <= selection.Span.Onset && selection.Span.End <= span.End ? span : selection.Span
        : null;

    /// <summary>The unit of the level shown that the card is, or holds the card's start.</summary>
    private PhraseUnit? FocusUnit => FocusSpan is { } span && _levels is { } levels ? levels.Matching(_level, span) ?? levels.At(_level, span.Onset) : null;

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
            return _levels is { } levels && levels.Matching(_level, span) is { } unit
                ? Text.Format("focus.position", bars, PhraseLevels.Describe(unit.Level, Words), unit.Number, levels.Units(_level).Count)
                : bars;
        }
    }

    private bool CanStepBack => FocusUnit is { Number: > 1 };

    private bool CanStepOn => FocusUnit is { } unit && unit.Number < _levels!.Units(_level).Count;

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
        null when VariationChosen => "variation",
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
            var key = (span, _notes, variation, version, _readable, name, Text.Culture.Name);
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
                    ? NotationGenerator.Generate(_notes, _meter!, NotationKeys(), end, asHeard: true, bars: bars, ossias: ossias)
                    : NotationGenerator.Generate(_notes, _meter!, NotationKeys(), end, playedTempos: _score.Tempos, pedal: _score.Pedal.Select(held => held.Span), bars: bars, ossias: ossias);
            _focusCard = PhraseCardSource.From(notation, _meter!.GetBar(bars.FirstBar).Span.Onset, _ => variation is not null ? Text.Format("focus.ossia", name!) : name!);
            _focusCardKey = key;
            return _focusCard;
        }
    }

    /// <summary>Lifts the selection into focus.</summary>
    private void EnterFocus()
    {
        if (_selection is null || _penOn)
        {
            return;
        }

        _focus = true;
        _focusSpan = _selection.Span;
        _menuMore = false;
        _popover = Popover.None;
        _pillMenu = PillMenu.None;
    }

    /// <summary>Back to the page, with the selection as it is; a loop that led in plays the selection alone again.</summary>
    private async Task LeaveFocusAsync()
    {
        if (!_focus)
        {
            return;
        }

        _focus = false;
        _focusSpan = null;
        _focusCard = null;
        _focusCardKey = null;
        if (_leadIn && Looping && _selection is { NoteIds.Count: > 0 } selection && _answerLoop is null)
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
    private void MenuFocus() => EnterFocus();

    /// <summary>A kept variation's chip: it is heard and shown as the ossia.</summary>
    private Task ShowKeptVariationAsync(Observation observation) =>
        observation.Id == HeardKeptId ? Task.CompletedTask : OpenObservationAsync(observation);

    /// <summary>Lead-in on the pill: the loop takes in the bar before and the bar after, or lets them go.</summary>
    private async Task ToggleLeadInAsync()
    {
        _leadIn = !_leadIn;
        if (Looping && _selection is { NoteIds.Count: > 0 } selection && _answerLoop is null)
        {
            await LoopSelectionAsync(selection);
        }
    }

    /// <summary>Whether play takes in the bars around the selection now: Lead-in is on and the phrase is in focus.</summary>
    private bool LeadingIn => _leadIn && Focused;

    /// <summary>A range with the bar before and the bar after it while leading in, within the piece.</summary>
    private TickSpan WithLead(TickSpan range)
    {
        if (!LeadingIn || _meter is null || _score is null)
        {
            return range;
        }

        Tick start = range.Onset.Value > 0 ? _meter.BarAt(new Tick(range.Onset.Value - 1)).Span.Onset : range.Onset;
        Tick end = range.End < _score.End ? _meter.BarAt(range.End).Span.End : range.End;
        return TickSpan.FromBounds(start, end > _score.End ? _score.End : end);
    }

    /// <summary>
    /// Gives the engine the notes Lead-in adds: the piece's notes before the selection and after what plays for it,
    /// for the hand or layer the player listens to; none when not leading in.
    /// </summary>
    private async Task SetLeadSourceAsync(MotifSelection selection)
    {
        if (!LeadingIn)
        {
            await Player.SetSourceAsync(LeadSource, _score!.WithNotes([]));
            return;
        }

        Tick after = selection.Span.End;
        if (_variation is { Notes.Count: > 0 } && PlayRange(selection, [VariationSource]).End > after)
        {
            after = PlayRange(selection, [VariationSource]).End;
        }

        await Player.SetSourceAsync(LeadSource, _score!.WithNotes(Voiced([.. _notes!.Where(note => note.Span.Onset < selection.Span.Onset || note.Span.Onset >= after)])));
    }

    /// <summary>The sources to hear with the lead-in's added while leading in.</summary>
    private string[] WithLeadSource(IEnumerable<string> sources) => LeadingIn ? [.. sources, LeadSource] : [.. sources];
}