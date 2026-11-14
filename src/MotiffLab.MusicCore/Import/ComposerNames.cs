using System.Text.RegularExpressions;

namespace MotiffLab.MusicCore.Import;

/// <summary>
/// Which part of a file's name is the composer (version 0.5, «Импорт папки»): a part that names a composer people often
/// keep sheet music of, in Latin or Russian letters, or a part shaped like a person's name (one to four capitalised words
/// or initials, no digits, no word of a piece's title such as "Prelude", "Соната", "major"). A guess, never a fact.
/// </summary>
internal static partial class ComposerNames
{
    /// <summary>
    /// The composer's part of a name and the rest: "Composer - Title" or "Title - Composer" (the side that reads as the
    /// composer, the first when both do), "Title (Composer)", a known composer at the start ("Chopin Op. 28 No. 4") or the
    /// end ("Ave Maria Schubert"); no composer and the whole name otherwise.
    /// </summary>
    public static (string? Composer, string Remainder) Split(string name) =>
        BySeparator(name) ?? ByBrackets(name) ?? AtStart(name) ?? AtEnd(name) ?? (null, name);

    /// <summary>Whether the text is a name shaped like a person's and names a composer this class knows.</summary>
    public static bool IsKnown(string text) => IsNameShaped(text) && Names(text);

    /// <summary>Whether the text reads as a person's name: shaped so, each word capitalised or an initial.</summary>
    public static bool IsNameLike(string text)
    {
        string[] words = Words(text);
        return IsNameShaped(text) && words.Length <= 4
            && words.All(static word => Initials().IsMatch(word) || char.IsUpper(word.FirstOrDefault(char.IsLetter)))
            && words.Any(static word => word.Count(char.IsLetter) >= 2);
    }

    private static (string?, string)? BySeparator(string name)
    {
        string[] parts = Separator().Split(name);
        if (parts.Length < 2)
        {
            return null;
        }

        int first = Rank(parts[0]);
        int last = Rank(parts[^1]);
        return first > 0 && first >= last ? (Trimmed(parts[0]), string.Join(" - ", parts[1..]))
            : last > 0 ? (Trimmed(parts[^1]), string.Join(" - ", parts[..^1]))
            : null;
    }

    private static (string?, string)? ByBrackets(string name) =>
        Bracketed().Match(name) is { Success: true } match && Rank(match.Groups["who"].Value) > 0
            ? (Trimmed(match.Groups["who"].Value), match.Groups["title"].Value)
            : null;

