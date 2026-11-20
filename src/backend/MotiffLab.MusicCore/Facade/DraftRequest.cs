using System.Text.Json;

using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// Where a fragment for a draft comes from: the piece open where it was selected (the working notes, Notes or a version
/// opened to be shown, or another piece), the id of those Notes, their provenance, and the id of the project they are in.
/// </summary>
internal sealed record FragmentOrigin(OpenPiece Open, string Notes, NotesProvenance Provenance, string Project);

/// <summary>
/// The drafts' calls (decision record 0034, version 1.1; spec «Экран (iPad)»): the «В черновик» sheet for a selection
/// in any view of notes and its «Добавить», and renaming and removing a draft from the Sources group «Черновики». Each
/// call that changes the piece answers the sources (<see cref="SourceRequest.Layers"/>), the drafts among them.
/// </summary>
internal static class DraftRequest
{
    /// <summary>
    /// The «В черновик» sheet for "span" {onset, end} or "bars" {first, last} of the piece the fragment comes from (else its
    /// selection) and "part" (all, the default): "parts", what can be taken there; "part"; "notes", how many the fragment
    /// holds; "newName", the name a new draft takes; "drafts": [{"id", "name", "suggested" (the place offered first, null
    /// when none), "places": [{"id", "label", "percent", "sameMusic"}]}] of the piece the draft goes into; and "suggested",
    /// {"draft", "place", "percent"} where the most of the fragment is the same music, null when nowhere (<see cref="SameMusic"/>).
    /// </summary>
    public static string Sheet(OpenPiece owner, FragmentOrigin from, Request request)
    {
        (SourceMusic music, TickSpan span) = Selected(from.Open, request);
        IReadOnlyList<DraftPart> parts = DraftFragment.Parts(music.NotesIn(span.Onset, span.End));
        DraftPart part = PartOf(request, parts);
        SourceMusic fragment = Taken(() => DraftFragment.Of(music, span, part));
        Words words = owner.Words;
        List<(Draft Draft, IReadOnlyList<PlaceMatch> Matches, PlaceMatch? Suggested)> offered = [];
        foreach (Draft draft in owner.Project.Drafts)
        {
            IReadOnlyList<PlaceMatch> matches = SameMusic.Of(draft, fragment);
            offered.Add((draft, matches, SameMusic.Suggested(matches)));
        }

        // The place offered first of all the drafts: the earlier draft on a tie, as within a draft.
        PlaceMatch? best = SameMusic.Suggested([.. offered.Select(static each => each.Suggested).OfType<PlaceMatch>()]);
        return Answer.Write(json =>
        {
            json.WriteStartArray("parts");
            foreach (DraftPart each in parts)
            {
                json.WriteStringValue(Name(each));
            }

            json.WriteEndArray();
            json.WriteString("part", Name(part));
            json.WriteNumber("notes", fragment.Notes.Count);
            json.WriteString("newName", DraftLabels.NewName(owner.Project, words));
            json.WriteStartArray("drafts");
            foreach ((Draft draft, IReadOnlyList<PlaceMatch> matches, PlaceMatch? suggested) in offered)
            {
                SheetDraft(json, draft, matches, suggested, words);
            }

            json.WriteEndArray();
            if (best is not null)
            {
                json.WriteStartObject("suggested");
                json.WriteString("draft", offered.First(each => each.Suggested == best).Draft.Id);
                json.WriteString("place", best.Place);
                json.WriteNumber("percent", best.Percent);
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("suggested");
            }
        });
    }

    /// <summary>
    /// «Добавить» on the sheet: the fragment (as <see cref="Sheet"/> reads it) into the draft "draft", "drop" as "variant" of
    /// its place "place", as a new "place" at the end or on the "shelf"; with no "draft", a new draft named "name" (else
    /// «Черновик N») of one place, "now" its time. From another piece the variant names that piece; from notes heard in a
    /// recording it keeps the stretch of the recording they were played in (<see cref="DraftFragment.Heard"/>). Answers the
    /// sources with "draft", "place" (null on the shelf) and "variant", the ids.
    /// </summary>
    public static string Add(OpenPiece owner, FragmentOrigin from, Request request)
    {
        (SourceMusic music, TickSpan span) = Selected(from.Open, request);
        DraftPart part = PartOf(request, DraftFragment.Parts(music.NotesIn(span.Onset, span.End)));
        DateTimeOffset now = SourceRequest.Now(request);
        Draft? draft = request.Text("draft") is { } id ? owner.Project.FindDraft(id) ?? throw FacadeException.BadRequest($"There is no draft {id}.") : null;
        DraftDrop drop = request.Enum<DraftDrop>("drop") ?? (draft is null ? DraftDrop.Place : throw FacadeException.BadRequest("Send \"drop\": variant, place or shelf."));
        if (draft is null && drop != DraftDrop.Place)
        {
            throw FacadeException.BadRequest("A new draft starts with the fragment as its first place.");
        }

        string variantId = draft?.NextVariantId() ?? "v_0001";
        string? place = draft is null ? "pl_0001" : drop switch
        {
            DraftDrop.Variant => request.Required("place"),
            DraftDrop.Place => draft.NextPlaceId(),
            _ => null,
        };
        string? project = from.Project == owner.Project.Id ? null : from.Project;
        DraftVariant variant = Taken(() => DraftFragment.Variant(variantId, now, music, span, part, from.Notes, from.Provenance, project, DraftFragment.Heard(from.Open.Project, HeardClock.Of(from.Open.Project, from.Open.Piece), span)));
        if (draft is null)
        {
            string newId = owner.Project.NextDraftId();
            string name = request.Text("name") ?? DraftLabels.NewName(owner.Project, owner.Words);
            return owner.Changed(request, piece => piece.WithNewDraft(Draft.Start(newId, name, now, place!, variant)), json => Added(json, newId, place, variantId));
        }

        return owner.Changed(request, piece => piece.ChangeDraft(draft.Id, each => each.With(variant, drop, place)), json => Added(json, draft.Id, place, variantId));
    }

