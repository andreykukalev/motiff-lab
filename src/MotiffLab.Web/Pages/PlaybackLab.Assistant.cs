using System.Globalization;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using MotiffLab.MusicCore.Settings;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The assistant's entry points (0018 section 7; design/kit/docs/01-ux-concept.md, "AI Assistant"): Ask in the contextual
/// menu and the focus bar (the selection or phrase), Ask about the piece in the overflow and in Piece DNA (the whole piece),
/// and ? on the keyboard open it as a popover where it was pressed, over what was there, which stays as it was (testing
/// item 23); the input that closes the Insights stream, and Open in Insights in the popover, open it in the Insights sheet.
/// There is one conversation for the piece, which both show; nothing pops up on its own. It is Pro in full, and nothing is
/// sent anywhere yet.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>Whether the Insights sheet holds the assistant rather than the stream.</summary>
    private bool _asking;

    /// <summary>Whether the assistant looks at the whole piece rather than the selection.</summary>
    private bool _askWholePiece;

    /// <summary>The Piece DNA map in words, when Ask about the piece came from it, as part of what it knows.</summary>
    private string? _askMap;

    /// <summary>Where the assistant's popover is anchored, in the viewport's pixels; null for none, and (0, 0) for the middle.</summary>
    private (double X, double Y)? _askAt;

    /// <summary>The questions asked about this piece, in order: one conversation, the popover and the sheet windows onto it.</summary>
    private readonly List<string> _conversation = [];

    /// <summary>The popover at the place Ask was pressed: the selection's menu, the focus bar, Piece DNA, More or the keyboard.</summary>
    private void OpenAskPopover(MouseEventArgs? at, bool wholePiece)
    {
        _popover = Popover.None;
        _menuMore = false;
        _askWholePiece = wholePiece || _selection is null;
        _askMap = wholePiece && DnaShown && _dna is { } dna ? Text.Format("ask.knowsMap", Text.Count("dna.ideas", dna.Ideas.Count)) : null;
        _askAt = at is { ClientX: > 0 } or { ClientY: > 0 } ? (at.ClientX, at.ClientY) : (0, 0);
    }

    private void CloseAskPopover() => _askAt = null;

    private void AskPopoverKey(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseAskPopover();
        }
    }

    /// <summary>Whether the assistant is open, as the popover or in the sheet, so Ask shows as pressed.</summary>
    private bool AskOpen => _askAt is not null || (_insightsOpen && _asking);

    /// <summary>The popover's anchor for CSS; the middle of the top of the screen when it came from the keyboard.</summary>
    private string AskPlace => _askAt is { } at && (at.X > 0 || at.Y > 0)
        ? string.Create(CultureInfo.InvariantCulture, $"--ax: {at.X:0}px; --ay: {at.Y:0}px")
        : "--ax: 50vw; --ay: 30vh";

    /// <summary>Open in Insights from the popover: the same conversation goes on in the sheet; Piece DNA waits under it.</summary>
    private async Task AskInInsightsAsync()
    {
        string? map = _askMap;
        bool whole = _askWholePiece;
        bool fromDna = DnaShown;
        _askAt = null;
        if (fromDna)
        {
            await StopDnaAsync();
        }

        OpenAsk(whole);
        _askMap = map;
        _dnaAside = fromDna;
        _cameFrom = fromDna ? (Place.Ask, Origin.Dna) : null;
    }

    private void Asked(string question) => _conversation.Add(question);

    /// <summary>
    /// Three questions to ask here: about the selection, its harmony, how to practise it and where it returns; about the
    /// whole piece, how a section that comes back differs from the first, where to start and where it peaks.
    /// </summary>
    private IReadOnlyList<string> AskSuggestions
    {
        get
        {
            if (!_askWholePiece && _selection is not null)
            {
                return [Text["ask.suggest.harmony"], Text["ask.suggest.practise"], Text["ask.suggest.returns"]];
            }

            var suggestions = new List<string>();
            if (_dna is { Form: { Count: > 1 } form } && form.FirstOrDefault(section => section.Label.Length > 1) is { } back
                && form.FirstOrDefault(section => back.SharesWith.Contains(section.Id)) is { } first)
            {
                suggestions.Add(Text.Format("ask.suggest.compare", back.Label, first.Label));
            }

            suggestions.Add(Text["ask.suggest.start"]);
            suggestions.Add(Text["ask.suggest.climax"]);
            if (suggestions.Count < 3)
            {
                suggestions.Add(Text["ask.suggest.form"]);
            }

            return suggestions;
        }
    }

    private void OpenAsk(bool wholePiece)
    {
        _askAt = null;
        _insightsOpen = true;
        _contentsOpen = false;
        _checkOpen = false;
        _performanceOpen = false;
        _popover = Popover.None;
        _menuMore = false;
        _asking = true;
        _askWholePiece = wholePiece || _selection is null;
        _askMap = null;
    }

    /// <summary>The Insights stream's input: the assistant in the sheet, as the stream's last voice.</summary>
    private void MenuAsk() => OpenAsk(wholePiece: false);

    /// <summary>Ask in the selection's menu or the focus bar: the popover there, on the selection.</summary>
    private void AskHere(MouseEventArgs at) => OpenAskPopover(at, wholePiece: false);

    /// <summary>Ask about the piece in More: the popover where More was.</summary>
    private void AskAboutPiece(MouseEventArgs at) => OpenAskPopover(at, wholePiece: true);

    /// <summary>Ask about the piece from Piece DNA: the popover at the button, the map open under it with what it showed going with the question.</summary>
    private void AskFromDna(MouseEventArgs at) => OpenAskPopover(at, wholePiece: true);

    /// <summary>‹ in the assistant: back where it was opened from, or to the Insights stream it is the last voice of.</summary>
    private Task AskBackAsync()
    {
        if (CameFrom(Place.Ask) is not null)
        {
            return GoBackAsync();
        }

        _asking = false;
        return DropDnaAsideAsync();
    }

    private void SetAskWholePiece(bool wholePiece) => _askWholePiece = wholePiece || _selection is null;

    /// <summary>? from the keyboard (Scripts/phrases/phrase-keys.ts): the assistant on the selection.</summary>
    [JSInvokable]
    public void AskFromKey()
    {
        if (_score is not null && !_penOn && _bookmarkDraft is null && _noteDraft is null && !DnaShown)
        {
            OpenAskPopover(null, wholePiece: false);
            StateHasChanged();
        }
    }

    /// <summary>The scope chip for the selection: "This phrase · bars 9–16" or "This selection · bar 3".</summary>
    private string? AskSelectionLabel => _selection is { } selection
        ? Text.Format(CurrentPhrase is not null ? "ask.thisPhrase" : "ask.thisSelection", BarsOf(selection.Span))
        : null;

    /// <summary>
    /// What the assistant would be given, in words: the piece, the passage and its facts, what is being practised, what
    /// was kept with the piece, and the level the settings are at.
    /// </summary>
    private IReadOnlyList<string> AskKnows
    {
        get
        {
            var items = new List<string> { Text.Format("ask.knowsPiece", BarTitle) };
            if (_askMap is { } map)
            {
                items.Add(map);
            }

            if (!_askWholePiece && _selection is { } selection)
            {
                items.Add(Text.Format("ask.knowsPassage", BarsOf(selection.Span), Text.Count("ask.facts", Facts.Count)));
            }

            string practice = Text.Format("ask.knowsPractice", TempoPercent, FilterLabel(_filter).ToLowerInvariant());
            items.Add(_looping ? practice + " · " + Text["ask.knowsLooping"] : practice);

            string[] kept =
            [
                .. new[] { ("ask.ideas", Kept.Count), ("ask.notes", NotesInOrder.Count), ("ask.bookmarks", Bookmarks.Count) }
                    .Where(count => count.Item2 > 0)
                    .Select(count => Text.Count(count.Item1, count.Item2)),
            ];
            if (kept.Length > 0)
            {
                items.Add(Text.Format("ask.knowsKept", string.Join(" · ", kept)));
            }

            Preset preset = SettingsStore.State.Preset;
            items.Add(Text.Format("ask.knowsLevel", preset == Preset.Custom ? Text["ask.presetCustom"] : Text[$"settings.preset.{preset.ToString().ToLowerInvariant()}"]));
            return items;
        }
    }
}