using System.Text;

using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The drafts' calls across open pieces (decision record 0034, version 1.1): a fragment is selected in any view of notes,
/// the working notes, Notes or a version opened to be shown, or another piece, and goes into a draft of the piece the
/// view was opened from, or of the open piece "into" names, or (version 1.2, collage) of another piece whose project file
/// the app sends as "file", which is not opened; a draft opens under a new number only to be shown.
/// </summary>
public sealed partial class MusicCoreFacade
{
    private string CallOnDrafts(int number, OpenPiece open, string name, Request fields)
    {
        if (name == "drafts.add" && fields.Has("file"))
        {
            return fields.Has("into") ? throw FacadeException.BadRequest("Send \"into\" or \"file\", not both.") : DraftRequest.AddTo(Elsewhere(fields), Origin(open), fields);
        }

        (int ownerNumber, OpenPiece owner) = Owner(number, open, fields);
        return CallRegistry.OnDrafts.TryGetValue(name, out FacadeCall<DraftCall>? call)
            ? call.Run(new DraftCall(this, open, ownerNumber, owner), fields)
            : throw new FacadeException("unknownCall", $"There is no call \"{name}\".");
    }

    /// <summary>The drafts' calls (<see cref="CallRegistry.OnDrafts"/>), each on the piece whose drafts they are.</summary>
    internal static readonly FacadeCall<DraftCall>[] DraftCalls =
    [
        new("drafts.sheet", static (call, fields) => DraftRequest.Sheet(call.Owner, call.Facade.Origin(call.Open), fields)),
        new("drafts.add", static (call, fields) => DraftRequest.Add(call.Owner, call.Facade.Origin(call.Open), fields)),
        new("drafts.open", static (call, fields) => call.Facade.OpenDraft(call.OwnerNumber, call.Owner, fields)),
        new("drafts.rename", static (call, fields) => DraftRequest.Rename(call.Owner, fields)),
        new("drafts.remove", static (call, fields) => DraftRequest.Remove(call.Owner, fields)),
        new("drafts.place", static (call, fields) => DraftRequest.Place(call.Owner, fields)),
        new("drafts.base", static (call, fields) => DraftRequest.Base(call.Owner, fields)),
        new("drafts.movePlace", static (call, fields) => DraftRequest.Move(call.Owner, fields)),
        new("drafts.findOnRecording", static (call, fields) => DraftRecordingRequest.Find(call.Owner, fields)),
        new("drafts.addFromRecording", static (call, fields) => DraftRecordingRequest.Add(call.Owner, fields)),
        new("drafts.length", static (call, fields) => DraftLengthRequest.Length(call.Owner, fields, call.Facade.Working)),
        new("drafts.resize", static (call, fields) => DraftLengthRequest.Resize(call.Owner, fields, call.Facade.Working)),
        new("drafts.carryPlaces", static (call, fields) => DraftCarryRequest.Places(call.Owner, fields)),
        new("drafts.carry", static (call, fields) => DraftCarryRequest.Carry(call.Owner, fields)),
        new("drafts.techniques", static (call, fields) => DraftTakeRequest.Techniques(call.Owner, fields, call.Facade.Working)),
        new("drafts.take", static (call, fields) => DraftTakeRequest.Take(call.Owner, fields)),
        new("drafts.leave", static (call, fields) => DraftTakeRequest.Leave(call.Owner, fields)),
        new("drafts.takeSure", static (call, fields) => DraftTakeRequest.TakeSure(call.Owner, fields)),
        new("drafts.takeFrom", static (call, fields) => DraftTakeRequest.TakeFrom(call.Owner, fields)),
        new("drafts.undo", static (call, fields) => DraftTakeRequest.Undo(call.Owner, fields)),
        new("drafts.seams", static (call, fields) => DraftSeamRequest.Seams(call.Owner, fields)),
        new("drafts.transpose", static (call, fields) => DraftSeamRequest.Transpose(call.Owner, fields)),
        new("drafts.tempo", static (call, fields) => DraftSeamRequest.Tempo(call.Owner, fields)),
        new("drafts.leaveSeam", static (call, fields) => DraftSeamRequest.Leave(call.Owner, fields)),
        new("drafts.bridge", static (call, fields) => DraftBridgeRequest.Put(call.Owner, fields)),
        new("drafts.leaveBridge", static (call, fields) => DraftBridgeRequest.Leave(call.Owner, fields)),
        new("drafts.anchor", static (call, fields) => DraftAnchorRequest.Confirm(call.Owner, fields, call.Facade.Working)),
        new("drafts.unanchor", static (call, fields) => DraftAnchorRequest.Remove(call.Owner, fields)),
        new("drafts.context", static (call, fields) => DraftAssistantRequest.Context(call.Owner, fields)),
        new("drafts.call", static (call, fields) => DraftAssistantRequest.Call(call.Owner, fields)),
        new("drafts.accept", static (call, fields) => DraftAssistantRequest.Accept(call.Owner, fields)),
        new("drafts.proposal", static (call, fields) => call.Facade.OpenProposal(call.OwnerNumber, call.Owner, fields)),
    ];

