using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Local-first originals (version 0.3, Andrei 2026-11-11 21:20 UTC): the Sources sheet says where each original of the
/// piece is held (in the piece's file here, on this device, only on the iPad or in another browser, or nowhere), and
/// «Привязать оригинал» links a file the player picks again to the same original by its fingerprint, without a copy. When
/// the piece opens, this browser writes its own marks: it holds the PDF in the file, and the recordings it keeps; a
/// recording it held and the browser has since cleared loses its mark, so the badge on the other devices tells the truth.
/// </summary>
public partial class PlaybackLab
{
    private IJSObjectReference? _originalsModule;

    /// <summary>The recordings of the piece this browser holds, by original id, as last checked.</summary>
    private readonly HashSet<string> _heldHere = new(StringComparer.Ordinal);

    /// <summary>The original whose file is being linked again, for the status under it.</summary>
    private string? _relinking;

    /// <summary>What came of linking it: linked, or another file.</summary>
    private string? _relinkStatus;

    /// <summary>The file pickers of «Привязать оригинал» under the recordings not in this browser, by version.</summary>
    private readonly Dictionary<string, InputFile> _relinkPickers = new(StringComparer.Ordinal);

    private async Task<IJSObjectReference> OriginalsAsync() =>
        _originalsModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/storage/originals.js");

    /// <summary>The originals the Sources sheet lists with a badge: what was added as a PDF, photos or a recording.</summary>
    private IEnumerable<PieceOriginal> ShownOriginals => _project?.Originals.Where(static original => original.Kind is OriginalKind.Pdf or OriginalKind.Images or OriginalKind.Audio) ?? [];

    /// <summary>Where an original's bytes are, as this browser sees them.</summary>
    private OriginalWhere WhereIs(PieceOriginal original) =>
        _project is { } project
            ? new OriginalSummary(original.Id, original.Kind, original.Kept, project.HasInFile(original), [.. original.Held.Select(static mark => mark.Device)])
                .Where(HoldingDevice.Web, _heldHere.Contains(original.Id))
            : OriginalWhere.Nowhere;

    /// <summary>The fingerprint of the recording just picked to add as a version; null when it was too large to read whole.</summary>
    private string? _pickedRecording;

    /// <summary>A recording is picked to add as a version: its fingerprint is taken before it is heard, so it can be linked again.</summary>
    private async Task FingerprintRecordingAsync(ElementReference input)
    {
        try
        {
            _pickedRecording = await (await OriginalsAsync()).InvokeAsync<string?>("fingerprintPicked", input);
        }
        catch (JSException)
        {
            _pickedRecording = null;
        }
    }

    /// <summary>
    /// A recording just added as a version: its original is described by the file the player picked (version 0.3) with its
    /// fingerprint, the version says it was heard in it, and this browser marks it held when its sound was kept here.
    /// </summary>
    private async Task KeepRecordingOriginalAsync(string versionId, string fileName)
    {
        if (_project?.FindOriginal(versionId) is not { } original)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.Now;
        _project = _project
            .WithOriginal(new PieceOriginal(versionId, OriginalKind.Audio, string.IsNullOrWhiteSpace(fileName) ? original.FileName : fileName, OriginalKept.Device, now) { Sha256 = _pickedRecording })
            .ChangePerformance(versionId, version => version with { From = versionId, At = now });
        _pickedRecording = null;
        await CheckOriginalsAsync(changed: true);
    }

