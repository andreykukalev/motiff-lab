using System.Text.Json;

using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// A place's length in bars (version 1.4, Andrei's answer of 2026-11-26): the player makes a place longer or shorter, and
/// music-core cuts its base again from the Notes it was copied from (<see cref="PlaceLength"/>). The length is the draft's
/// structure, not a step: drafts.undo takes it back while the piece stays open.
/// </summary>
internal static class DraftLengthRequest
{
    /// <summary>
    /// The length of the place "place" of the draft "draft", nothing changed: "draft", "place", "bars" {"first", "last"} (the
    /// bars of its source its base was cut from, null when not known), "most" {"first", "last"} (the bars those Notes have,
    /// null when they are not at hand), "resizable" and "reason" (why not, in words; null when it is). Not resizable: a bridge
    /// place, a base music-core made, a fragment of another piece that is not open, Notes no longer in the piece.
    /// <paramref name="pieces"/> gives another piece open to work on by id, for a place copied from it.
    /// </summary>
    public static string Length(OpenPiece owner, Request request, Func<string, Project?> pieces)
    {
        (Draft draft, string placeId) = DraftTakeRequest.PlaceOf(owner, request);
        DraftPlace place = draft.FindPlace(placeId)!;
        Source? source = SourceOf(owner, place, pieces, out string? reason);
        return Answer.Write(json =>
        {
            json.WriteString("draft", draft.Id);
            json.WriteString("place", placeId);
            Bars(json, "bars", place.BaseVariant.From.Bars);
            Bars(json, "most", source is null ? null : Most(source.Music));
            json.WriteBoolean("resizable", source is not null);
            if (reason is null)
            {
                json.WriteNull("reason");
            }
            else
            {
                json.WriteString("reason", reason);
            }
        });
    }

    /// <summary>
    /// The place "place" of the draft "draft" made bars "bars" {"first", "last"} of its source long, both included; an edge
    /// not moved keeps where it was in its bar. Bars outside the Notes, a last before the first, or the length it has are
    /// a bad request; a place not resizable (<see cref="Length"/>) or a span with no notes of its part is unavailable.
    /// Answers the sources with "draft", "place", "variant" (its base), "dropped" (the steps that went: those outside the
    /// new span, and the seams kept as they are whose boundary moved) and "bars" {"first", "last"}, as the place is now.
    /// </summary>
    public static string Resize(OpenPiece owner, Request request, Func<string, Project?> pieces)
    {
        (Draft draft, string placeId) = DraftTakeRequest.PlaceOf(owner, request);
        DraftPlace place = draft.FindPlace(placeId)!;
        Source source = SourceOf(owner, place, pieces, out string? reason) ?? throw FacadeException.Unavailable(reason!);
        Request bars = request.Object("bars");
        int first = bars.Int("first") ?? throw FacadeException.BadRequest("Send \"bars\" {\"first\", \"last\"}, the bars of the source.");
        int last = bars.Int("last") ?? throw FacadeException.BadRequest("Send \"bars\" {\"first\", \"last\"}, the bars of the source.");
        (int lowest, int highest) = Most(source.Music) ?? throw FacadeException.Unavailable(owner.Words.T("The Notes the place was cut from have no bars.", "В нотах, из которых вырезано место, нет тактов."));
        if (first > last)
        {
            throw FacadeException.BadRequest("A place keeps at least one bar: \"first\" is at most \"last\".");
        }

        if (first < lowest || last > highest)
        {
            throw FacadeException.BadRequest($"The Notes have bars {lowest} to {highest}.");
        }

        DraftVariant was = place.BaseVariant;
        TickSpan span = Span(source.Music, was.From, first, last);
        DraftVariant cut;
        try
        {
            cut = DraftFragment.Variant(was.Id, was.AddedAt, source.Music, span, was.From.Part, was.From.Notes, was.Provenance, was.From.Project, DraftFragment.Heard(source.Notes, HeardClock.Of(source.Notes, source.Piece), span));
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.Unavailable(exception.Message);
        }

        return DraftRequest.Restructured(owner, request, draft, placeId, was.Id, each => PlaceLength.Resized(each, placeId, cut), (json, _) => Bars(json, "bars", cut.From.Bars));
    }

    /// <summary>The Notes a place's base was cut from, opened, and their music.</summary>
    private sealed record Source(Project Notes, ImportedPiece Piece, SourceMusic Music);

    /// <summary>
    /// The Notes the base of <paramref name="place"/> was cut from as they open now: the piece's working notes as open, other
    /// Notes or a version of it, or of the other piece the base names when that is open to work on; null with the reason why not.
    /// </summary>
    private static Source? SourceOf(OpenPiece owner, DraftPlace place, Func<string, Project?> pieces, out string? reason)
    {
        Words words = owner.Words;
        DraftSource from = place.BaseVariant.From;
        reason = PlaceLength.Refusal(place, words);
        if (reason is not null)
        {
            return null;
        }

        if ((from.Project is null ? owner.Project : pieces(from.Project)) is not { } holder)
        {
            reason = words.T("Open the piece the place was taken from to change its length.", "Откройте пьесу, из которой взято место, чтобы изменить его длину.");
            return null;
        }

        if (holder == owner.Project && holder.HasNotes && from.Notes == holder.NotesId)
        {
            return new Source(holder, owner.Piece, SourceMusic.OfPiece(owner.Piece, owner.Notes));
        }

        if (DraftRecordingRequest.Opened(holder, from.Notes, owner.Corrections.Layer) is var (notes, piece, music))
        {
            return new Source(notes, piece, music);
        }

        reason = words.T("The Notes the place was cut from are no longer in the piece.", "Нот, из которых вырезано место, больше нет в пьесе.");
        return null;
    }

    /// <summary>The first and last bar numbers of <paramref name="music"/>; null without bars.</summary>
    private static (int First, int Last)? Most(SourceMusic music) =>
        music.Bars.Count == 0 ? null : (music.NumberOf(0), music.NumberOf(music.Bars.Count - 1));

    /// <summary>The span of bars <paramref name="first"/> to <paramref name="last"/>, an edge at the bar it was in keeping where it was in it.</summary>
    private static TickSpan Span(SourceMusic music, DraftSource from, int first, int last)
    {
        (int First, int Last) now = from.Bars ?? (music.BarAt(from.Span.Onset).Number, music.BarAt(new Tick(Math.Max(from.Span.Onset.Value, from.Span.End.Value - 1))).Number);
        AssemblyBar Bar(int number) => music.Bars[number - music.NumberOf(0)];
        Tick onset = first == now.First ? from.Span.Onset : Bar(first).Span.Onset;
        Tick end = last == now.Last ? from.Span.End : Bar(last).Span.End;
        return TickSpan.FromBounds(onset, end);
    }

    private static void Bars(Utf8JsonWriter json, string name, (int First, int Last)? bars)
    {
        if (bars is not var (first, last))
        {
            json.WriteNull(name);
            return;
        }

        json.WriteStartObject(name);
        json.WriteNumber("first", first);
        json.WriteNumber("last", last);
        json.WriteEndObject();
    }
}