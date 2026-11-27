using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// scan.insert on piece 0 (within contract 14, a call added; v1.3 i-1b, Andrei 2026-11-24 «Вставить в те же ноты»): a page no
/// engine read, read again by itself once the rest of the piece opened, put into the same Notes (<see cref="ScanPageInsert.Into"/>).
/// </summary>
internal static partial class ScanRequest
{
    /// <summary>
    /// {"file": the piece's project file, "page": the page's place among the notes' pages, from 1 (the pages read before it,
    /// plus one), "reading": the page as one of scan.join's "pages" (its MusicXML, its readings, or {"readings", "evidence"}),
    /// "reports" (the page's report as "reports" takes one; without it notes that kept reports keep none, v1.5 i-7), "pages": the PDF's pages,
    /// "language"} → {"file": the project file with the page in, "was" and "fingerprint": the notes' music before and after,
    /// as `check` answers it, "firstBar" and "bars": the page's bars, the bars from "firstBar" on before now that many on,
    /// "pages": [the page as scan.join answers one, numbered by its place]}. The player's corrections, checked bars and Check
    /// flags are carried over to the same music, the bookmarks and resume points after it move on by its length (v1.5 i-7); refused ("unavailable") when something else the player put on the score
    /// would move, or a correction cannot be carried exactly. Pure: the app writes the file.
    /// </summary>
    public static string Insert(Request fields)
    {
        Words words = Words.For(fields.Text("language"));
        Project project = MusicCoreFacade.ProjectIn(fields);

        int page = fields.Int("page") ?? throw FacadeException.BadRequest("Send \"page\", the page's place among the notes' pages, from 1.");
        Dictionary<int, ScanMergedPage> merged = [];
        PageInserted inserted;
        try
        {
            byte[] musicXml = Read(fields.Element("reading") ?? throw FacadeException.BadRequest("Send \"reading\", the page read again."), page, merged);
            PageReport? report = ReportsIn(fields) is { Count: > 0 } reports ? reports[0] : null;
            inserted = ScanPageInsert.Into(project, page, musicXml, report, fields.Int("pages") ?? project.DocumentPages?.Pages ?? 1);
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        if (inserted.Project is not { } with)
        {
            throw FacadeException.Unavailable(inserted.Refused switch
            {
                PageRefusal.Work => words.T(
                    "The page can be put into notes read from this PDF only while nothing but corrections and bookmarks is on the score: phrases, marks, motifs and ideas would move.",
                    "Страницу можно вставить в ноты из этого PDF, только пока на нотах нет ничего, кроме правок и закладок: фразы, пометки, мотивы и идеи сдвинулись бы."),
                PageRefusal.Across => words.T(
                    "The page cannot be put in: a corrected note sounds across the place where it goes.",
                    "Страницу не вставить: исправленная нота звучит через место, куда она встаёт."),
                _ => words.T(
                    "The page cannot be put in: a correction would no longer apply to the same notes.",
                    "Страницу не вставить: правка перестала бы относиться к тем же нотам."),
            });
        }

        ImportedPiece piece = PieceImporter.Import(with.FileName, with.Source).Piece!;
        ScanCheck check = with.Scan!.CheckOf(piece);
        PageMap map = PageMap.Of(piece, ScanJoin.Split(with.Source).Count);
        return Answer.Write(json =>
        {
            json.WriteString("file", Encoding.UTF8.GetString(ProjectFile.Write(with)));
            json.WriteString("was", PieceOriginal.Fingerprint(project.Source));
            json.WriteString("fingerprint", PieceOriginal.Fingerprint(with.Source));
            json.WriteNumber("firstBar", inserted.FirstBar);
            json.WriteNumber("bars", inserted.Bars);
            json.WriteStartArray("pages");
            Page(json, page, [.. check.Bars.Where(bar => map.PageOf(bar.Span.Onset) == page)], with.Scan.Pages.Count > 0 ? with.Scan.Pages[page - 1] : null, merged.GetValueOrDefault(page));
            json.WriteEndArray();
        });
    }
}