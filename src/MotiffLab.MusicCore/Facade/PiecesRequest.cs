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
    /// "lastBar", "span", "pages": {"first", "last"} or null, "confidence", "certainty", "reason", "added", "onlySilence"}],
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
        json.WriteStartArray("proposals");
        foreach (FoundSection section in found.Items)
        {
            json.WriteStartObject();
            json.WriteNumber("number", section.Number);
            json.WriteString("title", section.PieceTitleIn(words));
            json.WriteNumber("firstBar", section.FirstBar);
            json.WriteNumber("lastBar", section.LastBar);
            Answer.Span(json, "span", section.Span);
            Pages(json, pages, section.Span);
            json.WriteNumber("confidence", section.Confidence);
            json.WriteString("certainty", section.ConfidenceIn(words));
            json.WriteString("reason", section.ReasonIn(words));
            json.WriteBoolean("added", section.IsAdded);
            json.WriteBoolean("onlySilence", section.IsOnlySilence);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        Layer(json, project, piece.Meter, pages);
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

    /// <summary>"layer": [{"id", "title", "firstBar", "lastBar", "span", "pages"}]: the pieces the project keeps, in score order.</summary>
    public static void Layer(Utf8JsonWriter json, Project project, MeterMap meter, PageMap? pages)
    {
        json.WriteStartArray("layer");
        foreach (LayerPiece piece in PieceLayer.Read(project, meter))
        {
            json.WriteStartObject();
            json.WriteString("id", piece.BookmarkId);
            json.WriteString("title", piece.Title);
            json.WriteNumber("firstBar", piece.FirstBar);
            json.WriteNumber("lastBar", piece.LastBar);
            Answer.Span(json, "span", piece.Span);
            Pages(json, pages, piece.Span);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void Pages(Utf8JsonWriter json, PageMap? pages, TickSpan span)
    {
        if (pages is null)
        {
            json.WriteNull("pages");
            return;
        }

        json.WriteStartObject("pages");
        json.WriteNumber("first", pages.PageOf(span.Onset));
        json.WriteNumber("last", pages.PageOf(new Tick(span.End.Value - 1)));
        json.WriteEndObject();
    }
}