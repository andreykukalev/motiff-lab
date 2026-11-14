using System.Text.RegularExpressions;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Import;

/// <summary>
/// The title, composer and catalogue numbers a file's name suggests (version 0.5, «Импорт папки»), read from the forms
/// people name their sheet music by: "Composer - Title", "Title - Composer", "Title (Composer)", "Composer_Op28_No4",
/// "Composer Title", with Russian and Latin names, track numbers, IMSLP prefixes and "(1)" or "copy" endings left out.
/// Catalogue numbers are written one way: "BWV 846", "Op. 28", "No. 4", "K. 331", "D. 899", "Hob. XVI:52", "соч. 10",
/// "№ 3". Everything here is a guess for the player to confirm, never a fact; no AI.
/// </summary>
/// <param name="Title">The piece's title as the name gives it, catalogue numbers included; null when the name gives none (a scan's "IMG_0042").</param>
/// <param name="Composer">As written in the name; null when no part of it reads as a composer.</param>
/// <param name="Catalogue">The catalogue numbers in the name, in their order, as one text; null when there is none.</param>
public sealed partial record FileNameDetails(string? Title, string? Composer, string? Catalogue)
{
    public static FileNameDetails None { get; } = new(null, null, null);

    /// <summary>What a file's name suggests; its extension is left out.</summary>
    public static FileNameDetails Of(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return OfText(PieceDetails.TitleOf(NameText.Composed(fileName)));
    }

    /// <summary>What a title written somewhere else suggests: no extension is taken off.</summary>
    public static FileNameDetails OfText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string name = Cleaned(NameText.Composed(text));
        if (name.Length == 0)
        {
            return None;
        }

        (string? composer, string rest) = ComposerNames.Split(name);
        string? title = PieceMetadata.Clean(rest.Trim(' ', ',', ';', ':', '-', '–', '—'));
        if (title is not null && (IsJunk(title) || !title.Any(char.IsLetterOrDigit)))
        {
            title = null;
        }

