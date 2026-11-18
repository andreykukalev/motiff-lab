using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The piece's sources by the layers of 0030 (version 0.6, step 5): the Originals and Notes with their states and the
/// views of the switch in one call, and the Sources sheet's operations. The calls of version 0.5 ("sources",
/// "sources.document", …) stay as they were. «Сделать рабочими» changes the notes the piece is open on, so the facade
/// opens it again (<c>sources.makeWorking</c>, <see cref="MusicCoreFacade"/>).
/// </summary>
internal sealed partial class OpenPiece
{
    private string? CallOnSourceLayers(string name, Request request) => name switch
    {
        "sources.notes.recognise" => RecogniseAgain(request),
        "sources.notes.hear" => HearRecording(request),
        _ => SourceRequest.Call(name, request, () => Project, project => Project = project, pages => PageMap.Even(Piece.Meter, Piece.End, pages)),
    };

    private string Changed(Request request, Func<Project, Project> change, Action<Utf8JsonWriter> more) =>
        SourceRequest.Changed(request, () => Project, project => Project = project, change, more);

    /// <summary>
    /// «Распознать ещё раз»: "original" (the PDF's id), "musicXml" (base64) the scan server read from it, "engine",
    /// "pages", "reports" (the scan server's page reports) and "now": other Notes beside the working ones, which keep their
    /// corrections. The engine's mistakes are put right first (<see cref="ScanRepair"/>). Answers the sources with their "id".
    /// </summary>
    private string RecogniseAgain(Request request)
    {
        byte[] musicXml = ScanRepair.Repair(SourceRequest.Base64(request.Required("musicXml"), "musicXml"));
        PieceImportResult imported = PieceImporter.Import("notes.musicxml", musicXml);
        if (imported.Piece is not { } read)
        {
            throw new FacadeException("import", imported.Rejection?.Text(Words) ?? Words.T("No notes could be read from these pages.", "На этих страницах не удалось прочитать нот."));
        }

        string id = Project.NextNotesId();
        PageMap pages = PageMap.Of(read, Math.Max(1, request.Int("pages") ?? 1));
        List<PageReport>? reports = ScanRequest.ReportsIn(request);
        return Changed(request, project => project.RecognisedAgain(request.Required("original"), musicXml, request.Required("engine"), pages, Now(request), reports), json => json.WriteString("id", id));
    }

    /// <summary>
    /// «Распознать» on a recording already added: "original" (the recording's id) and the notes the transcription heard in
    /// the stretch sent («Что распознать»), as for transcribe ("milliseconds", "parts", "levels", "engine", "sentFromMs",
    /// "tempo"), with "now". Other Notes beside the working ones, their provenance naming the stretch
    /// ("sentFromMs" to "sentFromMs" + "milliseconds"). Answers the sources with their "id".
    /// </summary>
    private string HearRecording(Request request)
    {
        (byte[] midi, TranscriptionOrigin origin, IReadOnlyList<CheckFlag> check) = Heard(Path.ChangeExtension(Project.FileName, ".mid"), request, Words);
        long from = request.Long("sentFromMs") ?? 0;
        (long, long) stretch = (from, from + request.Long("milliseconds")!.Value);
        string id = Project.NextNotesId();
        return Changed(request, project => project.HeardFrom(request.Required("original"), midi, origin, check, Now(request), stretch), json => json.WriteString("id", id));
    }
}