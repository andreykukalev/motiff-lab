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

    /// <summary>
    /// {"pieces": [{"title" (optional), "firstBar"} or {"title", "firstPage"}], "language", "now"} → the project with that
    /// pieces layer; none clears it. A piece by its first page (the iPad's rows of a PDF with notes read, version 0.5)
    /// starts at the bar its page starts in, by the piece's page map.
    /// </summary>
    public static Project Save(Project project, ImportedPiece piece, Words words, DateTimeOffset now, Request request)
    {
        if (!request.Has("pieces"))
        {
            throw FacadeException.BadRequest("Send \"pieces\": [{\"title\", \"firstBar\" or \"firstPage\"}], in order.");
        }

        PageMap? pages = PieceParts.PagesOf(project, piece);
        PieceStart[] pieces = [.. request.Elements("pieces").Select(element => Request.Of(element)).Select(start => new PieceStart(start.Text("title"), FirstBar(start, piece, pages)))];
        return PieceLayer.Save(project, pieces, piece.Meter, piece.End, words, now);
    }

    /// <summary>A piece's first bar: as sent, or the bar its first page starts in.</summary>
    private static int FirstBar(Request start, ImportedPiece piece, PageMap? pages)
    {
        if (start.Int("firstBar") is { } bar)
        {
            return bar;
        }

        if (start.Int("firstPage") is not { } page)
        {
            throw FacadeException.BadRequest("Each piece needs \"firstBar\" or \"firstPage\".");
        }

        if (pages is null || page < 1 || page > pages.Pages)
        {
            throw FacadeException.BadRequest(pages is null ? "These notes have no pages." : $"A page is from 1 to {pages.Pages}; not {page}.");
        }

        return piece.Meter.BarAt(pages.StartOf(page)).Number;
    }

    /// <summary>
    /// "pieces" in the answer to check, when the project has a pieces layer and flags: [{"title", "firstBar", "lastBar",
    /// "open", "total"}], the bars of each piece to check, open and in all (<see cref="CheckPieces.Of"/>).
    /// </summary>
    public static void Check(Utf8JsonWriter json, Project project, MeterMap meter)
    {
        IReadOnlyList<CheckPiece> pieces = CheckPieces.Of(project, meter);
        if (pieces.Count == 0)
        {
            return;
        }

        json.WriteStartArray("pieces");
        foreach (CheckPiece piece in pieces)
        {
            json.WriteStartObject();
            json.WriteString("title", piece.Title);
            json.WriteNumber("firstBar", piece.FirstBar);
            json.WriteNumber("lastBar", piece.LastBar);
            json.WriteNumber("open", piece.Open);
            json.WriteNumber("total", piece.Total);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>
    /// check.resolve: {"bar"} for one bar, or {"bars": {"first", "last"}} for a whole piece (version 0.5): every flag
    /// there resolved, or open again with "resolved": false, in one change (<see cref="CheckPieces.Resolve(Project, int, int, bool)"/>).
    /// </summary>
    public static Project Resolve(Project project, Request request)
    {
        bool resolved = request.Bool("resolved") ?? true;
        if (request.Has("bars"))
        {
            Request bars = request.Object("bars");
            int first = bars.Int("first") ?? throw FacadeException.BadRequest("Send \"bars\": {\"first\", \"last\"}.");
            return CheckPieces.Resolve(project, first, bars.Int("last") ?? first, resolved);
        }

        return project.ResolveCheck(request.Int("bar") ?? throw FacadeException.BadRequest("Send \"bar\", or \"bars\": {\"first\", \"last\"}."), resolved);
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