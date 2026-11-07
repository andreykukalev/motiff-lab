namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// One piece, many sources, and the result is the player's own version of the notes (testing item 16; Andrei, 2026-11-06):
/// what the piece's notes came from (the base), the PDF kept inside it, the other versions added over it, and what the
/// player's own version holds beyond the base: the bars taken from the versions and the player's corrections.
/// </summary>
/// <param name="Base">Where the base notes came from: a file, notes read from a PDF or a scan, or notes heard in a recording.</param>
/// <param name="HasDocument">Whether a PDF is kept inside the piece, to read as it is.</param>
/// <param name="ReadFromDocument">Whether the base notes were read by a scan with that PDF kept: read from it.</param>
/// <param name="Versions">The other versions, in the order they were added.</param>
/// <param name="TakenBars">Bars taken into the notes from the other versions.</param>
/// <param name="Edits">The player's own corrections of the notes.</param>
public sealed record PieceSources(PieceOrigin Base, bool HasDocument, bool ReadFromDocument, IReadOnlyList<SavedPerformance> Versions, int TakenBars, int Edits)
{
    /// <summary>The sources of a piece.</summary>
    public static PieceSources Of(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new(
            project.Summary.Origin,
            project.Document is not null,
            project.Document is not null && project.HasNotes && project.Scan is not null,
            project.Performances,
            project.TakenBars.Count(),
            project.Edits.Edits.Count);
    }

    /// <summary>The base in words: «ноты из файла», "the notes read from the PDF", «ноты, услышанные в записи».</summary>
    public string BaseName(Words words) => Base switch
    {
        PieceOrigin.Pdf => words.T("the PDF, no notes yet", "PDF, нот пока нет"),
        PieceOrigin.Scan when ReadFromDocument => words.T("the notes read from the PDF", "ноты, прочитанные с PDF"),
        PieceOrigin.Scan => words.T("the notes read from a scan", "ноты, прочитанные со скана"),
        PieceOrigin.Recording => words.T("the notes heard in a recording", "ноты, услышанные в записи"),
        _ => words.T("the notes of the file", "ноты из файла"),
    };

    /// <summary>
    /// The head of the Sources sheet: «Основа — ноты из файла · взято 12 тактов · 9 правок», "Base: the notes read from the
    /// PDF · 2 versions"; the bars taken and the corrections only when there are some.
    /// </summary>
    public string Summary(Words words)
    {
        List<string> parts = [words.T($"Base: {BaseName(words)}", $"Основа — {BaseName(words)}")];
        if (Versions.Count > 0)
        {
            parts.Add(words.Count(Versions.Count, "other version", "other versions", "другая версия", "другие версии", "других версий"));
        }

        if (TakenBars > 0)
        {
            parts.Add(words.T(
                $"{words.Count(TakenBars, "bar", "bars", "такт", "такта", "тактов")} taken",
                $"взято {words.Count(TakenBars, "bar", "bars", "такт", "такта", "тактов")}"));
        }

        if (Edits > 0)
        {
            parts.Add(words.Count(Edits, "correction", "corrections", "правка", "правки", "правок"));
        }

        return string.Join(" · ", parts);
    }
}