    /// <summary>
    /// This browser's marks on the piece's originals, brought up to date: the PDF in the file and each recording kept here
    /// are marked held, a recording this browser held and no longer keeps is not; the PDF's fingerprint is worked out once.
    /// Saved, and so sent online, only when a mark changed.
    /// </summary>
    /// <param name="changed">The piece changed before the check, so it is saved whatever the check finds.</param>
    private async Task CheckOriginalsAsync(bool changed = false)
    {
        if (_project is not { } project)
        {
            return;
        }

        Project checkedProject = project.Fingerprinted();
        DateTimeOffset now = DateTimeOffset.Now;
        bool anyHere = false;
        try
        {
            IJSObjectReference module = await OriginalsAsync();
            foreach (PieceOriginal original in checkedProject.Originals)
            {
                bool here;
                if (original.Kept == OriginalKept.File)
                {
                    here = checkedProject.HasInFile(original);
                }
                else if (original.Kind == OriginalKind.Audio)
                {
                    string state = await module.InvokeAsync<string>("recordingHere", RecordingKey(original.Id));
                    if (state == "never" && original.IsHeldBy(HoldingDevice.Web))
                    {
                        // Another browser's mark: this one never held the sound, so the mark is not its to take back.
                        _heldHere.Remove(original.Id);
                        continue;
                    }

                    here = state == "here";
                    await module.InvokeVoidAsync("rememberHeld", RecordingKey(original.Id), here);
                }
                else
                {
                    continue;
                }

                if (here && original.Kept == OriginalKept.Device)
                {
                    _heldHere.Add(original.Id);
                }
                else
                {
                    _heldHere.Remove(original.Id);
                }

                anyHere |= here && original.Kind is OriginalKind.Pdf or OriginalKind.Images or OriginalKind.Audio;
                checkedProject = checkedProject.MarkHeld(original.Id, HoldingDevice.Web, here, now);
            }

            if (anyHere)
            {
                _ = await module.InvokeAsync<bool?>("persistOnce");
            }
        }
        catch (JSException)
        {
            // The marks are a convenience: the piece works without them, and they are checked again on the next open.
            checkedProject = project;
        }

        if (changed || !ReferenceEquals(checkedProject, project))
        {
            _project = checkedProject;
            CurrentPiece.Saved(_project);
            await Projects.SaveAsync(_project);
        }
    }

    /// <summary>«Привязать оригинал» for the PDF: the picked file is the piece's PDF by its fingerprint, and it is kept in the piece again.</summary>
    private async Task RelinkPdfAsync(InputFileChangeEventArgs args)
    {
        _relinkStatus = null;
        _relinking = PieceOriginal.DocumentId;
        if (_project is not { } project || _meter is null || _score is null)
        {
            return;
        }

        (byte[]? data, string? problem) = await PickedPdfAsync(args.File);
        if (data is null)
        {
            _relinkStatus = problem;
            return;
        }

        if (project.RelinkDocument(data, PageMap.Even(_meter, _score.End, PdfDocument.PageCount(data))) is not { } linked)
        {
            _relinkStatus = Text["originals.otherFile"];
            return;
        }

        _project = linked.MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Web, held: true, DateTimeOffset.Now);
        _relinkStatus = Text["originals.linked"];
        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
    }

    /// <summary>
    /// «Привязать оригинал» for a recording: the picked file is that recording by its fingerprint (or, for one added before
    /// fingerprints were kept, the player's word), and its sound is kept in this browser again.
    /// </summary>
    private async Task RelinkRecordingAsync(string id)
    {
        _relinkStatus = null;
        _relinking = id;
        if (_project?.FindOriginal(id) is not { } original || _relinkPickers.GetValueOrDefault(id)?.Element is not { } input)
        {
            return;
        }

        IJSObjectReference module = await OriginalsAsync();
        string? sha256 = await module.InvokeAsync<string?>("fingerprintPicked", input);
        if (original.Sha256 is not null && (sha256 is null || !original.Matches(sha256)))
        {
            _relinkStatus = Text["originals.otherFile"];
            return;
        }

        if (!await module.InvokeAsync<bool>("keepRelinked", input, RecordingKey(id)))
        {
            _relinkStatus = Text["recording.notDecoded"];
            return;
        }

        _heldHere.Add(id);
        Project project = _project!;
        _project = project.WithOriginal(original with { Sha256 = original.Sha256 ?? sha256 }).MarkHeld(id, HoldingDevice.Web, held: true, DateTimeOffset.Now);
        _relinkStatus = Text["originals.linked"];
        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
        if (_assemblyShown == id)
        {
            await SoundShownAsync();
        }
    }

    /// <summary>The badge of an original: «В файле пьесы», «На этом устройстве», «Только на iPad», …</summary>
    private string WhereText(PieceOriginal original) => OriginalPlaces.Text(Text, WhereIs(original));
}