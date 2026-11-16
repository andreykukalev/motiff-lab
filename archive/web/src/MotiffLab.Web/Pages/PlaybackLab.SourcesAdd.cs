using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Import;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Originals added in the Sources sheet (version 0.6, behind <see cref="Sources.SourcesExperiment"/>): a PDF, photos, a
/// recording or a deleted original's same file, kept as they are and not recognised; the player's files are only read.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>«+ Добавить оригинал · PDF»: kept in the piece as it is, not recognised; the first PDF of a piece is its PDF view.</summary>
    private async Task AddPdfOriginalAsync(InputFileChangeEventArgs args)
    {
        (byte[]? data, string? problem) = await PickedPdfAsync(args.File);
        if (data is null)
        {
            _sourcesStatus = problem;
            return;
        }

        await AddKeptOriginalAsync(OriginalKind.Pdf, args.File.Name, data, PdfDocument.PageCount(data));
    }

    /// <summary>«Фото страниц»: the pictures, in page order, become one PDF made on this device, kept in the piece as photos.</summary>
    private async Task AddPhotosOriginalAsync(InputFileChangeEventArgs args)
    {
        IReadOnlyList<IBrowserFile> photos = PhotoPages.InOrder(args.GetMultipleFiles(PhotoPages.MaxPhotos));
        if (photos.Count == 0 || photos.Any(photo => !PhotoPages.IsPhoto(photo.Name)))
        {
            _sourcesStatus = Text["sources.notPhoto"];
            return;
        }

        List<byte[]> pictures = [];
        foreach (IBrowserFile photo in photos)
        {
            await using Stream stream = photo.OpenReadStream(PdfDocument.MaxFileBytes);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            pictures.Add(memory.ToArray());
        }

        _photosModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/import/photos-pdf.js");
        byte[]? pdf = await _photosModule.InvokeAsync<byte[]?>("photosToPdf", pictures, photos.Select(static photo => photo.ContentType).ToArray());
        if (pdf is null)
        {
            _sourcesStatus = Text["sources.notPhoto"];
            return;
        }

        await AddKeptOriginalAsync(OriginalKind.Images, Path.ChangeExtension(photos[0].Name, PdfDocument.Extension), pdf, photos.Count);
    }

    /// <summary>
    /// An original whose bytes go into the piece's file, not recognised. The same file as a deleted original takes it back
    /// with its marks (music-core matches the fingerprint); the piece's first PDF comes with its pages spread over the bars.
    /// </summary>
    private async Task AddKeptOriginalAsync(OriginalKind kind, string fileName, byte[] data, int? pages)
    {
        if (_project is not { } project || _meter is null || _score is null)
        {
            return;
        }

        string stem = kind switch
        {
            OriginalKind.Pdf => PieceOriginal.DocumentId,
            OriginalKind.Images => "src_photos",
            OriginalKind.Midi => "src_midi",
            _ => "src_score",
        };
        var original = new PieceOriginal(project.NextOriginalId(stem), kind, fileName, OriginalKept.File, DateTimeOffset.Now) { Pages = pages };
        string sha = PieceOriginal.Fingerprint(data);
        string id = project.Sources.Removed.FirstOrDefault(removed => removed.Original.Matches(sha))?.Original.Id ?? original.Id;
        PageMap? map = id == PieceOriginal.DocumentId && kind == OriginalKind.Pdf ? PageMap.Even(_meter, _score.End, Math.Max(1, pages ?? 1)) : null;
        try
        {
            _project = project.AddOriginal(original, data, map);
        }
        catch (ArgumentException exception)
        {
            _sourcesStatus = exception.Message;
            return;
        }

        _mergeToast = (Text.Format("sources.addedToast", fileName), () => RestoreSourcesAsync(project));
        await CheckOriginalsAsync(changed: true);
    }

    /// <summary>«Запись»: its sound is kept in this browser, not heard; only its description goes into the piece.</summary>
    private async Task AddRecordingOriginalAsync(IBrowserFile file, ElementReference input)
    {
        if (_project is not { } project)
        {
            return;
        }

        IJSObjectReference module = await OriginalsAsync();
        string? sha = await module.InvokeAsync<string?>("fingerprintPicked", input);
        string id = (sha is null ? null : project.Sources.Removed.FirstOrDefault(removed => removed.Original.Matches(sha))?.Original.Id) ?? project.NextOriginalId("src_rec");
        await KeepRecordingOriginalAsync(project, id, file.Name, sha, input);
    }

    /// <summary>«Добавить тот же файл»: a deleted original comes back when the file picked is the one it was.</summary>
    private async Task AddSameOriginalAsync(string id, IBrowserFile file, ElementReference input)
    {
        if (_project?.Sources.RemovedWith(id) is not { } removed)
        {
            return;
        }

        IJSObjectReference module = await OriginalsAsync();
        string? sha = await module.InvokeAsync<string?>("fingerprintPicked", input);
        if (sha is null || !removed.Original.Matches(sha))
        {
            _sourcesStatus = Text["sources.otherFile"];
            return;
        }

        if (removed.Original.Kind == OriginalKind.Audio)
        {
            await KeepRecordingOriginalAsync(_project, id, file.Name, sha, input);
            return;
        }

        byte[] data;
        await using (Stream stream = file.OpenReadStream(PdfDocument.MaxFileBytes))
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            data = memory.ToArray();
        }

        await AddKeptOriginalAsync(removed.Original.Kind, removed.Original.FileName, data, removed.Original.Pages);
    }

    private async Task KeepRecordingOriginalAsync(Project project, string id, string fileName, string? sha, ElementReference input)
    {
        IJSObjectReference module = await OriginalsAsync();
        if (!await module.InvokeAsync<bool>("keepRelinked", input, RecordingKey(id)))
        {
            _sourcesStatus = Text["sources.recordingNotKept"];
            return;
        }

        double? milliseconds = await module.InvokeAsync<double?>("durationPicked", input);
        DateTimeOffset now = DateTimeOffset.Now;
        var original = new PieceOriginal(id, OriginalKind.Audio, fileName, OriginalKept.Device, now)
        {
            Sha256 = sha,
            DurationMs = milliseconds is { } ms ? (long)Math.Round(ms) : null,
        };
        try
        {
            _project = project.AddOriginal(original, null).MarkHeld(id, HoldingDevice.Web, held: true, now);
        }
        catch (ArgumentException exception)
        {
            _sourcesStatus = exception.Message;
            return;
        }

        _heldHere.Add(id);
        _mergeToast = (Text.Format("sources.addedToast", fileName), () => RestoreSourcesAsync(project));
        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
    }

    /// <summary>«Привязать оригинал» in the new sheet: the piece's PDF, or a recording, by its fingerprint.</summary>
    private async Task RelinkSourceAsync(string id, InputFileChangeEventArgs args, ElementReference input)
    {
        if (id == PieceOriginal.DocumentId)
        {
            await RelinkPdfAsync(args);
        }
        else
        {
            await RelinkRecordingFromAsync(id, input);
        }

        _sourcesStatus = _relinkStatus;
    }

    /// <summary>«Добавить ещё PDF» in the sheet of 0.5 (always, sources sheet 2026-11-14): a PDF beside the one notes were read from, never in its place.</summary>
    private async Task AddAnotherPdfAsync(InputFileChangeEventArgs args)
    {
        _addingSource = false;
        await AddPdfOriginalAsync(args);
        _performanceStatus = _sourcesStatus ?? Text.Format("sources.addedToast", args.File.Name);
    }

    /// <summary>Whether notes were read from the piece's PDF: then a PDF added goes beside it, never in its place.</summary>
    private bool PdfHasNotes => _project is { Document: not null } && SourceViews?.Notes.Any(static notes => notes.Original == PieceOriginal.DocumentId) == true;

    /// <summary>
    /// A file handed in from the sheet: the sheet's pickers call back through plain delegates, which re-render only the sheet,
    /// so the page shows what the file changed (the switch, the toast) once its work is done.
    /// </summary>
    private async Task ShownAfter(Task work)
    {
        await work;
        StateHasChanged();
    }
}