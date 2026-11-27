using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// «Найти на записи» in a place's card (version 1.4, F2; spec «Ноты + запись»): the engine finds the stretch of each
/// recording of the piece that is the place's music (<see cref="OnRecording"/>), and puts it in as a variant of the place,
/// the variant a selection of that stretch in the recording's view would make «Вариантом к месту». A recording here is
/// Notes of the piece heard in one (<see cref="NotesProvenance.AudioRecognition"/>).
/// </summary>
internal static class DraftRecordingRequest
{
    /// <summary>
    /// What «Найти на записи» finds for the place "place" of the draft "draft", nothing changed: "draft", "place" and "found"
    /// [{"notes" (the recording's Notes), "label" («Запись 9–16», as the variant would be named), "percent" (its notes found
    /// in the place's base), "span" {"onset", "end"} (in those Notes' ticks), "bars" {"first", "last"} (their bar numbers),
    /// "ms" {"from", "to"} (the stretch of the recording it was played in, null when not known) and "variant" (the place's
    /// variant copied from that very stretch already, null when none)}], the most found first; empty when no recording has
    /// the place's music, and for a bridge place, whose variants are candidates to choose from.
    /// </summary>
    public static string Find(OpenPiece owner, Request request)
    {
        (Draft draft, string placeId) = DraftTakeRequest.PlaceOf(owner, request);
        IReadOnlyList<Found> found = Search(owner.Project, draft, draft.FindPlace(placeId)!, draft.CreatedAt, null);
        return Answer.Write(json =>
        {
            json.WriteString("draft", draft.Id);
            json.WriteString("place", placeId);
            json.WriteStartArray("found");
            foreach (Found each in found)
            {
                DraftSource from = each.Variant.From;
                json.WriteStartObject();
                json.WriteString("notes", from.Notes);
                json.WriteString("label", DraftLabels.Variant(each.Variant, owner.Words));
                json.WriteNumber("percent", each.Match.Percent);
                Answer.Span(json, "span", from.Span);
                json.WriteStartObject("bars");
                json.WriteNumber("first", from.Bars!.Value.First);
                json.WriteNumber("last", from.Bars!.Value.Last);
                json.WriteEndObject();
                if (from.Milliseconds is (long start, long end))
                {
                    json.WriteStartObject("ms");
                    json.WriteNumber("from", start);
                    json.WriteNumber("to", end);
                    json.WriteEndObject();
                }
                else
                {
                    json.WriteNull("ms");
                }

                if (each.Already is { } already)
                {
                    json.WriteString("variant", already);
                }
                else
                {
                    json.WriteNull("variant");
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
        });
    }

    /// <summary>
    /// «Найти на записи»: the stretch found on the recording "notes" (else the one where the most is found, the first of
    /// drafts.findOnRecording's "found") put in as a variant of the place "place" of the draft "draft", "now" its time; only
    /// that recording is searched when "notes" names it. Unavailable when no recording has the place's music, or the place
    /// has that stretch already (another recording's is not taken in its stead). Answers as drafts.place does ("draft", "place", "variant", "dropped" empty),
    /// with "percent"; undone by drafts.undo, as a change of the draft's structure.
    /// </summary>
    public static string Add(OpenPiece owner, Request request)
    {
        (Draft draft, string placeId) = DraftTakeRequest.PlaceOf(owner, request);
        Found chosen = Search(owner.Project, draft, draft.FindPlace(placeId)!, request.Now(), request.Text("notes")).FirstOrDefault()
            ?? throw FacadeException.Unavailable(owner.Words.T("The recording has no stretch of this place's music.", "На записи нет музыки этого места."));
        if (chosen.Already is not null)
        {
            throw FacadeException.Unavailable(owner.Words.T("The place has this stretch of the recording already.", "Этот отрезок записи уже есть в месте."));
        }

        return DraftRequest.Restructured(owner, request, draft, placeId, chosen.Variant.Id, each => each.With(chosen.Variant, DraftDrop.Variant, placeId), (json, _) => json.WriteNumber("percent", chosen.Match.Percent));
    }

    /// <summary>What the search found on one recording: the variant it would put in (its id the draft's next, added at <c>at</c>), how much is found, and the place's variant of that stretch already.</summary>
    private sealed record Found(DraftVariant Variant, PlaceMatch Match, string? Already);

    /// <summary>
    /// The stretch of each recording of <paramref name="project"/> (only the Notes <paramref name="only"/> when it is given)
    /// that is the music of <paramref name="place"/>, the most found first, then in the Sources' order.
    /// </summary>
    private static IReadOnlyList<Found> Search(Project project, Draft draft, DraftPlace place, DateTimeOffset at, string? only)
    {
        if (place.Bridge)
        {
            return [];
        }

        List<Found> found = [];
        foreach (SourceNotes notes in PieceViews.Of(project, HoldingDevice.Ipad).Notes.Where(each => each.Provenance.Op == NotesProvenance.AudioRecognition && (only is null || each.Id == only)))
        {
            if (Opened(project, notes.Id, EditsLayer.Lenient) is not var (heard, piece, music) || OnRecording.Find(place, music) is not var (span, match))
            {
                continue;
            }

            DraftVariant variant = DraftFragment.Variant(draft.NextVariantId(), at, music, span, DraftPart.All, notes.Id, notes.Provenance, null, DraftFragment.Heard(heard, HeardClock.Of(heard, piece), span));
            string? already = place.Variants.FirstOrDefault(each => each.From is { Project: null, Part: DraftPart.All } from && from.Notes == notes.Id && from.Span == span)?.Id;
            found.Add(new Found(variant, match, already));
        }

        return [.. found.OrderByDescending(static each => each.Match.Matched)];
    }

    /// <summary>
    /// The Notes <paramref name="id"/> of the project (or a version) as they open, their corrections laid as
    /// <paramref name="layer"/> says, and their music; null when they are no longer there or no longer open.
    /// </summary>
    internal static (Project Notes, ImportedPiece Piece, SourceMusic Music)? Opened(Project project, string id, EditsLayer layer)
    {
        try
        {
            Project notes = project.ViewOf(id, followWork: false);
            OpenedProject opened = OpenedProject.Open(notes, layer);
            return (notes, opened.Piece, SourceMusic.OfPiece(opened.Piece, opened.Notes));
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or ProjectFileException)
        {
            return null;
        }
    }
}