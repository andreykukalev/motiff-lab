using System.Text.Json;

using MotiffLab.MusicCore.Projects.Drafts;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The calls of a bridge at a seam (decision record 0034, section 5; the plan's answer 2): «Связка» puts the candidates as
/// the variants of a new place between the two, «Сделать основой» (drafts.base) chooses one, «Оставить» removes the place.
/// The place is the draft's structure, not a step: drafts.undo takes either back while the piece stays open.
/// </summary>
internal static class DraftBridgeRequest
{
    /// <summary>
    /// «Связка · 3 кандидата»: the candidates of the seam before the place "place" of the draft "draft" as a new bridge place
    /// just before it, the first its base, "now" their time. Answers the sources with "draft", "place" (the new place),
    /// "variant" (its base), "dropped" (none) and "candidates" [{"variant", "kind" (dominant, commonChord, bassWalk),
    /// "label" («Доминанта»), "explanation" (the chords it sounds)}] in order; unavailable where the keys do not differ.
    /// </summary>
    public static string Put(OpenPiece owner, Request request)
    {
        Draft draft = DraftOf(owner, request);
        string after = request.Required("place");
        return DraftRequest.Restructured(
            owner,
            request,
            draft,
            changed => (Made(draft, changed).Id, Made(draft, changed).Base),
            each => DraftBridges.Put(each, after, request.Now(), owner.Ids),
            (json, changed) => Candidates(json, Made(draft, changed), owner.Words));
    }

    /// <summary>The place <paramref name="changed"/> has that <paramref name="draft"/> had not: the bridge put.</summary>
    private static DraftPlace Made(Draft draft, Draft changed) => changed.Places.Single(place => draft.FindPlace(place.Id) is null);

    /// <summary>
    /// «Оставить» in a bridge place: the place "place" of the draft "draft" removed with its candidates and its steps.
    /// Answers the sources with "draft", "place" and "variant" (the place and its base, gone) and "dropped" (its steps).
    /// </summary>
    public static string Leave(OpenPiece owner, Request request)
    {
        Draft draft = DraftOf(owner, request);
        string id = request.Required("place");
        DraftPlace place = draft.FindPlace(id) ?? throw FacadeException.BadRequest($"The draft has no place {id}.");
        return DraftRequest.Restructured(owner, request, draft, id, place.Base, each => DraftBridges.Leave(each, id));
    }

    private static void Candidates(Utf8JsonWriter json, DraftPlace place, Words words)
    {
        json.WriteStartArray("candidates");
        foreach (DraftVariant variant in place.Variants)
        {
            json.WriteStartObject();
            json.WriteString("variant", variant.Id);
            Assistant.Drafts.DraftTools.WriteCandidate(json, variant, words);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static Draft DraftOf(OpenPiece owner, Request request)
    {
        string id = request.Required("draft");
        return owner.Project.FindDraft(id) ?? throw FacadeException.BadRequest($"There is no draft {id}.");
    }
}