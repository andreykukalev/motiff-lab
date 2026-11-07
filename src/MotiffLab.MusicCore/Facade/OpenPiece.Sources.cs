using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The piece's sources (testing item 16; contract 6): one piece, many sources, and the player's own version of the notes.
/// What the base is and what the player's version holds beyond it, a PDF kept inside the piece, and the notes read from
/// that PDF added as another version, lined up with the base.
/// </summary>
internal sealed partial class OpenPiece
{
    private string CallOnSources(string name, Request request) => name switch
    {
        "sources" => SourcesAnswer(),
        "sources.document" => KeepDocument(request),
        "sources.documentNotes" => AddDocumentNotes(request),
        _ => throw new FacadeException("unknownCall", $"There is no call \"{name}\"."),
    };

    /// <summary>
    /// The base and its name, the summary line for the Sources sheet («Основа — ноты из файла · взято 12 тактов · 9 правок»),
    /// the PDF kept in the piece with its pages, the other versions, the bars taken and the corrections.
    /// </summary>
    private string SourcesAnswer()
    {
        PieceSources sources = PieceSources.Of(Project);
        return Answer.Write(json =>
        {
            json.WriteString("base", sources.Base.ToString().ToLowerInvariant());
            json.WriteString("baseName", sources.BaseName(Words));
            json.WriteString("summary", sources.Summary(Words));
            if (Project.Document is not null)
            {
                json.WriteStartObject("document");
                json.WriteString("fileName", Project.DocumentFileName);
                json.WriteNumber("pages", Project.DocumentPages?.Pages ?? 0);
                json.WriteBoolean("readFrom", sources.ReadFromDocument);
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("document");
            }

            Answer.Performances(json, Project);
            json.WriteNumber("taken", sources.TakenBars);
            json.WriteNumber("edits", sources.Edits);
        });
    }

    /// <summary>
    /// "pdf" (base64) and "pages" (how many it has, else counted from the PDF): the PDF kept inside this piece, its pages linked to the bars shared
    /// evenly until its notes are read (testing item 16). It stays on the device, as every PDF does.
    /// </summary>
    private string KeepDocument(Request request)
    {
        byte[] pdf;
        try
        {
            pdf = Convert.FromBase64String(request.Required("pdf"));
        }
        catch (FormatException)
        {
            throw FacadeException.BadRequest("\"pdf\" must be base64.");
        }

        if (PdfDocument.CheckSize(pdf.Length) is { } rejection)
        {
            throw new FacadeException("import", $"{rejection.Text(Words)} {rejection.TipIn(Words)}".Trim());
        }

        int pages = Math.Max(1, request.Int("pages") ?? PdfDocument.PageCount(pdf));
        try
        {
            Project = Project.WithDocument(pdf, PageMap.Even(Piece.Meter, Piece.End, pages));
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        return SourcesAnswer();
    }

    /// <summary>
    /// "musicXml" (base64) the scan server read from the PDF kept in this piece, "engine", "pages" and "name": the notes
    /// added as another version from the score (0022), and the PDF's pages linked to these bars by the bar numbers the scan
    /// began each page with. Answers the comparison, as performance.add does.
    /// </summary>
    private string AddDocumentNotes(Request request)
    {
        if (Project.Document is null)
        {
            throw FacadeException.Unavailable("Keep the PDF in the piece first (sources.document).");
        }

        byte[] musicXml;
        try
        {
            musicXml = Convert.FromBase64String(request.Required("musicXml"));
        }
        catch (FormatException)
        {
            throw FacadeException.BadRequest("\"musicXml\" must be base64.");
        }

        string fileName = Project.DocumentFileName;
        PieceImportResult imported = PieceImporter.Import(Path.ChangeExtension(fileName, ".musicxml"), musicXml);
        if (imported.Piece is not { } read)
        {
            throw new FacadeException("import", imported.Rejection?.Text(Words) ?? Words.T("No notes could be read from these pages.", "На этих страницах не удалось прочитать нот."));
        }

        VersionFileResult version = VersionFile.Read(Path.ChangeExtension(fileName, ".musicxml"), musicXml);
        if (version.Midi is not { } midi)
        {
            throw FacadeException.BadRequest(version.Rejection is { } why ? $"{why.Message} {why.Tip}".Trim() : "The notes read have no notes.");
        }

        int pages = Math.Max(1, request.Int("pages") ?? Project.DocumentPages?.Pages ?? 1);
        Project = Project with { DocumentPages = PageMap.Of(read, pages).Through(read.Meter, Piece.Meter, Piece.End) };
        string name = request.Text("name") is { Length: > 0 } given ? given : Path.GetFileNameWithoutExtension(fileName);
        return AddVersion(name, midi, PerformanceSource.Score, 0, request.Text("engine"));
    }
}