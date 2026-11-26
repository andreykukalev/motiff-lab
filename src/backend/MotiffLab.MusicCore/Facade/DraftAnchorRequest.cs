using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The calls of a variant's anchors (decision record 0034, section 2; the plan's answer 4): «Не тот такт? Сопоставить…»
/// confirms that a tick of the place's base and a tick of the variant, or the time of its recording, sound together, and
/// «Открепить» takes one away. The alignment, the place's result and its приёмы follow on every call; anchors are the
/// variant's state, not a step, so drafts.undo takes either back while the piece stays open.
/// </summary>
internal static class DraftAnchorRequest
{
    /// <summary>
    /// «Сопоставить»: on the variant "variant" of the place "place" of the draft "draft", "anchor" {"base" (a tick of the
    /// place's base) and "variant" (a tick of the variant) or "recording" (milliseconds of the recording the variant was
    /// heard in, on its clock)}, in place of any at either tick. Answers the sources with "draft", "place", "variant" and
    /// "dropped" (none). <paramref name="pieces"/> gives another piece open to work on, for a variant heard in its recording.
    /// </summary>
    public static string Confirm(OpenPiece owner, Request request, Func<string, Project?> pieces)
    {
        (Draft draft, string place, string variant) = VariantOf(owner, request);
        Request anchor = request.Object("anchor");
        Tick at = Base(anchor);
        Tick inVariant = anchor.Long("variant") is { } tick ? new Tick(tick)
            : anchor.Long("recording") is { } milliseconds ? Heard(owner.Project, draft, place, variant, milliseconds, pieces)
            : throw FacadeException.BadRequest("Send \"anchor\" with \"variant\" (a tick) or \"recording\" (milliseconds).");
        return DraftRequest.Restructured(owner, request, draft, place, variant, each => DraftAnchors.Confirm(each, place, variant, new DraftAnchor(at, inVariant)));
    }

    /// <summary>«Открепить»: the anchor at "base" (the place's base tick) of the variant "variant" taken away. Answers as <see cref="Confirm"/>.</summary>
    public static string Remove(OpenPiece owner, Request request)
    {
        (Draft draft, string place, string variant) = VariantOf(owner, request);
        Tick at = Base(request);
        return DraftRequest.Restructured(owner, request, draft, place, variant, each => DraftAnchors.Remove(each, place, variant, at));
    }

    /// <summary>The variant's tick played at <paramref name="milliseconds"/> of its recording; unavailable for one not heard or not at hand.</summary>
    private static Tick Heard(Project project, Draft draft, string place, string variant, long milliseconds, Func<string, Project?> pieces)
    {
        DraftVariant found = draft.FindPlace(place)?.Find(variant) ?? throw FacadeException.BadRequest($"The place {place} has no variant {variant}.");
        return DraftTakeRequest.RecordingOf(found, project, pieces)?.VariantTickAt(milliseconds) ?? throw FacadeException.Unavailable("The variant has no recording at hand at that time.");
    }

    private static Tick Base(Request request) => new(request.Long("base") ?? throw FacadeException.BadRequest("The request needs \"base\", a tick of the place's base."));

    private static (Draft Draft, string Place, string Variant) VariantOf(OpenPiece owner, Request request)
    {
        string id = request.Required("draft");
        Draft draft = owner.Project.FindDraft(id) ?? throw FacadeException.BadRequest($"There is no draft {id}.");
        return (draft, request.Required("place"), request.Required("variant"));
    }
}