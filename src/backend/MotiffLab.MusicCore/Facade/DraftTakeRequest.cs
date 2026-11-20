using System.Text.Json;

using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The calls of a place's приёмы (decision record 0034, section 4; the iPad's step 6): the приёмы of a place with their
/// numbers, positions and labels, «Взять везде · N», «Только здесь», «Оставить», «Взять всё уверенное», a hand or a
/// selection taken from a variant, and undo. Each call that changes the draft answers the sources
/// (<see cref="SourceRequest.Layers"/>) with what it did; the draft view is opened again to show the result.
/// </summary>
internal static class DraftTakeRequest
{
    /// <summary>
    /// The приёмы of the place "place" of the draft "draft": "draft", "place", "total" (how many), "taken" (how many are
    /// taken), "sure" (how many sure ones are open: what «Взять всё уверенное» takes) and "techniques" [{"number", "variant",
    /// "kind" (fill, bass, simplify, ornament, octave, rhythm, insertBars, other), "label" («октава»), "inferred" (always
    /// true: the engine found the kind), "confidence" (per cent, the lowest of its occurrences: the bar's notes found in
    /// place, the приём's own left aside), "sure", "state" (open, taken, left), "occurrences" [{"bar" (the place's bar,
    /// from 1, as "take" names it), "firstBar", "lastBar" (the draft's own numbers), "onset", "end" (the draft's ticks; for
    /// bars not yet put in, where they would go, onset and end the same), "confidence", "state"}]}] in number order.
    /// </summary>
    public static string Techniques(OpenPiece owner, Request request)
    {
        (Draft draft, string placeId) = PlaceOf(owner, request);
        IReadOnlyList<PlaceTechnique> techniques = draft.TechniquesOf(placeId);
        DraftMusic score = DraftMusic.Of(draft);
        long start = score.Places.First(each => each.Place == placeId).Span.Onset.Value;
        PlaceResult result = draft.ResultOf(placeId);
        return Answer.Write(json =>
        {
            json.WriteString("draft", draft.Id);
            json.WriteString("place", placeId);
            json.WriteNumber("total", techniques.Count);
            json.WriteNumber("taken", techniques.Count(static technique => technique.State == TechniqueState.Taken));
            json.WriteNumber("sure", techniques.Count(static technique => technique.IsSure && technique.State == TechniqueState.Open));
            json.WriteStartArray("techniques");
            foreach (PlaceTechnique technique in techniques)
            {
                json.WriteStartObject();
                json.WriteNumber("number", technique.Number);
                json.WriteString("variant", technique.Variant);
                json.WriteString("kind", Answer.Camel(technique.Kind));
                json.WriteString("label", DraftLabels.Technique(technique.Kind, owner.Words));
                json.WriteBoolean("inferred", true);
                json.WriteNumber("confidence", technique.Confidence);
                json.WriteBoolean("sure", technique.IsSure);
                json.WriteString("state", Answer.Camel(technique.State));
                json.WriteStartArray("occurrences");
                foreach (TechniqueOccurrence occurrence in technique.Occurrences)
                {
                    Occurrence(json, occurrence, score.Music, start, result);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
        });
    }

    /// <summary>
    /// «Взять везде · N» or, with "bar" (an occurrence's), «Только здесь»: the приём "technique" of the place "place" of the
    /// draft "draft" taken, "now" its time. Answers the sources with "draft", "place" and "step", its number.
    /// </summary>
    public static string Take(OpenPiece owner, Request request) =>
        Stepped(owner, request, (draft, place, now) => DraftTakes.Take(draft, place, request.RequiredInt("technique"), request.Int("bar"), now));

    /// <summary>«Оставить»: as <see cref="Take"/>, the приём kept as in the base.</summary>
    public static string Leave(OpenPiece owner, Request request) =>
        Stepped(owner, request, (draft, place, now) => DraftTakes.Leave(draft, place, request.RequiredInt("technique"), request.Int("bar"), now));

    /// <summary>
    /// «Правая рука из варианта…», «Взять отсюда»: "part" (all, the default, right or left) of the variant "variant" taken into
    /// the place over "span" {onset, end} of the draft's ticks, or the whole place. Answers as <see cref="Take"/>.
    /// </summary>
    public static string TakeFrom(OpenPiece owner, Request request) =>
        Stepped(owner, request, (draft, place, now) => DraftTakes.TakeFrom(draft, place, request.Required("variant"), request.Enum<DraftPart>("part") ?? DraftPart.All, InPlace(draft, place, request.Span("span")), now));

    /// <summary>«Взять всё уверенное»: every sure open приём of the place taken, as one group. Answers the sources with "draft", "place" and "taken", their numbers.</summary>
    public static string TakeSure(OpenPiece owner, Request request)
    {
        (Draft draft, string placeId) = PlaceOf(owner, request);
        IReadOnlyList<int> taken = [];
        return owner.Changed(
            request,
            piece => piece.ChangeDraft(draft.Id, each =>
            {
                (Draft changed, taken) = DraftTakes.TakeSure(each, placeId, SourceRequest.Now(request));
                return changed;
            }),
            json =>
            {
                Ids(json, draft.Id, placeId);
                Answer.Numbers(json, "taken", taken);
            });
    }

    /// <summary>Undo in a draft: its last step goes, with the steps accepted with it. Answers the sources with "draft" and "undone", the steps' numbers.</summary>
    public static string Undo(OpenPiece owner, Request request)
    {
        string id = request.Required("draft");
        IReadOnlyList<int> undone = [];
        return owner.Changed(
            request,
            piece => piece.ChangeDraft(id, each =>
            {
                Draft changed = each.Undone();
                undone = [.. each.Steps.Select(static step => step.Number).Except(changed.Steps.Select(static step => step.Number))];
                return changed;
            }),
            json =>
            {
                json.WriteString("draft", id);
                Answer.Numbers(json, "undone", undone);
            });
    }

    /// <summary>An occurrence where it is in the draft now: its place's start in the draft and its own position in the place's result.</summary>
    private static void Occurrence(Utf8JsonWriter json, TechniqueOccurrence occurrence, SourceMusic score, long start, PlaceResult result)
    {
        Tick onset = result.InResult(occurrence.Span.Onset).Offset(start);
        Tick end = occurrence.Insert is not null && occurrence.State != TechniqueState.Taken ? onset : onset.Offset(occurrence.Span.Duration);
        json.WriteStartObject();
        json.WriteNumber("bar", occurrence.Bar);
        json.WriteNumber("firstBar", score.BarAt(onset).Number);
        json.WriteNumber("lastBar", score.BarAt(new Tick(Math.Max(onset.Value, end.Value - 1))).Number);
        json.WriteNumber("onset", onset.Value);
        json.WriteNumber("end", end.Value);
        json.WriteNumber("confidence", occurrence.Confidence);
        json.WriteString("state", Answer.Camel(occurrence.State));
        json.WriteEndObject();
    }

    /// <summary>A step made by <paramref name="step"/> on the draft's place, answered as the sources with "draft", "place" and "step".</summary>
    internal static string Stepped(OpenPiece owner, Request request, Func<Draft, string, DateTimeOffset, Draft> step)
    {
        (Draft draft, string placeId) = PlaceOf(owner, request);
        int number = draft.Next;
        return owner.Changed(request, piece => piece.ChangeDraft(draft.Id, each => step(each, placeId, SourceRequest.Now(request))), json =>
        {
            Ids(json, draft.Id, placeId);
            json.WriteNumber("step", number);
        });
    }

    /// <summary>The draft "draft" of the piece and its place "place".</summary>
    private static (Draft Draft, string Place) PlaceOf(OpenPiece owner, Request request)
    {
        string id = request.Required("draft");
        Draft draft = owner.Project.FindDraft(id) ?? throw FacadeException.BadRequest($"There is no draft {id}.");
        string place = request.Required("place");
        return draft.FindPlace(place) is null ? throw FacadeException.BadRequest($"The draft has no place {place}.") : (draft, place);
    }

    /// <summary>A span of the draft's ticks as the ticks of the place's result, cut to the place; null for the whole place.</summary>
    private static TickSpan? InPlace(Draft draft, string placeId, TickSpan? span)
    {
        if (span is not { } inDraft)
        {
            return null;
        }

        TickSpan place = DraftMusic.Of(draft).Places.First(each => each.Place == placeId).Span;
        long from = Math.Max(inDraft.Onset.Value, place.Onset.Value) - place.Onset.Value;
        long to = Math.Min(inDraft.End.Value, place.End.Value) - place.Onset.Value;
        return to > from ? TickSpan.FromBounds(new Tick(from), new Tick(to)) : throw FacadeException.BadRequest("The span lies outside the place.");
    }

    private static void Ids(Utf8JsonWriter json, string draft, string place)
    {
        json.WriteString("draft", draft);
        json.WriteString("place", place);
    }
}