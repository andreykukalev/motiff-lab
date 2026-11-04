using Microsoft.JSInterop;

using MotiffLab.MusicCore.Settings;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The assistant's entry points (0018 section 7; design/kit/docs/01-ux-concept.md, "AI Assistant"): Ask in the contextual
/// menu and the focus bar (the selection or phrase), Ask about the piece in the overflow and in Piece DNA (the whole piece),
/// the input that closes the Insights stream (whatever is selected), and ? on the keyboard. Each opens the assistant in
/// the Insights sheet; nothing pops up on its own. It is Pro in full, and nothing is sent anywhere yet.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>Whether the Insights sheet holds the assistant rather than the stream.</summary>
    private bool _asking;

    /// <summary>Whether the assistant looks at the whole piece rather than the selection.</summary>
    private bool _askWholePiece;

    /// <summary>The Piece DNA map in words, when Ask about the piece came from it, as part of what it knows.</summary>
    private string? _askMap;

    private void OpenAsk(bool wholePiece)
    {
        _insightsOpen = true;
        _contentsOpen = false;
        _checkOpen = false;
        _popover = Popover.None;
        _menuMore = false;
        _asking = true;
        _askWholePiece = wholePiece || _selection is null;
        _askMap = null;
    }

    private void MenuAsk() => OpenAsk(wholePiece: false);

    private void AskAboutPiece() => OpenAsk(wholePiece: true);

    /// <summary>Ask about the piece from Piece DNA: the map is left, and what it showed goes with the question.</summary>
    private async Task AskFromDnaAsync()
    {
        string? map = _dna is { } dna ? Text.Format("ask.knowsMap", Text.Count("dna.ideas", dna.Ideas.Count)) : null;
        await CloseDnaAsync();
        OpenAsk(wholePiece: true);
        _askMap = map;
    }

    private void LeaveAsk() => _asking = false;

    private void SetAskWholePiece(bool wholePiece) => _askWholePiece = wholePiece || _selection is null;

    /// <summary>? from the keyboard (Scripts/phrases/phrase-keys.ts): the assistant on the selection.</summary>
    [JSInvokable]
    public void AskFromKey()
    {
        if (_score is not null && !_penOn && _bookmarkDraft is null && _noteDraft is null && _dna is null)
        {
            OpenAsk(wholePiece: false);
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