    /// <summary>The piece whose drafts a call works on: "into", else the piece a view was opened from, else the piece itself; never one open only to be shown.</summary>
    private (int Number, OpenPiece Piece) Owner(int number, OpenPiece open, Request fields)
    {
        int owner = fields.Int("into") ?? open.ShownFrom?.Piece ?? number;
        OpenPiece piece = _pieces.TryGetValue(owner, out OpenPiece? found) ? found : throw new FacadeException("noPiece", $"Piece {owner} is not open.");
        return piece.ReadOnly ? throw new FacadeException("readOnly", "Drafts belong to a piece open to work on, not to notes open only to be shown.") : (owner, piece);
    }

    /// <summary>
    /// The project of "file", another piece's project file the app keeps (version 1.2, collage); never a piece open here to
    /// work on, whose drafts "into" names, so the app never holds two copies of one piece that differ.
    /// </summary>
    private Project Elsewhere(Request fields)
    {
        Project project = ProjectFile.Read(Encoding.UTF8.GetBytes(fields.Required("file")));
        return Working(project.Id) is null ? project : throw FacadeException.BadRequest("That piece is open here: send \"into\", its number, in place of its file.");
    }

    /// <summary>Where a fragment selected in <paramref name="open"/> comes from: the Notes it shows and their provenance in the piece they belong to.</summary>
    private FragmentOrigin Origin(OpenPiece open)
    {
        if (open.ShownFrom is not { } shown)
        {
            return new FragmentOrigin(open, open.Project.NotesId, open.Project.WorkingProvenance, open.Project.Id);
        }

        if (shown.Draft)
        {
            throw new FacadeException("readOnly", "A fragment is taken from notes, not from a draft.");
        }

        Project from = _pieces.TryGetValue(shown.Piece, out OpenPiece? piece) ? piece.Project : throw new FacadeException("noPiece", $"Piece {shown.Piece}, which these notes were opened from, is not open.");
        SourceNotes notes = PieceViews.Of(from, HoldingDevice.Ipad).Notes.FirstOrDefault(each => each.Id == shown.Id)
            ?? throw FacadeException.Unavailable($"The piece has no Notes {shown.Id} any more.");
        return new FragmentOrigin(open, notes.Id, notes.Provenance, from.Id);
    }

    /// <summary>
    /// The draft view (the switch's tab of a draft): the draft "draft" of the piece, its places' results in order as one score
    /// (<see cref="Project.ViewOfDraft"/>), open under a new number only to be shown, so piece, notation, playback and roll
    /// answer for it. Answers as <c>open</c> does, with "readOnly": true and "draft" (<see cref="DraftRequest.View"/>). The
    /// summary is that of the score as an imported MIDI ("origin" imported, kind midi), as a version's view is: where the
    /// draft came from is in "draft" (its places' labels), never in those fields. A draft with no notes yet is unavailable.
    /// With "variant" (version 1.1, the iPad's step 6), that variant's own music alone, answered with "variant", its id, in
    /// place of "draft": a приём heard as the variant has it, over an occurrence's "inVariant" ticks.
    /// </summary>
    private string OpenDraft(int number, OpenPiece owner, Request fields)
    {
        string id = fields.Required("draft");
        Draft? draft = owner.Project.FindDraft(id);
        string? variant = fields.Text("variant");
        return OpenShown(
            owner,
            () => owner.Project.ViewOfDraft(id, variant),
            new ShownView(number, id, Draft: true),
            json =>
            {
                if (variant is null)
                {
                    DraftRequest.View(json, draft!, owner.Words);
                }
                else
                {
                    json.WriteString("variant", variant);
                }
            });
    }

    /// <summary>
    /// «▶ После» of what the assistant proposes in a draft (version 1.2, decision record 0034, section 6): the draft as the
    /// call "tool" with "parameters" on the place would leave it (<see cref="DraftAssistantRequest.Proposal"/>), open under a
    /// new number only to be shown, answered as drafts.open is; the draft itself does not change. «▶ До» is drafts.open.
    /// </summary>
    private string OpenProposal(int number, OpenPiece owner, Request fields)
    {
        Draft after = DraftAssistantRequest.Proposal(owner, fields).After;
        return OpenShown(owner, () => owner.Project.ChangeDraft(after.Id, _ => after).ViewOfDraft(after.Id), new ShownView(number, after.Id, Draft: true), json => DraftRequest.View(json, after, owner.Words));
    }

    /// <summary>The piece open to work on whose id is <paramref name="projectId"/>, as a variant from another piece names it; null when none is.</summary>
    private Project? Working(string projectId) =>
        _pieces.Values.FirstOrDefault(each => !each.ReadOnly && each.Project.Id == projectId)?.Project;
}