        string? catalogue = string.Join(' ', Canonical().Matches(name).Select(static match => match.Value));
        return new(title, composer, catalogue.Length > 0 ? catalogue : null);
    }

    /// <summary>The composer a folder's name says: only one this class knows ("Бах", "Chopin", "Bach - Partitas"), as written.</summary>
    public static string? ComposerOfFolder(string folderName)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        string name = Cleaned(NameText.Composed(folderName));
        if (ComposerNames.IsKnown(name) && ComposerNames.IsNameLike(name))
        {
            return name;
        }

        return OfText(folderName).Composer is { } composer && ComposerNames.IsKnown(composer) ? composer : null;
    }

    /// <summary>
    /// Whether a name says it holds several pieces, a guess (version 0.5.1, «несколько пьес?»): a collection's word
    /// ("Сборник", "Album", "Sammlung", "Selected", "Complete"), a volume ("Vol. 2", "Том 1", "Heft 3"), a range of
    /// numbers ("Op. 10-12", "Nos. 1–6"), or a plural genre ("Etudes", "Сонаты", "Klavierstücke") with no single number
    /// after it ("Nocturnes Op. 9 No. 2" is one piece).
    /// </summary>
    public static bool NamesCollection(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string text = NameText.Composed(name);
        string folded = NameText.Folded(text);
        return CollectionWord().IsMatch(folded) || NumberRange().IsMatch(text) || (PluralGenre().IsMatch(folded) && !SingleNumber().IsMatch(text));
    }

    /// <summary>
    /// The name without what is not the piece's: underscores as spaces, an IMSLP prefix, a track number at the start, a
    /// copy's "(1)" or "copy" at the end, and the catalogue numbers written one way.
    /// </summary>
    private static string Cleaned(string text)
    {
        string name = Prefix().Replace(text, string.Empty);
        name = Track().Replace(name, string.Empty);
        name = name.Replace('_', ' ');
        for (string before = string.Empty; before != name;)
        {
            before = name;
            name = CopyEnding().Replace(name, string.Empty).TrimEnd();
        }

        name = Hob().Replace(name, match => $"Hob. {match.Groups["book"].Value.ToUpperInvariant()}:{match.Groups["number"].Value}");
        name = Numbered().Replace(name, match => $"{Mark(match.Groups["mark"].Value)} {match.Groups["number"].Value}");
        name = Thematic().Replace(name, match => $"{(match.Groups["mark"].Value == "D" ? "D." : "K.")} {match.Groups["number"].Value}");
        return PieceMetadata.Clean(name) ?? string.Empty;
    }

    private static string Mark(string mark) => NameText.Folded(mark) switch
    {
        "bwv" => "BWV",
        "op" or "opus" => "Op.",
        "соч" => "соч.",
        _ => mark == "№" ? "№" : "No.",
    };

    /// <summary>A title that names nothing: a scan's or a camera's file, an empty document, a number, a code.</summary>
    private static bool IsJunk(string title) => JunkTitle().IsMatch(NameText.Folded(title)) || HexCode().IsMatch(title);

    [GeneratedRegex(@"^(?:(?:IMSLP|PMLP|SIBLEY)\d+[\w.]*-+)+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Prefix();

    [GeneratedRegex(@"^\d{1,3}(?:\s*[._)]\s*|\s*-\s*|\s+-\s+)(?=\D)", RegexOptions.CultureInvariant)]
    private static partial Regex Track();

    [GeneratedRegex(@"(?:\s*\(\d{1,2}\)|\s*\[\d{1,2}\]|\s*[-(]?\s*\b(?:copy|копия|kopie|final|draft)(?:\s*\d{1,2})?\)?)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CopyEnding();

    [GeneratedRegex(@"(?<![\p{L}\p{Nd}])Hob\.?\s*(?<book>[IVXL]{1,6}[a-z]?)\s*[:/.-]?\s*(?<number>\d{1,3})(?![\p{L}\p{Nd}])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Hob();

    [GeneratedRegex(@"(?<![\p{L}\p{Nd}])(?<mark>BWV|Opus|Op|соч|No|Nr|Nº|N°|№)\.?\s*(?<number>\d{1,4}[a-z]?)(?![\p{L}\p{Nd}])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Numbered();

    [GeneratedRegex(@"(?<![\p{L}\p{Nd}])(?<mark>KV|K|D)\.?\s*(?<number>\d{1,3}[a-z]?)(?![\p{L}\p{Nd}])", RegexOptions.CultureInvariant)]
    private static partial Regex Thematic();

    [GeneratedRegex(@"(?:BWV \d+[a-z]?|Hob\. [IVXL]+[a-z]?:\d+|Op\. \d+[a-z]?|соч\. \d+[a-z]?|No\. \d+[a-z]?|№ \d+[a-z]?|K\. \d+[a-z]?|D\. \d+[a-z]?)", RegexOptions.CultureInvariant)]
    private static partial Regex Canonical();



    [GeneratedRegex(@"\b(?:сборник\w*|альбом\w*|album|albums|collection|collected|sammlung|anthology|антологи\w*|хрестомати\w*|selected|ausgewahlte\w*|избранн\w*|complete|samtliche\w*|собрани\w*|(?:vol|volume|volumes|том|вып|выпуск|band|heft|book|livre|тетрадь)\s+(?:\d+|[ivx]{1,4}))\b", RegexOptions.CultureInvariant)]
    private static partial Regex CollectionWord();

    [GeneratedRegex(@"\b(?:sonatas|sonatinas|etudes|studies|pieces|preludes|nocturnes|waltzes|mazurkas|polonaises|inventions|partitas|suites|fugues|songs|dances|impromptus|ballades|bagatelles|intermezzi|works|exercises|сонаты|сонатины|этюды|пьесы|прелюдии|ноктюрны|вальсы|мазурки|полонезы|инвенции|партиты|сюиты|фуги|песни|танцы|экспромты|баллады|багатели|романсы|упражнения|произведения|сочинения|sonaten|sonatinen|etuden|\w*stucke|praludien|mazurken|polonaisen|inventionen|partiten|suiten|fugen|lieder|tanze|balladen|bagatellen|werke|ubungen)\b", RegexOptions.CultureInvariant)]
    private static partial Regex PluralGenre();

    [GeneratedRegex(@"(?<![\p{L}\p{Nd}])(?:Opus|Op|соч|Nos|No|Nr|№|BWV)\.?\s*\d+[a-z]?\s*[-–—]\s*\d+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NumberRange();

    [GeneratedRegex(@"(?<![\p{L}\p{Nd}])(?:No|Nr|Nº|N°|№)\.?\s*\d+[a-z]?(?!\s*[-–—]\s*\d)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SingleNumber();

    [GeneratedRegex(@"^(?:untitled|document|doc|scan|scanned|img|image|dsc|dscn|photo|page|pages|title|pdf|score|sheet music|new document|безымянный|без названия|документ|новый документ|скан|сканирование|фото|изображение|ноты|страница|\d+)?(?: ?\d+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex JunkTitle();

    [GeneratedRegex(@"^[0-9A-Fa-f-]{12,}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexCode();
}