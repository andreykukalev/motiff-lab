using MotiffLab.MusicCore.Projects.Drafts;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// A приём carried into another place (version 1.4, Andrei's answer of 2026-11-26): music-core builds a variant of that
/// place with the приём done where its shape fits (<see cref="TechniqueCarry"/>) and puts it in; the player then takes it
/// as any variant's приёмы. The variant is the draft's structure, not a step: drafts.undo takes it back while the piece stays open.
/// </summary>
internal static class DraftCarryRequest
{
    /// <summary>
    /// Where the приём "technique" of the place "place" of the draft "draft", read at its bar "bar" (its first when none), can
    /// be carried, nothing changed: "draft", "place", "technique" and "places" [{"place", "label" (as the draft's map names
    /// it), "bars" (the place's bars, from 1, where it fits; empty where it fits nowhere)}], each other place that is not a
    /// bridge and that it was not carried into already, in the draft's order. Bars put in are not carried (unavailable); no
    /// such place, приём or bar is a bad request.
    /// </summary>
    public static string Places(OpenPiece owner, Request request)
    {
        (Draft draft, string placeId) = DraftTakeRequest.PlaceOf(owner, request);
        int technique = request.RequiredInt("technique");
        IReadOnlyList<(DraftPlace Place, IReadOnlyList<int> Bars)> targets = Refused(() => TechniqueCarry.Targets(draft, placeId, technique, request.Int("bar"), owner.Words));
        return Answer.Write(json =>
        {
            json.WriteString("draft", draft.Id);
            json.WriteString("place", placeId);
            json.WriteNumber("technique", technique);
            json.WriteStartArray("places");
            foreach ((DraftPlace place, IReadOnlyList<int> bars) in targets)
            {
                json.WriteStartObject();
                json.WriteString("place", place.Id);
                json.WriteString("label", DraftLabels.Place(place, draft.Places.ToList().IndexOf(place), owner.Words));
                JsonWrite.Numbers(json, "bars", bars);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        });
    }

    /// <summary>
    /// «Перенести приём»: the приём "technique" of the place "place" of the draft "draft" (read at "bar", its first when none)
    /// carried into the place "to" as its variant, "now" its time. Unavailable (in words) for bars put in, a bridge place, a
    /// place where it fits nowhere, or one it was carried into already. Answers the sources with "draft", "place" ("to"),
    /// "variant" (the new variant), "dropped" (none) and "bars" (the place's bars it was carried into).
    /// </summary>
    public static string Carry(OpenPiece owner, Request request)
    {
        (Draft draft, string placeId) = DraftTakeRequest.PlaceOf(owner, request);
        int technique = request.RequiredInt("technique");
        string to = request.Required("to");
        (Draft carried, IReadOnlyList<int> bars) = Refused(() => TechniqueCarry.Put(draft, placeId, technique, request.Int("bar"), to, SourceRequest.Now(request), owner.Words));
        return DraftRequest.Restructured(owner, request, draft, to, draft.NextVariantId(), _ => carried, (json, _) => JsonWrite.Numbers(json, "bars", bars));
    }

    /// <summary>What <paramref name="make"/> gives; no such place, приём or bar is a bad request, what cannot be carried unavailable, its message the player's.</summary>
    private static T Refused<T>(Func<T> make)
    {
        try
        {
            return make();
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw FacadeException.Unavailable(exception.Message);
        }
    }
}