    /// <summary>«Переименовать»: the draft "draft" named "name". Answers the sources with "draft".</summary>
    public static string Rename(OpenPiece owner, Request request)
    {
        string id = request.Required("draft");
        return owner.Changed(request, piece => piece.ChangeDraft(id, draft => draft.Renamed(request.Required("name"))), json => json.WriteString("draft", id));
    }

    /// <summary>«Удалить»: the draft "draft" goes, and only it (0034, section 1). Answers the sources with "removed", its id.</summary>
    public static string Remove(OpenPiece owner, Request request)
    {
        string id = request.Required("draft");
        return owner.Changed(request, piece => piece.WithoutDraft(id), json => json.WriteString("removed", id));
    }

    /// <summary>
    /// "draft" of a draft opened to be shown: its "id", "name" and "places" [{"id", "label" («① Ноты 9–16 + Запись · 2
    /// варианта»), "onset", "end" (in the draft's ticks), "firstBar", "lastBar" (the draft's own numbers), "variants"}] in order.
    /// </summary>
    public static void View(Utf8JsonWriter json, Draft draft, Words words)
    {
        IReadOnlyList<DraftPlaceSpan> spans = DraftMusic.Of(draft).Places;
        json.WriteStartObject("draft");
        json.WriteString("id", draft.Id);
        json.WriteString("name", draft.Name);
        json.WriteStartArray("places");
        for (int index = 0; index < draft.Places.Count; index++)
        {
            DraftPlace place = draft.Places[index];
            DraftPlaceSpan span = spans[index];
            json.WriteStartObject();
            json.WriteString("id", place.Id);
            json.WriteString("label", DraftLabels.Place(place, index, words));
            json.WriteNumber("onset", span.Span.Onset.Value);
            json.WriteNumber("end", span.Span.End.Value);
            json.WriteNumber("firstBar", span.FirstBar);
            json.WriteNumber("lastBar", span.LastBar);
            json.WriteNumber("variants", place.Variants.Count);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void SheetDraft(Utf8JsonWriter json, Draft draft, IReadOnlyList<PlaceMatch> matches, PlaceMatch? suggested, Words words)
    {
        json.WriteStartObject();
        json.WriteString("id", draft.Id);
        json.WriteString("name", draft.Name);
        if (suggested is null)
        {
            json.WriteNull("suggested");
        }
        else
        {
            json.WriteString("suggested", suggested.Place);
        }

        json.WriteStartArray("places");
        for (int index = 0; index < matches.Count; index++)
        {
            json.WriteStartObject();
            json.WriteString("id", matches[index].Place);
            json.WriteString("label", DraftLabels.Place(draft.Places[index], index, words));
            json.WriteNumber("percent", matches[index].Percent);
            json.WriteBoolean("sameMusic", matches[index].IsSameMusic);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Added(Utf8JsonWriter json, string draft, string? place, string variant)
    {
        json.WriteString("draft", draft);
        if (place is null)
        {
            json.WriteNull("place");
        }
        else
        {
            json.WriteString("place", place);
        }

        json.WriteString("variant", variant);
    }

    /// <summary>The music of the piece open and the span asked for: "span", "bars" {first, last} as select reads them, else the piece's selection.</summary>
    private static (SourceMusic Music, TickSpan Span) Selected(OpenPiece open, Request request)
    {
        ImportedPiece piece = open.Piece;
        Request bars = request.Object("bars");
        TickSpan span = request.Span("span")
            ?? (bars.Int("first") is { } first ? MotifSelector.Bars(piece.Meter, first, bars.Int("last") ?? first, piece.End) : (TickSpan?)null)
            ?? open.Selection?.Span
            ?? throw FacadeException.NoSelection();
        return (SourceMusic.OfPiece(piece, open.Notes), span);
    }

    /// <summary>"part": all (the default), right, left or melody, among what <paramref name="parts"/> offers.</summary>
    private static DraftPart PartOf(Request request, IReadOnlyList<DraftPart> parts)
    {
        DraftPart part = request.Enum<DraftPart>("part") ?? DraftPart.All;
        return parts.Contains(part) ? part : throw FacadeException.Unavailable($"The selection has no {Name(part)} to take.");
    }

    /// <summary>What <paramref name="take"/> takes of the selection; a part with no notes there is unavailable, on the sheet and on «Добавить» alike.</summary>
    private static T Taken<T>(Func<T> take)
    {
        try
        {
            return take();
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.Unavailable(exception.Message);
        }
    }

    private static string Name(DraftPart part) => part.ToString().ToLowerInvariant();
}