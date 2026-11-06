using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Bookmarks and the Contents drawer (0018 section 5; design/kit/docs/01-ux-concept.md, "Bookmarks"): a passage the
/// player has named and filed in a category, drawn as a bracket in the category's ink above the phrase brackets and
/// listed in the drawer as the piece's own contents. Bookmark in the menu, or B, opens a small card over the
/// selection with a title pre-filled from the bars and the category chips; the drawer opens from the piece's title in
/// the bar or the overflow, with the Contents and the Pages. Bookmarks belong to the piece: with a project they are
/// saved with it; without one they last while the page is open, as the ideas do.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The bookmark being made from the menu, or being changed in the drawer (<see cref="BookmarkDraft.Id"/>).</summary>
    private sealed record BookmarkDraft(string? Id, string Title, string CategoryId);

    private bool _contentsOpen;
    private BookmarkDraft? _bookmarkDraft;
    private string _contentsSearch = "";
    private bool _contentsMyOrder;
    private readonly HashSet<string> _foldedCategories = [];
    private IReadOnlyList<PageOutline> _pageOutline = [];

    /// <summary>The Contents' search and order show once there are more bookmarks than this (testing item 21).</summary>
    private const int ContentsToolsFrom = 3;

    /// <summary>The card's title field, focused once when the card opens so a title can be typed at once.</summary>
    private ElementReference _draftTitle;
    private bool _focusDraft;

    /// <summary>The category the last bookmark went into: the next one starts there.</summary>
    private string _lastCategory = BookmarkCategory.PractiseId;

    /// <summary>What the last change to the bookmarks or the marks did, for screen readers.</summary>
    private string? _marksStatus;

    /// <summary>The bookmark last gone to from the drawer or the score: a second tap on it enters focus.</summary>
    private string? _shownBookmark;

    private (IReadOnlyList<Bookmark> From, IReadOnlyList<BookmarkCategory> Categories, string Language, IReadOnlyList<ScoreBookmark> Drawn)? _scoreBookmarks;

    private IReadOnlyList<Bookmark> Bookmarks => (_project ?? _session)?.Bookmarks ?? [];

    private IReadOnlyList<BookmarkCategory> Categories => (_project ?? _session)?.Categories ?? BookmarkCategory.BuiltIn;

    private BookmarkCategory CategoryOf(Bookmark bookmark) =>
        Categories.FirstOrDefault(category => category.Id == bookmark.CategoryId) ?? BookmarkCategory.BuiltIn[0];

    private string CategoryName(BookmarkCategory category) => category.NameIn(Words);

    private static string InkClass(InkColour ink) => $"ink-{ink.ToString().ToLowerInvariant()}";

    /// <summary>The bookmarks as the score draws them, made again only when they, their categories or the language change.</summary>
    private IReadOnlyList<ScoreBookmark>? ScoreBookmarks
    {
        get
        {
            if (_meter is null)
            {
                return null;
            }

            IReadOnlyList<Bookmark> bookmarks = Bookmarks;
            IReadOnlyList<BookmarkCategory> categories = Categories;
            if (_scoreBookmarks is { } cached && ReferenceEquals(cached.From, bookmarks) && ReferenceEquals(cached.Categories, categories) && cached.Language == Text.Language)
            {
                return cached.Drawn;
            }

            IReadOnlyList<ScoreBookmark> drawn = [.. bookmarks.Select(bookmark => new ScoreBookmark(
                bookmark.Id,
                bookmark.Span.Onset.Value,
                bookmark.Span.End.Value,
                bookmark.Title,
                CategoryOf(bookmark).Ink.ToString().ToLowerInvariant(),
                Text.Format("bookmark.label", bookmark.Title, BarsOf(bookmark.Span))))];
            _scoreBookmarks = (bookmarks, categories, Text.Language, drawn);
            return drawn;
        }
    }

    /// <summary>The title a new bookmark starts with: "Phrase 3, bars 9–12" for a detected phrase, else "Bars 9–12".</summary>
    private string DefaultBookmarkTitle(TickSpan span)
    {
        string bars = BarsOf(span);
        return CurrentPhrase is { } phrase
            ? Text.Format("bookmark.phraseTitle", phrase.Number, bars)
            : string.Concat(char.ToUpper(bars[0], Text.Culture).ToString(), bars[1..]);
    }

    /// <summary>Bookmark in the menu, or B: the card over the selection, pre-filled.</summary>
    private void StartBookmark()
    {
        _menuMore = false;
        if (_selection is not { } selection)
        {
            _marksStatus = Text["bookmark.noSelection"];
            return;
        }

        _bookmarkDraft = new BookmarkDraft(null, DefaultBookmarkTitle(selection.Span), _lastCategory);
        _focusDraft = true;
    }

    /// <summary>Whether the drawer was open at the last render: the menu moves aside when it opens or closes.</summary>
    private bool _contentsPlaced;

    private async Task PlaceMenuBesideContentsAsync()
    {
        if (_contentsPlaced != _contentsOpen && _scoreView is not null)
        {
            _contentsPlaced = _contentsOpen;
            await _scoreView.PlaceMenuAsync();
        }
    }

    private async Task FocusDraftAsync()
    {
        if (!_focusDraft || _bookmarkDraft is null)
        {
            return;
        }

        _focusDraft = false;
        try
        {
            await _draftTitle.FocusAsync();
        }
        catch (InvalidOperationException)
        {
            // The card is not on the page yet.
        }
    }

    /// <summary>B from the keyboard (Scripts/phrases/phrase-keys.ts), on the page and not in focus.</summary>
    [JSInvokable]
    public void BookmarkFromKey()
    {
        if (!Focused && !_penOn && _score is not null && _bookmarkDraft is null)
        {
            StartBookmark();
            StateHasChanged();
        }
    }

    private void SetDraftTitle(ChangeEventArgs change)
    {
        if (_bookmarkDraft is { } draft)
        {
            _bookmarkDraft = draft with { Title = change.Value as string ?? "" };
        }
    }

    private void SetDraftCategory(string id)
    {
        if (_bookmarkDraft is { } draft)
        {
            _bookmarkDraft = draft with { CategoryId = id };
        }
    }

    private void CancelBookmark() => _bookmarkDraft = null;

    private Task OnDraftKeyAsync(KeyboardEventArgs key)
    {
        if (key.Key == "Escape")
        {
            CancelBookmark();
        }

        return key.Key == "Enter" ? SaveBookmarkAsync() : Task.CompletedTask;
    }

    /// <summary>Done on the card: a new bookmark over the selection, or the changed one; a blank title keeps the bars.</summary>
    private async Task SaveBookmarkAsync()
    {
        if (_bookmarkDraft is not { } draft)
        {
            return;
        }

        _bookmarkDraft = null;
        if (draft.Id is { } id)
        {
            if (Bookmarks.FirstOrDefault(bookmark => bookmark.Id == id) is not { } old)
            {
                return;
            }

            string title = string.IsNullOrWhiteSpace(draft.Title) ? old.Title : draft.Title;
            await SaveMarksAsync(project => project.ChangeBookmark(id, bookmark => bookmark with { Title = title, CategoryId = draft.CategoryId }), Text.Format("bookmark.changed", title.Trim()));
            return;
        }

        if (_selection is not { } selection)
        {
            return;
        }

        _lastCategory = draft.CategoryId;
        string made = string.IsNullOrWhiteSpace(draft.Title) ? DefaultBookmarkTitle(selection.Span) : draft.Title;
        bool fromPhrase = CurrentPhrase is not null;
        await SaveMarksAsync(
            project => project.AddBookmark(new Bookmark(project.NextBookmarkId(), made, draft.CategoryId, selection.Span, fromPhrase, DateTimeOffset.Now)),
            Text.Format("bookmark.made", made.Trim()));
    }

    /// <summary>Changes the bookmarks or the marks and saves them with the project at once; without a project, they are kept for the page only.</summary>
    private async Task SaveMarksAsync(Func<Project, Project> change, string status)
    {
        Change(change);
        _marksStatus = status;
        if (_project is { } project)
        {
            CurrentPiece.Saved(project);
            await Projects.SaveAsync(project);
        }
    }

    /// <summary>A bookmark's bracket tapped on the score.</summary>
    private Task OnBookmarkPickedAsync(string id) =>
        Bookmarks.FirstOrDefault(bookmark => bookmark.Id == id) is { } bookmark ? GoToBookmarkAsync(bookmark) : Task.CompletedTask;

    /// <summary>An entry tapped: the score goes there and selects it; tapped again, the passage is lifted into focus.</summary>
    private async Task GoToBookmarkAsync(Bookmark bookmark)
    {
        _bookmarkDraft = null;
        if (_shownBookmark == bookmark.Id && _selection?.Span == SelectedSpanOf(bookmark))
        {
            _contentsOpen = false;
            EnterFocus();
            return;
        }

        _shownBookmark = bookmark.Id;
        await SelectAsync(bookmark.Span, note: null);
    }

    /// <summary>What selecting the bookmark selects: its span, or the first bars of it when it is longer than a selection goes.</summary>
    private TickSpan SelectedSpanOf(Bookmark bookmark) =>
        _notes is null || _meter is null ? bookmark.Span : MotifSelector.Select(_notes, _meter, bookmark.Span, _filter, SelectionCap(bookmark.Span)).Span;

    private bool IsShown(Bookmark bookmark) => _selection is { } selection && selection.Span == SelectedSpanOf(bookmark);

    private void EditBookmark(Bookmark bookmark)
    {
        _bookmarkDraft = new BookmarkDraft(bookmark.Id, bookmark.Title, bookmark.CategoryId);
        _focusDraft = true;
    }

    private Task DeleteBookmarkAsync(string id)
    {
        _bookmarkDraft = null;
        return Bookmarks.FirstOrDefault(bookmark => bookmark.Id == id) is { } bookmark
            ? SaveMarksAsync(project => project.RemoveBookmark(id), Text.Format("bookmark.deleted", bookmark.Title))
            : Task.CompletedTask;
    }

    /// <summary>Whether the drawer's empty state offers the detected form as Structure bookmarks.</summary>
    private bool CanAddStructure => _sentences is { Sentences.Count: > 0 } && !Bookmarks.Any(bookmark => bookmark.CategoryId == BookmarkCategory.StructureId);

    /// <summary>Add Structure bookmarks: one bookmark for each detected sentence (music-core writes and names them).</summary>
    private async Task AddStructureBookmarksAsync()
    {
        if (_sentences is not { } map)
        {
            return;
        }

        int before = Bookmarks.Count;
        await SaveMarksAsync(project => project.AddStructureBookmarks(map.Sentences, Words, DateTimeOffset.Now), "");
        _marksStatus = Text.Count("bookmark.structureAdded", Bookmarks.Count - before);
    }

    /// <summary>
    /// The bookmarks the drawer lists, by category in the categories' order: each in score order or the player's own,
    /// matching the search in its title or its category's name, and with folded categories' entries left out.
    /// </summary>
    private IReadOnlyList<(BookmarkCategory Category, IReadOnlyList<Bookmark> Entries)> ContentsGroups
    {
        get
        {
            IReadOnlyList<Bookmark> ordered = _contentsMyOrder ? Bookmarks : (_project ?? _session)?.BookmarksInScoreOrder ?? [];
            string search = _contentsSearch.Trim();
            return [.. Categories
                .Select(category => (Category: category, Entries: (IReadOnlyList<Bookmark>)[.. ordered.Where(bookmark => bookmark.CategoryId == category.Id && Matches(bookmark, category, search))]))
                .Where(group => group.Entries.Count > 0)];
        }
    }

    private bool Matches(Bookmark bookmark, BookmarkCategory category, string search) =>
        search.Length == 0
        || bookmark.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || CategoryName(category).Contains(search, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>"2 ideas": what the player kept about passages inside the bookmark; null for none.</summary>
    private string? IdeasIn(Bookmark bookmark)
    {
        int count = Kept.Count(kept => bookmark.Span.Onset <= kept.Phrase.Span.Onset && kept.Phrase.Span.End <= bookmark.Span.End);
        return count == 0 ? null : Text.Count("contents.ideas", count);
    }

    private void ToggleFold(string categoryId)
    {
        if (!_foldedCategories.Remove(categoryId))
        {
            _foldedCategories.Add(categoryId);
        }
    }

    private void SetContentsSearch(ChangeEventArgs change) => _contentsSearch = change.Value as string ?? "";

    /// <summary>The piece's title in the bar, or Contents in the overflow: the drawer opens over the left of the page.</summary>
    private async Task ToggleContentsAsync()
    {
        _contentsOpen = !_contentsOpen;
        _popover = Popover.None;
        if (_contentsOpen)
        {
            _insightsOpen = false;
            _checkOpen = false;
            _performanceOpen = false;
            await LoadPagesAsync();
        }
    }

    /// <summary>A chip at the top of the Contents: the sheet scrolls to that section; nothing else changes.</summary>
    private async Task ScrollContentsToAsync(string id)
    {
        await using IJSObjectReference module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/layout/fragment.js");
        await module.InvokeAsync<bool>("scrollToFragment", id);
    }

    private async Task LoadPagesAsync() => _pageOutline = _scoreView is null ? [] : await _scoreView.PageOutlineAsync();

    private Task ShowPageAsync(int page) => _scoreView?.ShowPageAsync(page) ?? Task.CompletedTask;

    /// <summary>"phrases 3–5" for the phrases that start on the page; null when none does.</summary>
    private string? PhrasesOn(PageOutline page)
    {
        int[] numbers = [.. (_phrases?.Phrases ?? []).Where(phrase => phrase.Span.Onset.Value >= page.Start && phrase.Span.Onset.Value < page.End).Select(phrase => phrase.Number)];
        return numbers.Length switch
        {
            0 => null,
            1 => Text.Format("contents.pagePhrase", numbers[0]),
            _ => Text.Format("contents.pagePhrases", numbers[0], numbers[^1]),
        };
    }

    /// <summary>The bookmarks that start on the page, for its ticks.</summary>
    private IEnumerable<Bookmark> BookmarksOn(PageOutline page) =>
        Bookmarks.Where(bookmark => bookmark.Span.Onset.Value >= page.Start && bookmark.Span.Onset.Value < page.End);

    private bool PageInView(PageOutline page) => _pages.First <= page.Page && page.Page <= _pages.Last;
}