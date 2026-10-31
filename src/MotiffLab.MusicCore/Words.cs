namespace MotiffLab.MusicCore;

/// <summary>The languages music-core writes its sentences in.</summary>
public enum TextLanguage
{
    English,
    Russian,
}

/// <summary>
/// The language a fact, a basis or a place label is written in, and the few grammar helpers both
/// languages need (#134). Each sentence is written in both languages where it is built, so the
/// numbers, cases and plurals agree; the UI shows the text as it comes and never rewrites it.
/// </summary>
/// <remarks>
/// The default is English, so a caller that names no language gets the text the tests and the AI
/// digest read. Russian terms follow decision record 0013 (§7): такт, доля, мотив, фраза; the claim
/// kinds are обнаружено, предположено. Note names stay letters with the octave ("E5"), as the score
/// view and the roll write them; key names use the Russian syllables ("ля минор").
/// </remarks>
public readonly record struct Words(TextLanguage Language)
{
    public static Words English { get; } = new(TextLanguage.English);

    public static Words Russian { get; } = new(TextLanguage.Russian);

    public bool IsRussian => Language == TextLanguage.Russian;

    /// <summary>The words for a language tag such as "ru" or "en-GB"; any tag but Russian gives English.</summary>
    public static Words For(string? languageTag) =>
        languageTag is not null && languageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? Russian : English;

    /// <summary>The English or the Russian text.</summary>
    public string T(string english, string russian) => IsRussian ? russian : english;

    /// <summary>"1 note", "3 notes"; "1 нота", "3 ноты", "5 нот".</summary>
    public string Count(int count, string one, string many, string russianOne, string russianFew, string russianMany) =>
        $"{count} {Noun(count, one, many, russianOne, russianFew, russianMany)}";

    /// <summary>The noun that goes with a count, without the count.</summary>
    public string Noun(int count, string one, string many, string russianOne, string russianFew, string russianMany) =>
        IsRussian ? RussianForm(count, russianOne, russianFew, russianMany) : count == 1 ? one : many;

    /// <summary>A number to a tenth as the language writes it: "92.3"; "92,3".</summary>
    public string Decimal(decimal value) =>
        value.ToString("0.#", IsRussian ? s_russianNumbers : System.Globalization.CultureInfo.InvariantCulture);

    private static readonly System.Globalization.NumberFormatInfo s_russianNumbers = new() { NumberDecimalSeparator = "," };

    /// <summary>"of 5 notes"; "из 5 нот", "из 1 ноты": the Russian takes the genitive.</summary>
    public string OutOf(int count, string one, string many, string russianGenitiveOne, string russianGenitiveMany) =>
        IsRussian
            ? $"из {count} {RussianForm(count, russianGenitiveOne, russianGenitiveMany, russianGenitiveMany)}"
            : $"of {count} {(count == 1 ? one : many)}";

    /// <summary>"a", "a and b", "a, b and c"; "a, b и c".</summary>
    public string Joined(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Count == 1 ? items[0] : $"{string.Join(", ", items.Take(items.Count - 1))} {T("and", "и")} {items[^1]}";
    }

    /// <summary>"once", "twice", "3 times"; "1 раз", "2 раза", "5 раз".</summary>
    public string Times(int count) => IsRussian
        ? $"{count} {RussianForm(count, "раз", "раза", "раз")}"
        : count switch
        {
            1 => "once",
            2 => "twice",
            _ => $"{count} times",
        };

    /// <summary>
    /// The Russian form for a count: <paramref name="one"/> for 1, 21, 31…; <paramref name="few"/>
    /// for 2–4, 22–24…; <paramref name="many"/> for 0, 5–20, 25–30…
    /// </summary>
    public static string RussianForm(int count, string one, string few, string many)
    {
        int n = Math.Abs(count) % 100;
        int last = n % 10;
        return n is >= 11 and <= 14 ? many : last switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
    }

    /// <summary>The text with its first letter in capitals.</summary>
    public static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}