using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The drafts' calls across open pieces (decision record 0034, version 1.1): a fragment is selected in any view of notes,
/// the working notes, Notes or a version opened to be shown, or another piece, and goes into a draft of the piece the
/// view was opened from, or of the open piece "into" names; a draft opens under a new number only to be shown.
/// </summary>
public sealed partial class MusicCoreFacade
{
    private string CallOnDrafts(int number, OpenPiece open, string name, Request fields)
    {
        (int ownerNumber, OpenPiece owner) = Owner(number, open, fields);
        return name switch
        {
            "drafts.sheet" => DraftRequest.Sheet(owner, Origin(open), fields),
            "drafts.add" => DraftRequest.Add(owner, Origin(open), fields),
            "drafts.open" => OpenDraft(ownerNumber, owner, fields),
            "drafts.rename" => DraftRequest.Rename(owner, fields),
            "drafts.remove" => DraftRequest.Remove(owner, fields),
            "drafts.techniques" => DraftTakeRequest.Techniques(owner, fields, Working),
            "drafts.take" => DraftTakeRequest.Take(owner, fields),
            "drafts.leave" => DraftTakeRequest.Leave(owner, fields),
            "drafts.takeSure" => DraftTakeRequest.TakeSure(owner, fields),
            "drafts.takeFrom" => DraftTakeRequest.TakeFrom(owner, fields),
            "drafts.undo" => DraftTakeRequest.Undo(owner, fields),
            _ => throw new FacadeException("unknownCall", $"There is no call \"{name}\"."),
        };
    }

    /// <summary>The piece whose drafts a call works on: "into", else the piece a view was opened from, else the piece itself; never one open only to be shown.</summary>
    private (int Number, OpenPiece Piece) Owner(int number, OpenPiece open, Request fields)
    {
        int owner = fields.Int("into") ?? open.ShownFrom?.Piece ?? number;
        OpenPiece piece = _pieces.TryGetValue(owner, out OpenPiece? found) ? found : throw new FacadeException("noPiece", $"Piece {owner} is not open.");
        return piece.ReadOnly ? throw new FacadeException("readOnly", "Drafts belong to a piece open to work on, not to notes open only to be shown.") : (owner, piece);
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

    /// <summary>The piece open to work on whose id is <paramref name="projectId"/>, as a variant from another piece names it; null when none is.</summary>
    private Project? Working(string projectId) =>
        _pieces.Values.FirstOrDefault(each => !each.ReadOnly && each.Project.Id == projectId)?.Project;
}