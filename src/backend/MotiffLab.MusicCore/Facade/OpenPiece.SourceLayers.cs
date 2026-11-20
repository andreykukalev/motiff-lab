using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

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
        "sources.notes.hear" => SourceRequest.Hear(request, () => Project, project => Project = project, Words),
        _ => SourceRequest.Call(name, request, () => Project, project => Project = project, pages => PageMap.Even(Piece.Meter, Piece.End, pages), Words),
    };

    /// <summary>
    /// The drafts as they were before each change of their structure made while the piece is open (drafts.place, drafts.base),
    /// the latest last: a draft's structure is its state, not a step of its journal, so only these let drafts.undo take
    /// such a change back; they are never in the file.
    /// </summary>
    private readonly List<Draft> _draftsBefore = [];

    /// <summary>Keeps <paramref name="before"/>, the draft as it was before a change of its structure, for drafts.undo.</summary>
    internal void KeepDraftBefore(Draft before) => _draftsBefore.Add(before);

    /// <summary>Forgets what was kept of the draft <paramref name="id"/> for drafts.undo: it changed otherwise (added to, renamed, removed).</summary>
    internal void ForgetDraftsBefore(string id) => _draftsBefore.RemoveAll(each => each.Id == id);

    /// <summary>
    /// What drafts.undo takes back of <paramref name="now"/>: the draft before its latest change of structure, when no step
    /// of the journal was added after that change (its number at or above the one that change left next), its step numbers
    /// counting on; null when the last change was a step. That kept draft is forgotten.
    /// </summary>
    internal Draft? DraftBefore(Draft now)
    {
        int index = _draftsBefore.FindLastIndex(each => each.Id == now.Id);
        if (index < 0 || now.Steps.Any(step => step.Number >= _draftsBefore[index].Next))
        {
            return null;
        }

        Draft before = _draftsBefore[index];
        _draftsBefore.RemoveAt(index);
        return before with { Next = now.Next };
    }

    /// <summary>The piece changed by one of the Sources sheet's operations, a draft's among them, answered as the sources with what <paramref name="more"/> adds.</summary>
    internal string Changed(Request request, Func<Project, Project> change, Action<Utf8JsonWriter> more) =>
        SourceRequest.Changed(request, () => Project, project => Project = project, Words, change, more);

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
}