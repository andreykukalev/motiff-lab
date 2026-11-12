using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The pieces of a long score or recording (version 0.5, contract 12, calls added; the experiment Several pieces in a
/// score or recording, which the app decides on): pieces.find proposes where they begin (<see cref="SectionFinder.FindPieces"/>)
/// over the notes as they are, without reading the file again, and pieces.save keeps the pieces the player confirmed as
/// the pieces layer (<see cref="PieceLayer"/>).
/// </summary>
internal static class PiecesRequest
{
    /// <summary>
    /// {"count" (optional, from 1: how many pieces the player says there are)} → {"proposals": [{"number", "title", "firstBar",
    /// "lastBar", "span", "pages": {"first", "last"} or null, "ms": {"from", "to"} or null (a recording's stretch), "confidence", "certainty", "reason", "added", "onlySilence"}],
    /// "layer": [...]}; the proposals leave out none, the layer is what the project keeps.
    /// </summary>
    public static void Find(Utf8JsonWriter json, Project project, ImportedPiece piece, IReadOnlyList<Note> notes, Words words, Request request)
    {
        int? count = request.Int("count");
        if (count is < 1)
        {
            throw FacadeException.BadRequest("\"count\" is how many pieces there are, from 1.");
        }

        FoundSections found = SectionFinder.FindPieces(SectionEvidence.Of(piece, notes, PlaybackScore.FromPiece(piece).Tempos), count);
        PageMap? pages = PieceParts.PagesOf(project, piece);
        var places = new Places(piece, pages, HeardClock.Of(project, piece));
        json.WriteStartArray("proposals");
        foreach (FoundSection section in found.Items)
        {
            json.WriteStartObject();
            json.WriteNumber("number", section.Number);
            json.WriteString("title", section.PieceTitleIn(words));
            json.WriteNumber("firstBar", section.FirstBar);
            json.WriteNumber("lastBar", section.LastBar);
            Answer.Span(json, "span", section.Span);
            places.Write(json, section.Span);
            json.WriteNumber("confidence", section.Confidence);
            json.WriteString("certainty", section.ConfidenceIn(words));
            json.WriteString("reason", section.ReasonIn(words));
            json.WriteBoolean("added", section.IsAdded);
            json.WriteBoolean("onlySilence", section.IsOnlySilence);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        Layer(json, project, places);
    }

    /// <summary>{"pieces": [{"title" (optional), "firstBar"}], "language", "now"} → the project with that pieces layer; none clears it.</summary>
    public static Project Save(Project project, ImportedPiece piece, Words words, DateTimeOffset now, Request request)
    {
        PieceStart[] pieces =
        [
            .. request.Elements("pieces").Select(element => Request.Of(element)).Select(start => new PieceStart(
                start.Text("title"),
                start.Int("firstBar") ?? throw FacadeException.BadRequest("Each piece needs \"firstBar\"."))),
        ];
        if (!request.Has("pieces"))
        {
            throw FacadeException.BadRequest("Send \"pieces\": [{\"title\", \"firstBar\"}], in order.");
        }

        return PieceLayer.Save(project, pieces, piece.Meter, piece.End, words, now);
    }

    /// <summary>"layer": [{"id", "title", "firstBar", "lastBar", "span", "pages", "ms"}]: the pieces the project keeps, in score order.</summary>
    public static void Layer(Utf8JsonWriter json, Project project, ImportedPiece opened) =>
        Layer(json, project, new Places(opened, PieceParts.PagesOf(project, opened), HeardClock.Of(project, opened)));

    private static void Layer(Utf8JsonWriter json, Project project, Places places)
    {
        json.WriteStartArray("layer");
        foreach (LayerPiece piece in PieceLayer.Read(project, places.Piece.Meter))
        {
            json.WriteStartObject();
            json.WriteString("id", piece.BookmarkId);
            json.WriteString("title", piece.Title);
            json.WriteNumber("firstBar", piece.FirstBar);
            json.WriteNumber("lastBar", piece.LastBar);
            Answer.Span(json, "span", piece.Span);
            places.Write(json, piece.Span);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>Where a piece is besides its bars: its pages, when the notes have pages, and its stretch of a recording heard.</summary>
    private sealed record Places(ImportedPiece Piece, PageMap? Pages, HeardClock? Clock)
    {
        /// <summary>"pages": {"first", "last"} or null; "ms": {"from", "to"} (<see cref="HeardCut.StretchOf"/>) or null.</summary>
        public void Write(Utf8JsonWriter json, TickSpan span)
        {
            if (Pages is null)
            {
                json.WriteNull("pages");
            }
            else
            {
                json.WriteStartObject("pages");
                json.WriteNumber("first", Pages.PageOf(span.Onset));
                json.WriteNumber("last", Pages.PageOf(new Tick(span.End.Value - 1)));
                json.WriteEndObject();
            }

            if (Clock is null)
            {
                json.WriteNull("ms");
                return;
            }

            (long from, long to) = HeardCut.StretchOf(Piece, Clock, span);
            json.WriteStartObject("ms");
            json.WriteNumber("from", from);
            json.WriteNumber("to", to);
            json.WriteEndObject();
        }
    }
}