    /// <summary>A known composer within the first four words, after initials or given names, with any initials after it.</summary>
    private static (string?, string)? AtStart(string name)
    {
        string[] words = Words(name);
        for (int index = 0; index < Math.Min(4, words.Length - 1); index++)
        {
            if (IsSurname(words[index]))
            {
                int end = index + 1;
                while (end < words.Length - 1 && Initials().IsMatch(words[end]))
                {
                    end++;
                }

                string rest = string.Join(' ', words[end..]);
                return rest.Any(char.IsLetter) ? (Trimmed(string.Join(' ', words[..end])), rest) : null;
            }

            if (!Initials().IsMatch(words[index]) && !IsNameLike(words[index]))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>A known composer as the last word, with the initials before it.</summary>
    private static (string?, string)? AtEnd(string name)
    {
        string[] words = Words(name);
        if (words.Length < 2 || !IsSurname(words[^1]))
        {
            return null;
        }

        int start = words.Length - 1;
        while (start > 1 && Initials().IsMatch(words[start - 1]))
        {
            start--;
        }

        return (Trimmed(string.Join(' ', words[start..])), string.Join(' ', words[..start]));
    }

    /// <summary>2 for a known composer, 1 for a name like a person's, 0 otherwise.</summary>
    private static int Rank(string part) => IsKnown(part) ? 2 : IsNameLike(part) ? 1 : 0;

    /// <summary>A capitalised word that is, or ends with, a known composer's name: "Bach", "Ф.Шопен", "Чайковского" is not.</summary>
    private static bool IsSurname(string word) => word.FirstOrDefault(char.IsLetter) is char letter && char.IsUpper(letter) && IsKnown(word);

    /// <summary>No digits, one to five words, none of them a word of a title or a catalogue mark.</summary>
    private static bool IsNameShaped(string text)
    {
        string[] words = Words(text);
        return words.Length is > 0 and <= 5 && !text.Any(char.IsDigit) && text.Any(char.IsLetter)
            && !NameText.Folded(text).Split(' ').Any(s_titleWords.Contains);
    }

    /// <summary>Whether a capitalised word of the text, or two joined as in "Rimsky-Korsakov", is a known composer's name.</summary>
    private static bool Names(string text)
    {
        string[] words = [.. Letters().Matches(NameText.Composed(text)).Select(static match => match.Value)];
        for (int index = 0; index < words.Length; index++)
        {
            string word = NameText.Folded(words[index]);
            bool known = s_known.Contains(word) || (index + 1 < words.Length && s_known.Contains($"{word} {NameText.Folded(words[index + 1])}"));
            if (known && char.IsUpper(words[index][0]))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Trimmed(string composer) => composer.Trim(' ', ',', ';', ':', '-');

    [GeneratedRegex(@"\s+[-–—]+\s+|\s*[–—]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex Separator();

    [GeneratedRegex(@"^(?<title>.*?\S)\s*[(\[](?<who>[^()\[\]]+)[)\]]$", RegexOptions.CultureInvariant)]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant)]
    private static partial Regex Letters();

    [GeneratedRegex(@"^(?:\p{Lu}\.)+,?$", RegexOptions.CultureInvariant)]
    private static partial Regex Initials();

    /// <summary>Composers people often keep sheet music of, folded (<see cref="NameText.Folded"/>), in Latin and Russian spellings.</summary>
    private static readonly HashSet<string> s_known = new(StringComparer.Ordinal)
    {
        "bach", "бах", "beethoven", "бетховен", "mozart", "моцарт", "chopin", "шопен", "schubert", "шуберт", "schumann", "шуман",
        "liszt", "лист", "brahms", "брамс", "haydn", "гайдн", "handel", "haendel", "гендель", "debussy", "дебюсси", "ravel", "равель",
        "rachmaninoff", "rachmaninov", "rachmaninow", "рахманинов", "tchaikovsky", "tschaikowsky", "tchaikovski", "чаиковскии",
        "prokofiev", "prokofieff", "прокофьев", "shostakovich", "schostakowitsch", "шостакович", "scriabin", "skryabin", "скрябин",
        "mussorgsky", "moussorgsky", "мусоргскии", "rimsky korsakov", "римскии корсаков", "glinka", "глинка", "borodin", "бородин",
        "grieg", "григ", "mendelssohn", "мендельсон", "satie", "сати", "scarlatti", "скарлатти", "vivaldi", "вивальди", "czerny",
        "черни", "clementi", "клементи", "burgmuller", "бургмюллер", "einaudi", "эинауди", "albeniz", "альбенис", "granados",
        "гранадос", "faure", "форе", "saint saens", "сен санс", "franck", "франк", "bartok", "барток", "kabalevsky", "кабалевскии",
        "khachaturian", "хачатурян", "sviridov", "свиридов", "gliere", "глиэр", "arensky", "аренскии", "lyadov", "liadov", "лядов",
        "medtner", "метнер", "gershwin", "гершвин", "joplin", "джоплин", "piazzolla", "пьяццолла", "couperin", "куперен", "rameau",
        "рамо", "telemann", "телеман", "purcell", "перселл", "dvorak", "дворжак", "smetana", "сметана", "janacek", "яначек",
        "elgar", "элгар", "mahler", "малер", "wagner", "вагнер", "verdi", "верди", "puccini", "пуччини", "rossini", "россини",
        "bizet", "бизе", "gounod", "гуно", "paganini", "паганини", "sibelius", "сибелиус", "hummel", "гуммель", "kuhlau", "кулау",
        "diabelli", "диабелли", "heller", "геллер", "gurlitt", "гурлитт", "hanon", "ганон", "tiersen", "тирсен", "yiruma", "ирума",
        "hisaishi", "хисаиси", "nyman", "наиман", "пярт", "gubaidulina", "губаидулина", "schnittke", "шнитке", "stravinsky",
        "strawinsky", "стравинскии", "messiaen", "мессиан", "poulenc", "пуленк", "albinoni", "альбинони", "pachelbel", "пахельбель",
        "corelli", "корелли", "boccherini", "боккерини", "weber", "вебер", "paderewski", "падеревскии", "moszkowski", "мошковскии",
        "grechaninov", "гречанинов", "maykapar", "маикапар", "goedicke", "gedike", "гедике", "shchedrin", "щедрин", "glazunov",
        "глазунов", "balakirev", "балакирев", "cui", "кюи", "taneyev", "танеев", "lysenko", "лысенко", "gavrilin", "гаврилин",
        "tartini", "тартини", "monteverdi", "монтеверди", "palestrina", "палестрина", "lully", "люлли", "dussek", "дусик",
    };

    /// <summary>Words of a title, a form, a key or a tempo, folded: a part with one of them is not a composer's name.</summary>
    private static readonly HashSet<string> s_titleWords = new(StringComparer.Ordinal)
    {
        "prelude", "preludes", "preludium", "fugue", "fuga", "fugues", "sonata", "sonatas", "sonate", "sonatina", "sonatine", "etude",
        "etudes", "study", "studies", "nocturne", "nocturnes", "waltz", "waltzes", "valse", "walzer", "mazurka", "mazurkas",
        "polonaise", "ballade", "ballad", "scherzo", "impromptu", "rondo", "minuet", "menuet", "menuetto", "march", "marche",
        "gavotte", "sarabande", "gigue", "allemande", "courante", "toccata", "invention", "inventions", "sinfonia", "symphony",
        "concerto", "suite", "partita", "fantasia", "fantasy", "fantaisie", "variations", "theme", "song", "songs", "lied",
        "romance", "berceuse", "barcarolle", "rhapsody", "arabesque", "reverie", "elegie", "elegy", "intermezzo", "capriccio",
        "caprice", "bagatelle", "humoresque", "serenade", "tango", "ragtime", "rag", "blues", "aria", "air", "chorale", "piano",
        "score", "scores", "sheet", "music", "notes", "book", "vol", "volume", "major", "minor", "dur", "moll", "flat", "sharp",
        "allegro", "adagio", "andante", "andantino", "moderato", "largo", "lento", "presto", "vivace", "maestoso", "the", "of",
        "for", "and", "in", "de", "la", "le", "op", "opus", "no", "bwv", "wtc", "solo", "duet", "trio", "easy", "прелюдия",
        "прелюдии", "фуга", "фуги", "соната", "сонаты", "сонатина", "этюд", "этюды", "ноктюрн", "ноктюрны", "вальс", "вальсы",
        "мазурка", "мазурки", "полонез", "баллада", "скерцо", "экспромт", "рондо", "менуэт", "марш", "гавот", "сарабанда", "жига",
        "токката", "инвенция", "инвенции", "симфония", "концерт", "сюита", "партита", "фантазия", "вариации", "тема", "песня",
        "песни", "романс", "колыбельная", "баркарола", "рапсодия", "пьеса", "пьесы", "ноты", "мажор", "минор", "для",
        "фортепиано", "и", "в", "том", "часть", "тетрадь", "альбом", "детскии", "сборник", "хтк", "соч", "ария", "хорал",
        "легкие", "избранное", "разное",
    };
}