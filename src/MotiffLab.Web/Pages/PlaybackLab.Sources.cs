using Microsoft.AspNetCore.Components.Forms;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
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
    /// <summary>The version just added: its four next steps show until one is chosen or another version is shown.</summary>
    private string? _addedVersion;

    /// <summary>The bars Take everything sure took last, from which version, to give back with one tap.</summary>
    private (string Version, IReadOnlyList<int> Bars)? _takenAll;

    /// <summary>Whether Read the notes of the PDF as a version is open under the PDF's row.</summary>
    private bool _readingDocument;

    /// <summary>Whether the choices of Add a source show.</summary>
    private bool _addingSource;

    private PieceSources? Sources => _project is { } project ? PieceSources.Of(project) : null;

    /// <summary>How much of the shown version lines up with the notes, in whole per cent of its bars; null with no comparison.</summary>
    private int? AlignedPercent => ShownComparison is { Bars.Count: > 0 } comparison
        ? (comparison.Bars.Count - UnalignedBarCount) * 100 / comparison.Bars.Count
        : null;

    /// <summary>The bars of the shown version Take everything sure would take: lined up, sure enough, not dismissed or taken.</summary>
    private int SureBarCount => DifferingBars.Count(bar => bar.Aligned && bar.Confidence >= PerformanceTake.AutomaticConfidence);

    /// <summary>Whether the version just added still offers its four next steps.</summary>
    private bool OffersNextSteps => _addedVersion is { } id && ShownPerformance?.Id == id;

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

    /// <summary>Compare: the version's notes show over the score, its markers above the bars it has otherwise.</summary>
    private async Task CompareVersionAsync()
    {
        _addedVersion = null;
        await SetLayersAsync(["version"], true);
    }

    /// <summary>
    /// Hear it alone: the phrase with the first bar the version has otherwise, in focus, played as in the version (the
    /// other version is heard only in focus, 0022 section 5).
    /// </summary>
    private async Task HearVersionAloneAsync()
    {
        if (FirstDifferingSpan() is not { } span)
        {
            return;
        }

        _addedVersion = null;
        _performanceOpen = false;
        await SelectAsync(_phrases?.Covering(span)?.Span ?? span, note: null);
        EnterFocus();
        await HearVersionAsync(true);
    }

    /// <summary>
    /// Take everything sure: every bar the comparison lines up with at least 80 % sure, not dismissed or taken already, comes
    /// into the notes at once; Give back returns them.
    /// </summary>
    private async Task TakeAllSureAsync()
    {
        if (_project is not { } project || ShownPerformance is not { } performance || ShownComparison is not { } comparison || _meter is null)
        {
            return;
        }

        IReadOnlyList<TakenBar> taken = PerformanceTake.TakeAll(performance, comparison, _meter, PrintedNotes);
        _addedVersion = null;
        if (taken.Count == 0)
        {
            _performanceStatus = Text["sources.nothingSure"];
            return;
        }

        _project = project.ChangePerformance(performance.Id, kept => kept.TakeBars(taken));
        _takenAll = (performance.Id, [.. taken.Select(bar => bar.Bar)]);
        _performanceStatus = Text.Count("sources.takenAll", taken.Count);
        await ApplyEditsAsync(_edits);
    }

    /// <summary>Give back: the bars Take everything sure took come back as they were.</summary>
    private async Task GiveBackTakenAllAsync()
    {
        if (_project is not { } project || _takenAll is not { } last || project.Performances.All(kept => kept.Id != last.Version))
        {
            _takenAll = null;
            return;
        }

        _project = project.ChangePerformance(last.Version, kept => kept.Restore(last.Bars));
        _takenAll = null;
        _performanceStatus = Text.Count("sources.givenBack", last.Bars.Count);
        await ApplyEditsAsync(_edits);
    }

    /// <summary>Adjust it myself: the first bar the version has otherwise is selected and the roll opens on it, editing.</summary>
    private async Task AdjustVersionAsync()
    {
        if (FirstDifferingSpan() is not { } span)
        {
            return;
        }

        _addedVersion = null;
        _performanceOpen = false;
        await SelectAsync(span, note: null);
        _rollOpen = true;
        await BeginNoteEditAsync();
    }

    /// <summary>The first bar the shown version has otherwise, or the first bar when it agrees everywhere.</summary>
    private TickSpan? FirstDifferingSpan()
    {
        if (_meter is null || _score is null)
        {
            return null;
        }

        int bar = DifferingBars.FirstOrDefault()?.Bar ?? _meter.FirstBarNumber;
        return MotifSelector.Bars(_meter, bar, bar, _score.End);
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

        IBrowserFile file = args.File;
        if (PdfDocument.CheckSize(file.Size) is { } tooLarge)
        {
            _performanceStatus = tooLarge.Text(Words);
            return;
        }

        byte[] data;
        await using (Stream stream = file.OpenReadStream(PdfDocument.MaxFileBytes))
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            data = memory.ToArray();
        }

        if (!PdfDocument.IsPdf(data))
        {
            _performanceStatus = Text["sources.notPdf"];
            return;
        }

        _project = project.WithDocument(data, PageMap.Even(_meter, _score.End, PdfDocument.PageCount(data)));
        _performanceStatus = Text["sources.pdfKept"];
        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
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