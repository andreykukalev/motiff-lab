using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Settings;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Margin notes, highlights and the layers of marks (0018 section 5; design/kit/docs/01-ux-concept.md, "Annotations"):
/// Note in the contextual menu writes the player's words about the selected bars, shown as a numbered amber marker
/// beside the first bar; a tap on the marker opens the note again. Highlight in the menu's overflow lays a translucent
/// band in an ink behind the staves. The layers (ink, notes, bookmarks, analysis, check) can each be hidden for a clean page,
/// from the pen palette or the overflow, and are kept with the piece. The Contents drawer's Notes tab lists them all.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The note being written over the selection: a new one, or the one <see cref="NoteDraft.Id"/> names.</summary>
    private sealed record NoteDraft(string? Id, TickSpan Span, string Text);

    /// <summary>The player's own marks and the phrases, as the settings "layers.ink" and the rest name them: shown as before.</summary>
    private static readonly string[] s_layers = ["ink", "notes", "bookmarks", "analysis"];

    /// <summary>
    /// What the app adds over the notes (testing item 33): the markers of a version's differences ("layers.performance"), the line
    /// under the bars taken from a version, and the Check flags. All hidden while "layers.extra" (ⓘ) is off, its default.
    /// </summary>
    private static readonly string[] s_extra = ["performance", "taken", "check"];

    /// <summary>The inks a highlight is laid in: the amber highlighter first, then the pencil inks.</summary>
    private static readonly InkColour[] s_highlightInks = [InkColour.Amber, InkColour.Carmine, InkColour.Forest, InkColour.Ochre];

    private NoteDraft? _noteDraft;
    private ElementReference _noteText;
    private bool _focusNote;

    /// <summary>The layers shown or hidden while the piece has no project; with one, the project keeps them.</summary>
    private readonly Dictionary<string, bool> _sessionLayers = [];

    private (string Key, IReadOnlyList<string> Hidden)? _hiddenLayers;
    private (IReadOnlyList<MarginNote> From, string Language, IReadOnlyList<ScoreMarginNote> Drawn)? _scoreNotes;
    private (IReadOnlyList<Highlight> From, IReadOnlyList<ScoreHighlightMark> Drawn)? _scoreHighlights;

    /// <summary>The colour the Notes tab lists highlights in; null lists every colour.</summary>
    private InkColour? _highlightFilter;

    private Marks CurrentMarks => (_project ?? _session)?.Marks ?? Marks.Empty;

    /// <summary>The margin notes in score order: their numbers beside the bars count in this order.</summary>
    private IReadOnlyList<MarginNote> NotesInOrder => [.. CurrentMarks.Notes.OrderBy(note => note.Span.Onset).ThenBy(note => note.CreatedAt)];

    private int NumberOf(MarginNote note) => NotesInOrder.ToList().FindIndex(kept => kept.Id == note.Id) + 1;

    /// <summary>"The leap needs a lighter thumb…": the note's first words, for its marker and the Notes tab.</summary>
    private static string FirstWords(string text, int length = 48) => text.Length <= length ? text : string.Concat(text.AsSpan(0, length).TrimEnd(), "…");

    private IReadOnlyList<ScoreMarginNote>? ScoreNotes
    {
        get
        {
            if (_meter is null)
            {
                return null;
            }

            IReadOnlyList<MarginNote> notes = CurrentMarks.Notes;
            if (_scoreNotes is { } cached && ReferenceEquals(cached.From, notes) && cached.Language == Text.Language)
            {
                return cached.Drawn;
            }

            IReadOnlyList<MarginNote> ordered = NotesInOrder;
            IReadOnlyList<ScoreMarginNote> drawn = [.. ordered.Select((note, index) => new ScoreMarginNote(
                note.Id, note.Span.Onset.Value, index + 1, Text.Format("margin.markerLabel", index + 1, BarsOf(note.Span), FirstWords(note.Text))))];
            _scoreNotes = (notes, Text.Language, drawn);
            return drawn;
        }
    }

    private IReadOnlyList<ScoreHighlightMark>? ScoreHighlights
    {
        get
        {
            IReadOnlyList<Highlight> highlights = CurrentMarks.Highlights;
            if (_scoreHighlights is { } cached && ReferenceEquals(cached.From, highlights))
            {
                return cached.Drawn;
            }

            IReadOnlyList<ScoreHighlightMark> drawn = [.. highlights.Select(highlight => new ScoreHighlightMark(
                highlight.Id, highlight.Span.Onset.Value, highlight.Span.End.Value, highlight.Colour.ToString().ToLowerInvariant()))];
            _scoreHighlights = (highlights, drawn);
            return drawn;
        }
    }

    /// <summary>Note in the menu: a card over the selection to write in.</summary>
    private void StartNote()
    {
        _menuMore = false;
        _bookmarkDraft = null;
        if (_selection is { } selection)
        {
            _noteDraft = new NoteDraft(null, selection.Span, "");
            _focusNote = true;
        }
    }

    /// <summary>A marker tapped, or a note chosen in the Notes tab: its bars are selected and the note opens to read or change.</summary>
    private async Task OpenNoteAsync(string id)
    {
        if (CurrentMarks.Notes.FirstOrDefault(note => note.Id == id) is not { } note)
        {
            return;
        }

        if (_selection?.Span != note.Span)
        {
            await SelectAsync(note.Span, note: null);
        }

        _bookmarkDraft = null;
        _noteDraft = new NoteDraft(note.Id, note.Span, note.Text);
        _focusNote = true;
    }

    private async Task FocusNoteAsync()
    {
        if (!_focusNote || _noteDraft is null)
        {
            return;
        }

        _focusNote = false;
        try
        {
            await _noteText.FocusAsync();
        }
        catch (InvalidOperationException)
        {
            // The card is not on the page yet.
        }
    }

    private void SetNoteText(ChangeEventArgs change)
    {
        if (_noteDraft is { } draft)
        {
            _noteDraft = draft with { Text = change.Value as string ?? "" };
        }
    }

    private void CancelNote() => _noteDraft = null;

    private void OnNoteKey(KeyboardEventArgs key)
    {
        if (key.Key == "Escape")
        {
            CancelNote();
        }
    }

    /// <summary>Done on the note card: the words kept, changed, or, when they were all taken out, the note deleted.</summary>
    private async Task SaveNoteAsync()
    {
        if (_noteDraft is not { } draft)
        {
            return;
        }

        _noteDraft = null;
        string text = draft.Text.Trim();
        if (draft.Id is { } id)
        {
            await (text.Length == 0
                ? SaveMarksAsync(project => project.ChangeMarks(marks => marks.RemoveNote(id)), Text["margin.deleted"])
                : SaveMarksAsync(project => project.ChangeMarks(marks => marks.ChangeNote(id, text)), Text["margin.changed"]));
            return;
        }

        if (text.Length > 0)
        {
            await SaveMarksAsync(
                project => project.ChangeMarks(marks => marks.AddNote(new MarginNote(marks.NextNoteId(), draft.Span, text, DateTimeOffset.Now))),
                Text.Format("margin.made", BarsOf(draft.Span)));
        }
    }

    private Task DeleteNoteAsync(string id)
    {
        _noteDraft = null;
        return SaveMarksAsync(project => project.ChangeMarks(marks => marks.RemoveNote(id)), Text["margin.deleted"]);
    }

    /// <summary>The highlight lying exactly over the selection, which the menu offers to take away.</summary>
    private Highlight? SelectedHighlight => _selection is { } selection ? CurrentMarks.Highlights.LastOrDefault(highlight => highlight.Span == selection.Span) : null;

    /// <summary>Highlight in the menu's overflow: the selected bars in an ink, in place of a highlight already there.</summary>
    private Task HighlightAsync(InkColour colour)
    {
        _menuMore = false;
        if (_selection is not { } selection)
        {
            return Task.CompletedTask;
        }

        string? replaced = SelectedHighlight?.Id;
        return SaveMarksAsync(
            project => project.ChangeMarks(marks =>
            {
                Marks without = replaced is null ? marks : marks.RemoveHighlight(replaced);
                return without.AddHighlight(new Highlight(without.NextHighlightId(), selection.Span, [], colour));
            }),
            Text.Format("highlights.made", BarsOf(selection.Span)));
    }

    private Task RemoveHighlightAsync(string id)
    {
        _menuMore = false;
        return SaveMarksAsync(project => project.ChangeMarks(marks => marks.RemoveHighlight(id)), Text["highlights.removed"]);
    }

    private string InkName(InkColour colour) => Text[colour switch
    {
        InkColour.Amber => "ink.amber",
        InkColour.Carmine => "ink.carmine",
        InkColour.Forest => "ink.forest",
        InkColour.Ochre => "ink.ochre",
        _ => "ink.graphite",
    }];

    /// <summary>Whether a layer shows: the project's choice, or the one made while the piece has no project, or the setting's default.</summary>
    private bool LayerShown(string layer)
    {
        string id = $"layers.{layer}";
        if (_project?.SettingOverrides.TryGetValue(id, out string? value) == true)
        {
            return value != Setting.Off;
        }

        return _project is null && _sessionLayers.TryGetValue(layer, out bool shown) ? shown : SettingsStore[id] != Setting.Off;
    }

    /// <summary>
    /// The layers hidden, for the score; the same list until the choice changes. Check flags are extra information, shown with
    /// ⓘ, and always while the Check drawer is open, as its bars' places on the page.
    /// </summary>
    private IReadOnlyList<string> HiddenLayers
    {
        get
        {
            string[] hidden = [.. s_layers.Where(layer => !LayerShown(layer)), .. ExtraShown("check") || _checkOpen ? Array.Empty<string>() : ["check"]];
            string key = string.Join(',', hidden);
            if (_hiddenLayers is not { } cached || cached.Key != key)
            {
                _hiddenLayers = (key, hidden);
            }

            return _hiddenLayers.Value.Hidden;
        }
    }

    /// <summary>Whether one kind of extra information shows: ⓘ on, and that kind on in its list.</summary>
    private bool ExtraShown(string item) => LayerShown("extra") && LayerShown(item);

    /// <summary>The kinds of extra information the piece has: a version's differences and taken bars only with a version, Check only for a scan or a transcription.</summary>
    private IEnumerable<string> PieceExtra => s_extra.Where(item => item switch
    {
        "performance" => HasPerformances,
        "taken" => _project is { } project && project.TakenBars.Any(),
        "check" => HasCheckFlags,
        _ => true,
    });

    /// <summary>The player's layers, in the lower half of the list.</summary>
    private IEnumerable<string> PieceLayers => s_layers;

    /// <summary>The layers of marks, which Hide all marks hides and shows.</summary>
    private IEnumerable<string> MarkLayers => PieceLayers;

    private bool AllMarksHidden => MarkLayers.All(layer => !LayerShown(layer));

    /// <summary>ⓘ: everything the app adds over the notes shows, or hides again.</summary>
    private Task ToggleExtraAsync() => SetLayersAsync(["extra"], !LayerShown("extra"));

    private Task ToggleLayerAsync(string layer) => SetLayersAsync([layer], !LayerShown(layer));

    /// <summary>Hide all marks, or show them all again.</summary>
    private Task ToggleAllMarksAsync() => SetLayersAsync(MarkLayers, AllMarksHidden);

    private async Task SetLayersAsync(IEnumerable<string> layers, bool shown)
    {
        if (_project is null)
        {
            foreach (string layer in layers)
            {
                _sessionLayers[layer] = shown;
            }

            return;
        }

        if (!PieceScoped(LayerIds))
        {
            // Everywhere: the Settings sheet's layers change, and every piece without its own follows them.
            foreach (string layer in layers)
            {
                await SettingsStore.SetAsync($"layers.{layer}", shown);
            }

            return;
        }

        Project project = _project;
        foreach (string layer in layers)
        {
            project = project.OverrideSetting($"layers.{layer}", shown ? Setting.On : Setting.Off);
        }

        await SaveMarksAsync(_ => project, "");
    }

    private string LayerName(string layer) => Text[$"layers.{layer}"];

    /// <summary>The settings of the layers the piece has, for the popover's scope.</summary>
    private IReadOnlyList<string> LayerIds => ["layers.extra", .. PieceExtra.Select(item => $"layers.{item}"), .. PieceLayers.Select(layer => $"layers.{layer}")];

    private void OpenLayers()
    {
        _popover = Popover.Layers;
        _pillMenu = PillMenu.None;
    }

    /// <summary>The Notes tab's ink: the runs of bars that carry strokes, "Ink, bars 9–12".</summary>
    private IReadOnlyList<TickSpan> InkRuns
    {
        get
        {
            if (_meter is null || _score is null || CurrentMarks.Ink.Count == 0)
            {
                return [];
            }

            int[] bars = [.. CurrentMarks.Ink.Select(stroke => _meter.BarAt(stroke.Bar).Number).Distinct().Order()];
            List<TickSpan> runs = [];
            int first = bars[0];
            int last = bars[0];
            foreach (int bar in bars.Skip(1).Append(int.MaxValue))
            {
                if (bar == last + 1)
                {
                    last = bar;
                    continue;
                }

                runs.Add(MotifSelector.Bars(_meter, first, last, _score.End));
                first = last = bar;
            }

            return runs;
        }
    }

    private IEnumerable<Highlight> HighlightsListed =>
        CurrentMarks.Highlights.Where(highlight => _highlightFilter is null || highlight.Colour == _highlightFilter).OrderBy(highlight => highlight.Span.Onset);

    private bool HasMarks => CurrentMarks.Notes.Count + CurrentMarks.Highlights.Count + CurrentMarks.Ink.Count > 0;

    private void FilterHighlights(InkColour? colour) => _highlightFilter = _highlightFilter == colour ? null : colour;

    /// <summary>An entry in the Notes tab: the score goes there and selects the bars.</summary>
    private Task GoToMarkAsync(TickSpan span)
    {
        _bookmarkDraft = null;
        _noteDraft = null;
        return SelectAsync(span, note: null);
    }
}