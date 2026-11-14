using Microsoft.AspNetCore.Components.Forms;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Import;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The Sources sheet (testing item 16; Andrei, 2026-11-06: "the PDF is available right inside the piece; everything there
/// is is picked up"): the piece's base, the PDF kept inside it, the other versions added over it, and what the player's own
/// version holds beyond the base. One Add a source takes a MIDI or MusicXML file, a PDF (kept in the piece, its notes read
/// as a version when the player asks) or a recording. After a version is added, four next steps: Compare, Hear it alone,
/// Take everything sure (reversible) and Adjust it myself.
/// </summary>
public partial class PlaybackLab
{

    /// <summary>Whether Read the notes of the PDF as a version is open under the PDF's row.</summary>
    private bool _readingDocument;

    /// <summary>Whether the choices of Add a source show.</summary>
    private bool _addingSource;

    private PieceSources? Sources => _project is { } project ? PieceSources.Of(project) : null;

    /// <summary>How much of the shown version lines up with the notes, in whole per cent of its bars; null with no comparison.</summary>
    private int? AlignedPercent => ShownComparison is { Bars.Count: > 0 } comparison
        ? (comparison.Bars.Count - UnalignedBarCount) * 100 / comparison.Bars.Count
        : null;

    private string BaseIcon => _project?.Summary.Origin switch
    {
        PieceOrigin.Recording => "mic",
        PieceOrigin.Scan or PieceOrigin.Pdf => "pages",
        _ => "file",
    };

    private static string VersionIcon(SavedPerformance version) => version.Source switch
    {
        PerformanceSource.Recording => "mic",
        PerformanceSource.Score => "note",
        _ => "file",
    };

    /// <summary>What the version is to the notes: notes (a score) or a performance (a MIDI file or a recording).</summary>
    private string VersionRole(SavedPerformance version) =>
        Text[version.Source == PerformanceSource.Score ? "sources.roleNotes" : "sources.rolePlaying"];

    /// <summary>The place to open on arrival («&amp;view=addSource»): the Sources sheet with Add a source open, as a piece split from a recording opens (version 0.5).</summary>
    private const string AddSourceView = "addSource";

    /// <summary>A piece split from a recording, opened: the Sources sheet with Add a source open, for its score.</summary>
    private void OpenAddSource()
    {
        _insightsOpen = false;
        _checkOpen = false;
        _performanceOpen = true;
        _addingSource = true;
        _readingDocument = false;
    }

    private void ToggleAddSource()
    {
        _addingSource = !_addingSource;
        _readingDocument = false;
    }

    private void ToggleReadDocument()
    {
        _readingDocument = !_readingDocument;
        _addingSource = false;
    }

    /// <summary>
    /// A PDF kept inside the piece, read on the device: its pages are shared out over the bars until its notes are read, and
    /// it can be opened in the "PDF" view. A PDF kept before is replaced; the online copy leaves it out.
    /// </summary>
    private async Task AddDocumentAsync(InputFileChangeEventArgs args)
    {
        _performanceStatus = null;
        _addingSource = false;
        if (_project is not { HasNotes: true } project || _meter is null || _score is null)
        {
            return;
        }

        (byte[]? data, string? problem) = await PickedPdfAsync(args.File);
        if (data is null)
        {
            _performanceStatus = problem;
            return;
        }

        _project = project.WithDocument(data, PageMap.Even(_meter, _score.End, PdfDocument.PageCount(data)));
        _performanceStatus = Text["sources.pdfKept"];
        await CheckOriginalsAsync(changed: true);
    }

    /// <summary>The bytes of a PDF the player picked, or why they cannot be kept: too large, or not a PDF.</summary>
    private async Task<(byte[]? Data, string? Problem)> PickedPdfAsync(IBrowserFile file)
    {
        if (PdfDocument.CheckSize(file.Size) is { } tooLarge)
        {
            return (null, tooLarge.Text(Words));
        }

        byte[] data;
        await using (Stream stream = file.OpenReadStream(PdfDocument.MaxFileBytes))
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            data = memory.ToArray();
        }

        return PdfDocument.IsPdf(data) ? (data, null) : (null, Text["sources.notPdf"]);
    }

    /// <summary>
    /// The notes the scan server read from the PDF kept in the piece, added as another version from a score (0022), with the
    /// PDF's pages linked to these bars by the bar numbers the scan began each page with. Returns why they cannot be used.
    /// </summary>
    private async Task<string?> ReadDocumentVersionAsync(ScanResult result)
    {
        if (_project is not { Document: not null } project || result.MusicXml is not { } musicXml || _meter is null || _score is null)
        {
            return result.Problem;
        }

        // The engine's mistakes put right, as in document.read and sources.documentNotes (#495 BUG-03).
        musicXml = ScanRepair.Repair(musicXml);
        string fileName = Path.ChangeExtension(project.DocumentFileName, ".musicxml");
        if (PieceImporter.Import(fileName, musicXml).Piece is not { } read)
        {
            return Text["pdf.readNone"];
        }

        VersionFileResult version = VersionFile.Read(fileName, musicXml);
        if (version.Rejection is { } rejection)
        {
            return $"{rejection.Text(Words)} {rejection.TipIn(Words)}".Trim();
        }

        IReadOnlyList<PlayedNote> played;
        try
        {
            played = version.Midi is { } midi ? PlayedNotes.FromMidi(midi) : [];
        }
        catch (Exception exception) when (exception is MidiImportException or ArgumentException or InvalidDataException or FormatException)
        {
            return Text["pdf.readNone"];
        }

        if (played.Count == 0)
        {
            return Text["performance.noNotes"];
        }

        int pages = Math.Max(1, result.Pages > 0 ? result.Pages : project.DocumentPages?.Pages ?? 1);
        project = project with { DocumentPages = PageMap.Of(read, pages).Through(read.Meter, _meter, _score.End) };
        _readingDocument = false;
        await AddVersionAsync(project, project.DocumentFileName, PerformanceSource.Score, version.Midi!, played);
        StateHasChanged();
        return null;
    }
}