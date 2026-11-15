using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Import;
using MotiffLab.Web.Score;
using MotiffLab.Web.Sources;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The new sources (version 0.6, behind <see cref="SourcesExperiment"/>; decision record 0030; sources sheet,
/// 2026-11-14): the switch of all the piece's views where PDF | Notes stood, the Sources button after it, and the Sources
/// sheet of Originals and Notes in place of the sheet of 0.5. What the sheet lists and what each operation does is
/// music-core's (<see cref="PieceViews"/>, <see cref="SourceOperations"/>); the page only shows it, keeps the piece and
/// offers Undo in the toast. Other Notes show read-only in place of the score until the working notes are picked again.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The other Notes shown in place of the working notes, read-only; null for the working notes.</summary>
    private string? _otherShown;

    /// <summary>The Notes just recognised, tinted once in the sheet.</summary>
    private string? _freshNotes;

    /// <summary>What the last change in the sheet did not do, and why.</summary>
    private string? _sourcesStatus;

    private (Project Of, int Held, PieceViews Views)? _pieceViews;
    private (OtherNotes Of, ScoreSource Score)? _otherScore;
    private readonly Dictionary<byte[], int> _pdfPages = new(ReferenceEqualityComparer.Instance);
    private IJSObjectReference? _photosModule;

    private bool SourcesOn => SourcesExperiment.IsOn(SettingsStore);

    /// <summary>The piece's sources as music-core sees them on this browser, worked out again when the piece changes.</summary>
    private PieceViews? SourceViews
    {
        get
        {
            if (_project is not { } project)
            {
                return null;
            }

            if (_pieceViews is not { } cached || !ReferenceEquals(cached.Of, project) || cached.Held != _heldHere.Count)
            {
                cached = (project, _heldHere.Count, PieceViews.Of(project, HoldingDevice.Web, [.. _heldHere]));
                _pieceViews = cached;
            }

            return cached.Views;
        }
    }

    private int AssemblyCarries => _project?.Assembly.Steps.Count(static step => step is CarryStep) ?? 0;

    /// <summary>The key of the view shown now: the working notes, or the other Notes shown.</summary>
    private string ShownViewKey => SourceNames.KeyOf(ViewKind.Notes, _otherShown ?? _project?.NotesId ?? Project.FirstNotesId);

    /// <summary>The views of the switch this browser can show, the hidden ones left out.</summary>
    private IReadOnlyList<SwitchView> SwitchViews => SourceViews is { } views ? SourceNames.Switch(Text, views, BarTitle, AssemblyCarries, CanShowView) : [];

    /// <summary>
    /// Whether this browser can show a view: the working or other Notes, a version it can compare, a PDF or photos whose
    /// bytes are here, a recording's sound with the notes heard in it (heard while comparing).
    /// </summary>
    private bool CanShowView(SourceView view)
    {
        if (_project is not { } project)
        {
            return false;
        }

        return view.Kind switch
        {
            ViewKind.Notes => view.Working || project.Sources.Notes.Any(notes => notes.Id == view.Id) || (project.FindPerformance(view.Id) is not null && Work?.Source(view.Id) is not null),
            ViewKind.Original => PdfOf(view.Id) is not null,
            _ => project.FindPerformance(view.Id) is not null && Work?.Source(view.Id) is not null,
        };
    }

    /// <summary>The bytes of a PDF or photos original kept in the piece's file; null when they are elsewhere.</summary>
    private byte[]? PdfOf(string id) => _project is not { } project ? null
        : id == PieceOriginal.DocumentId ? project.Document
        : project.Sources.Data.GetValueOrDefault(id) is { } bytes && PdfDocument.IsPdf(bytes) ? bytes : null;

    /// <summary>How many pages a PDF or photos original has: as its description says, else counted once in its bytes.</summary>
    private int? PagesOfOriginal(PieceOriginal original)
    {
        if (original.Pages is { } pages)
        {
            return pages;
        }

        if (PdfOf(original.Id) is not { } bytes)
        {
            return null;
        }

        if (!_pdfPages.TryGetValue(bytes, out int counted))
        {
            counted = PdfDocument.PageCount(bytes);
            _pdfPages[bytes] = counted;
        }

        return counted;
    }

    /// <summary>The other Notes shown, engraved as their file has them (the score as written, else from their notes).</summary>
    private ScoreSource? OtherNotesScore
    {
        get
        {
            if (_otherShown is not { } id || _project?.Sources.Notes.FirstOrDefault(notes => notes.Id == id) is not { } other)
            {
                return null;
            }

            if (_otherScore is { } cached && ReferenceEquals(cached.Of, other))
            {
                return cached.Score;
            }

            // music-core's view of the piece with these Notes as its notes, their own corrections laid over them, leniently:
            // a correction that no longer fits is left out of what shows, never stops it showing.
            OpenedProject opened;
            try
            {
                opened = OpenedProject.Open(_project.ViewOf(id), EditsLayer.Lenient);
            }
            catch (ProjectFileException)
            {
                return null;
            }

            ImportedPiece piece = opened.Piece;
            string title = SourceViews?.Notes.FirstOrDefault(notes => notes.Id == id) is { } listed && SourceViews is { } views
                ? SourceNames.NotesTitle(views, listed, BarTitle)
                : BarTitle;
            ScoreSource score = piece.ScoreXml is { } xml && !opened.Project.ChangesNotes
                ? ScoreSource.AsWritten(System.Text.Encoding.UTF8.GetString(xml))
                : ScoreSource.FromNotes(NotationGenerator.Generate(opened.Notes, piece.Meter, piece.Keys.Select(key => new KeyChange(key.At, Math.Clamp(key.Fifths, -7, 7))), piece.End, title));
            _otherScore = (other, score);
            return score;
        }
    }

    /// <summary>The Sources button: the sheet opens or closes, as «N версии» did.</summary>
    private void ToggleSources()
    {
        _sourcesStatus = null;
        TogglePerformance();
    }

    /// <summary>A view picked in the switch or «Открыть» in the sheet.</summary>
    private async Task OpenViewAsync(string key)
    {
        if (_project is not { } project || key.Split(':', 2) is not [var kind, var id])
        {
            return;
        }

        if (kind == "original")
        {
            await ShowOriginalAsync(id);
        }
        else if (kind == "notes" && id == project.NotesId)
        {
            ShowWorkingNotes();
        }
        else if (kind == "notes" && project.Sources.Notes.Any(notes => notes.Id == id))
        {
            await ShowOtherNotesAsync(id);
        }
        else
        {
            // A version, or a recording's sound: compared with the notes, as «Открыть сборку» did (0024).
            ShowWorkingNotes();
            await OpenAssemblyAsync(id);
        }
    }

    private Task PickViewAsync(SwitchView view) => OpenViewAsync(view.Key);

    /// <summary>A PDF or photos original in the PDF view: the piece's first PDF as before, another by its id.</summary>
    private async Task ShowOriginalAsync(string id)
    {
        if (id == PieceOriginal.DocumentId)
        {
            await ShowPdfAsync();
            return;
        }

        if (_project is { } project)
        {
            Navigation.NavigateTo($"pdf?project={Uri.EscapeDataString(project.Id)}&original={Uri.EscapeDataString(id)}");
        }
    }

    private void ShowWorkingNotes()
    {
        if (_otherShown is null)
        {
            return;
        }

        _otherShown = null;
        _landing = false;
    }

    /// <summary>Other Notes, read-only, in place of the working notes: playback stops and the drawers that work on the notes close.</summary>
    private async Task ShowOtherNotesAsync(string id)
    {
        if (Player.State != MotiffLab.Web.Playback.PlaybackState.Stopped)
        {
            await Player.PauseAsync();
        }

        await KeepNotesViewAsync();
        _otherShown = id;
        _scoreView = null;
        _insightsOpen = false;
        _checkOpen = false;
        _rollOpen = false;
        _popover = Popover.None;
        _selection = null;
    }

    /// <summary>The eye: a view hidden from the switch or shown in it again; hiding the one shown goes back to the working notes.</summary>
    private async Task HideViewAsync((string Id, bool Hidden) change)
    {
        if (_project is not { } project)
        {
            return;
        }

        await SaveSourcesAsync(project.ViewHidden(change.Id, change.Hidden));
        if (change.Hidden && _otherShown == change.Id)
        {
            ShowWorkingNotes();
        }
    }

    /// <summary>The piece changed by the sheet, kept; with its versions changed, the notes and comparisons are worked out again.</summary>
    private async Task SaveSourcesAsync(Project changed)
    {
        Project? before = _project;
        _project = changed;
        _sourcesStatus = null;
        if (before is not null && !ReferenceEquals(before.Performances, changed.Performances))
        {
            _performanceId = null;
            _scoreChecks = null;
            await ApplyEditsAsync(_edits);
            return;
        }

        CurrentPiece.Saved(changed);
        await Projects.SaveAsync(changed);
    }

    /// <summary>Undo in the toast: the piece as it was before the sheet's change.</summary>
    private async Task RestoreSourcesAsync(Project before)
    {
        if (_project?.Id == before.Id)
        {
            await SaveSourcesAsync(before);
        }
    }

    /// <summary>«Удалить только оригинал» or «Удалить вместе с нотами»: the toast brings it back.</summary>
    private async Task RemoveSourceOriginalAsync((string Id, bool WithNotes) remove)
    {
        if (_project is not { } before)
        {
            return;
        }

        Project changed;
        try
        {
            changed = before.RemoveOriginal(remove.Id, DateTimeOffset.Now, remove.WithNotes);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            _sourcesStatus = exception.Message;
            return;
        }

        if (_otherShown is { } shown && changed.Sources.Notes.All(notes => notes.Id != shown))
        {
            ShowWorkingNotes();
        }

        await SaveSourcesAsync(changed);
        _mergeToast = (Text[remove.WithNotes ? "sources.removedWithToast" : "sources.removedToast"], () => RestoreSourcesAsync(before));
    }

    /// <summary>«Удалить ноты»: other Notes or a version go with their corrections; the toast brings them back.</summary>
    private async Task DeleteSourceNotesAsync(string id)
    {
        if (_project is not { } before)
        {
            return;
        }

        Project changed;
        try
        {
            changed = before.DeleteNotes(id);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            _sourcesStatus = exception.Message;
            return;
        }

        if (_otherShown == id)
        {
            ShowWorkingNotes();
        }

        _picks.Remove(id);
        await SaveSourcesAsync(changed);
        _mergeToast = (Text["sources.notesDeletedToast"], () => RestoreSourcesAsync(before));
    }

    /// <summary>
    /// «Сделать рабочими»: other Notes become the working notes and the working notes until now stay beside them; the piece
    /// opens again on them (0030 section 4 as amended), and Undo makes the notes until now working again.
    /// </summary>
    private async Task MakeWorkingAsync(string id)
    {
        if (_project is not { } project || SourceViews is not { } views)
        {
            return;
        }

        string was = project.NotesId;
        string file = views.Notes.FirstOrDefault(notes => notes.Id == id) is { } notes ? SourceNames.NotesTitle(views, notes, BarTitle) : BarTitle;
        Project changed;
        try
        {
            changed = project.MakeWorking(id);
        }
        catch (InvalidOperationException)
        {
            _sourcesStatus = Text["sources.cannotMakeWorking"];
            return;
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        _otherShown = null;
        await ReopenAsync(changed);
        _mergeToast = (Text.Format("sources.workingToast", file), () => UndoMakeWorkingAsync(was));
    }

    private async Task UndoMakeWorkingAsync(string was)
    {
        if (_project is { } project && project.Sources.Notes.Any(notes => notes.Id == was))
        {
            await ReopenAsync(project.MakeWorking(was));
        }
    }

    /// <summary>
    /// The notes the scan server read again from a PDF or photos original: other Notes beside the working ones, which keep
    /// their corrections (0030 section 4, «Распознать» and «Распознать ещё раз»). Returns why they cannot be used.
    /// </summary>
    private async Task<string?> RecogniseOriginalAsync(string originalId, ScanResult result)
    {
        if (_project is not { HasNotes: true } project || result.MusicXml is not { } read || project.FindOriginal(originalId) is not { } original)
        {
            return result.Problem;
        }

        // The engine's mistakes put right, as in sources.notes.recognise (#495 BUG-03).
        byte[] musicXml = ScanRepair.Repair(read);
        if (PieceImporter.Import("notes.musicxml", musicXml).Piece is not { } piece)
        {
            return Text["pdf.readNone"];
        }

        int pages = Math.Max(1, result.Pages > 0 ? result.Pages : PagesOfOriginal(original) ?? 1);
        PageMap map = PageMap.Of(piece, pages);
        string id = project.NextNotesId();
        Project changed;
        try
        {
            changed = project.RecognisedAgain(originalId, musicXml, result.Engine, map, DateTimeOffset.Now, result.Reports.Count > 0 ? result.Reports : null);
        }
        catch (ArgumentException)
        {
            // Reports that do not match the pages of the notes: the notes are kept without them, as a first scan's are.
            changed = project.RecognisedAgain(originalId, musicXml, result.Engine, map, DateTimeOffset.Now);
        }

        _freshNotes = id;
        await SaveSourcesAsync(changed);
        _mergeToast = (Text.Count("sources.recognisedToast", pages), null);
        StateHasChanged();
        return null;
    }

    private async Task DisposeSourcesAsync()
    {
        if (_photosModule is not null)
        {
            try
            {
                await _photosModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is going away with the browser's side of it.
            }

            _photosModule = null;
        }
    }
}