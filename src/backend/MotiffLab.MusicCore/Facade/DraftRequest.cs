using System.Text;
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
            JsonWrite.Texts(json, "parts", parts.Select(Name));
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
        (Draft? draft, DraftDrop drop, string? place, DraftVariant variant) = Fragment(owner.Project, from, request);
        if (draft is null)
        {
            string newId = owner.Project.NextDraftId();
            string name = request.Text("name") ?? DraftLabels.NewName(owner.Project, owner.Words);
            return owner.Changed(request, piece => piece.WithNewDraft(Draft.Start(newId, name, variant.AddedAt, place!, variant)), json => Added(json, newId, place, variant.Id));
        }

        return Forgetting(owner, draft.Id, owner.Changed(request, piece => piece.ChangeDraft(draft.Id, each => each.With(variant, drop, place)), json => Added(json, draft.Id, place, variant.Id)));
    }

    /// <summary>
    /// «Добавить» into a draft of another piece (version 1.2, collage, the plan's answer 5): the fragment, as <see cref="Add"/>
    /// takes it, into the draft "draft" of <paramref name="into"/>, the project of the file the app sent, which is not open
    /// here. The piece the fragment comes from does not change, nor its undo; the variant is a copy that names that piece
    /// (its "project", and every source of its provenance), so it stays whole when that piece is removed. A new draft is
    /// not made there. Answers "draft", "place" (null on the shelf), "variant", "project", the summary of the project
    /// changed, and "file", its project file to save in place of the one sent, written at version 17 (<see cref="ProjectFile.Version"/>).
    /// </summary>
    public static string AddTo(Project into, FragmentOrigin from, Request request)
    {
        _ = request.Required("draft");
        (Draft? draft, DraftDrop drop, string? place, DraftVariant variant) = Fragment(into, from, request);
        Project changed = Refused(() => into.ChangeDraft(draft!.Id, each => each.With(variant, drop, place)));
        return Answer.Write(json =>
        {
            Added(json, draft!.Id, place, variant.Id);
            Answer.Project(json, "project", changed);
            json.WriteString("file", Encoding.UTF8.GetString(ProjectFile.Write(changed)));
        });
    }

    /// <summary>
    /// The drafts of another piece's project file (version 1.2, collage): "project", its summary, as project.summary answers
    /// it, and "drafts" as the piece's sources list them (<see cref="Answer.Drafts"/>), for the «В черновик» sheet's group of
    /// that piece. Nothing is written: a file of version 16 stays as it is until a fragment is added.
    /// </summary>
    public static string Of(Project project, Words words) => Answer.Write(json =>
    {
        Answer.Project(json, "project", project);
        Answer.Drafts(json, project, words);
    });

    /// <summary>
    /// The fragment selected in <paramref name="from"/> as «Добавить» takes it into the drafts of <paramref name="into"/>:
    /// the draft "draft" (null for a new one), where "drop" puts it, the place it goes to (null on the shelf) and the variant.
    /// </summary>
    private static (Draft? Draft, DraftDrop Drop, string? Place, DraftVariant Variant) Fragment(Project into, FragmentOrigin from, Request request)
    {
        (SourceMusic music, TickSpan span) = Selected(from.Open, request);
        DraftPart part = PartOf(request, DraftFragment.Parts(music.NotesIn(span.Onset, span.End)));
        DateTimeOffset now = SourceRequest.Now(request);
        Draft? draft = request.Text("draft") is { } id ? into.FindDraft(id) ?? throw FacadeException.BadRequest($"There is no draft {id}.") : null;
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
        string? project = from.Project == into.Id ? null : from.Project;
        DraftVariant variant = Taken(() => DraftFragment.Variant(variantId, now, music, span, part, from.Notes, from.Provenance, project, DraftFragment.Heard(from.Open.Project, HeardClock.Of(from.Open.Project, from.Open.Piece), span)));
        return (draft, drop, place, variant);
    }

    /// <summary>«Переименовать»: the draft "draft" named "name". Answers the sources with "draft".</summary>
    public static string Rename(OpenPiece owner, Request request)
    {
        string id = request.Required("draft");
        return Forgetting(owner, id, owner.Changed(request, piece => piece.ChangeDraft(id, draft => draft.Renamed(request.Required("name"))), json => json.WriteString("draft", id)));
    }

    /// <summary>«Удалить»: the draft "draft" goes, and only it (0034, section 1). Answers the sources with "removed", its id.</summary>
    public static string Remove(OpenPiece owner, Request request)
    {
        string id = request.Required("draft");
        return Forgetting(owner, id, owner.Changed(request, piece => piece.WithoutDraft(id), json => json.WriteString("removed", id)));
    }

    /// <summary>
    /// <paramref name="answer"/>, once a change of the draft <paramref name="id"/> that is neither a step nor a change of its
    /// structure is made: the drafts kept for drafts.undo from before its structure changed are forgotten, so an undo never
    /// takes this change back with them.
    /// </summary>
    private static string Forgetting(OpenPiece owner, string id, string answer)
    {
        owner.ForgetDraftsBefore(id);
        return answer;
    }

    /// <summary>
    /// «Поставить…» on the shelf, or a variant moved on the draft's map: the variant "variant" of the draft "draft", "drop"
    /// as a "variant" of the place "place", as a new "place" at the end or on the "shelf" (<see cref="Draft.Placed"/>).
    /// Answers the sources with "draft", "place" (where it went, null on the shelf), "variant" and "dropped", the numbers of
    /// the steps that went with it out of its place. Its structure is the draft's state, not a step: drafts.undo takes it back
    /// while the piece stays open (<see cref="OpenPiece.DraftBefore"/>).
    /// </summary>
    public static string Place(OpenPiece owner, Request request)
    {
        (Draft draft, string variant) = VariantOf(owner, request);
        DraftDrop drop = request.Enum<DraftDrop>("drop") ?? throw FacadeException.BadRequest("Send \"drop\": variant, place or shelf.");
        string? place = drop switch
        {
            DraftDrop.Variant => request.Required("place"),
            DraftDrop.Place => draft.NextPlaceId(),
            _ => null,
        };
        return Restructured(owner, request, draft, place, variant, each => each.Placed(variant, drop, place));
    }

    /// <summary>
    /// «Сделать основой» on the draft's map: the variant "variant" the base of its place "place" (<see cref="Draft.WithBase"/>),
    /// which gives the place its bars, meter, key and tempo; the place's steps go, and every anchor of its variants, as they
    /// were made against the base before. Answers the sources with "draft", "place", "variant" and "dropped", the numbers of
    /// those steps. Undone as <see cref="Place"/> is.
    /// </summary>
    public static string Base(OpenPiece owner, Request request)
    {
        (Draft draft, string variant) = VariantOf(owner, request);
        string place = request.Required("place");
        return Restructured(owner, request, draft, place, variant, each => each.WithBase(place, variant));
    }

    /// <summary>
    /// «Карта черновика: порядок мест» (version 1.4, F1): the place "place" of the draft "draft" moved to "index" among its
    /// places, from 0, the others in their order (<see cref="Draft.WithPlaceAt"/>); a place moved where it is, or to an index
    /// outside the places, is a bad request. A bridge place whose place before or after it is another now goes, made for a
    /// seam that is no longer there (the lead's decision, version 1.4 c-3b), and so does each step that kept a seam as it
    /// was («Оставить») where the place before it is another now. Answers the sources with "draft", "place" and "dropped",
    /// the numbers of the steps that went: those «Оставить» and the steps of a bridge place that went. Undone as
    /// <see cref="Place"/> is.
    /// </summary>
    public static string Move(OpenPiece owner, Request request)
    {
        string id = request.Required("draft");
        Draft draft = owner.Project.FindDraft(id) ?? throw FacadeException.BadRequest($"There is no draft {id}.");
        string place = request.Required("place");
        int index = request.RequiredInt("index");
        return Restructured(owner, request, draft, place, null, each => each.WithPlaceAt(place, index));
    }

    /// <summary>
    /// "draft" of a draft opened to be shown: its "id", "name", "places" [{"id", "label" («① Ноты 9–16 + Запись · 2
    /// варианта»), "onset", "end" (in the draft's ticks), "firstBar", "lastBar" (the draft's own numbers), and since the
    /// iPad's step 7 "steps" (the journal's steps of the place, which a new base removes) and "variants" (until then their
    /// count) [{"id", "label" («Запись 9–12 · правая рука»), "base", "steps" (those that take from it or leave it, which go with it
    /// out of the place)}]}] in order, and "shelf" [{"id", "label"}], the draft's map. Since version 1.2: each place "bridge"
    /// (made for a seam, its variants the candidates), a candidate "bridge" (dominant, commonChord, bassWalk) and
    /// "explanation" (the chords it sounds, in words), and a variant that is not its place's base "anchors" [{"base", "variant"
    /// (the ticks the player confirmed sound together, the base's and the variant's), "at" and "bar" (where the base's tick
    /// is in the draft and its bar there)}], for the amber marks over the bars and «Открепить».
    /// </summary>
    public static void View(Utf8JsonWriter json, Draft draft, Words words)
    {
        DraftMusic score = DraftMusic.Of(draft);
        IReadOnlyList<DraftPlaceSpan> spans = score.Places;
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
            json.WriteNumber("steps", draft.StepsOf(place.Id).Count);
            json.WriteBoolean("bridge", place.Bridge);
            json.WriteStartArray("variants");
            PlaceResult? result = null;
            foreach (DraftVariant variant in place.Variants)
            {
                MapVariant(json, variant, words, place.Base == variant.Id, draft.StepsNaming(variant.Id).Count);
                if (place.Base != variant.Id)
                {
                    Anchors(json, variant, result ??= draft.ResultOf(place.Id), span.Span.Onset, score.Music);
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("shelf");
        foreach (DraftVariant variant in draft.Shelf)
        {
            MapVariant(json, variant, words, null, null);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>A variant on the map, its object left open for the place's own fields.</summary>
    private static void MapVariant(Utf8JsonWriter json, DraftVariant variant, Words words, bool? isBase, int? steps)
    {
        json.WriteStartObject();
        json.WriteString("id", variant.Id);
        json.WriteString("label", DraftLabels.Variant(variant, words));
        if (isBase is { } @base)
        {
            json.WriteBoolean("base", @base);
            json.WriteNumber("steps", steps ?? 0);
        }

        if (DraftBridges.KindOf(variant) is { } bridge)
        {
            json.WriteString("bridge", JsonWrite.Name(bridge));
            json.WriteString("explanation", DraftBridges.Explanation(variant, words));
        }
    }

    /// <summary>The variant's confirmed anchors, each where its base tick is in the draft (<paramref name="start"/>, its place's start, and the place's result).</summary>
    private static void Anchors(Utf8JsonWriter json, DraftVariant variant, PlaceResult result, Tick start, SourceMusic score)
    {
        json.WriteStartArray("anchors");
        foreach (DraftAnchor anchor in variant.Anchors)
        {
            Tick at = result.InResult(anchor.Place).Offset(start.Value);
            json.WriteStartObject();
            json.WriteNumber("base", anchor.Place.Value);
            json.WriteNumber("variant", anchor.Variant.Value);
            json.WriteNumber("at", at.Value);
            json.WriteNumber("bar", score.BarAt(at).Number);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>The draft "draft" of the piece and its variant "variant".</summary>
    private static (Draft Draft, string Variant) VariantOf(OpenPiece owner, Request request)
    {
        string id = request.Required("draft");
        Draft draft = owner.Project.FindDraft(id) ?? throw FacadeException.BadRequest($"There is no draft {id}.");
        return (draft, request.Required("variant"));
    }

    /// <summary>
    /// The draft's structure changed by <paramref name="change"/>, the draft as it was kept for drafts.undo; one the draft
    /// cannot make (no such variant or place, a base moved, a variant or a place put where it is) is a bad request. Answers
    /// the sources with the ids (no "variant" when <paramref name="variant"/> is null), "dropped", the numbers of the steps
    /// the change took out of the journal, and what <paramref name="more"/> writes of the draft changed.
    /// </summary>
    internal static string Restructured(OpenPiece owner, Request request, Draft draft, string? place, string? variant, Func<Draft, Draft> change, Action<Utf8JsonWriter, Draft>? more = null)
    {
        Draft changed = draft;
        string answer = owner.Changed(
            request,
            piece => piece.ChangeDraft(draft.Id, each => changed = Refused(() => change(each))),
            json =>
            {
                Added(json, draft.Id, place, variant);
                JsonWrite.Numbers(json, "dropped", [.. draft.Steps.Select(static step => step.Number).Except(changed.Steps.Select(static step => step.Number))]);
                more?.Invoke(json, changed);
            });
        owner.KeepDraftBefore(draft);
        return answer;
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

    /// <summary>What <paramref name="make"/> gives; what the draft cannot make (no such place or variant, one put where it is) is a bad request.</summary>
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
    }

    private static void Added(Utf8JsonWriter json, string draft, string? place, string? variant)
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

        if (variant is not null)
        {
            json.WriteString("variant", variant);
        }
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