namespace MotiffLab.MusicCore;

/// <summary>
/// The words of music the sentences of the core share, each written once (W1.8, D14): bars and their
/// numbers, their runs, intervals, key signatures, the English article and count, and a list cut short. The
/// sentences that use them stay with what they say; these never measure, they only name.
/// </summary>
public readonly partial record struct Words
{
    private static readonly string[] s_intervalNames =
        ["unison", "minor 2nd", "major 2nd", "minor 3rd", "major 3rd", "perfect 4th", "tritone", "perfect 5th", "minor 6th", "major 6th", "minor 7th", "major 7th"];

    // The same intervals in Russian, as named (nominative) and after "на" (accusative): "на малую терцию выше".
    private static readonly string[] s_russianIntervalNames =
        ["чистая прима", "малая секунда", "большая секунда", "малая терция", "большая терция", "чистая кварта", "тритон", "чистая квинта", "малая секста", "большая секста", "малая септима", "большая септима"];

    private static readonly string[] s_russianIntervalAccusative =
        ["чистую приму", "малую секунду", "большую секунду", "малую терцию", "большую терцию", "чистую кварту", "тритон", "чистую квинту", "малую сексту", "большую сексту", "малую септиму", "большую септиму"];

    /// <summary>"3", "1–8": a bar number, or a run of bars.</summary>
    internal static string Range(int first, int last) => first == last ? $"{first}" : $"{first}–{last}";

    /// <summary>Runs of consecutive numbers, in order: 1, 2, 5 gives (1, 2) and (5, 5).</summary>
    internal static List<(int First, int Last)> Runs(IReadOnlyList<int> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var runs = new List<(int First, int Last)>();
        for (int i = 0; i < bars.Count;)
        {
            int j = i;
            while (j + 1 < bars.Count && bars[j + 1] == bars[j] + 1)
            {
                j++;
            }

            runs.Add((bars[i], bars[j]));
            i = j + 1;
        }

        return runs;
    }

    /// <summary>"bar 3", "bars 1–8"; "такт 3", "такты 1–8".</summary>
    internal string Bars(int first, int last) => Bars(Range(first, last), first == last);

    /// <summary>"bar 3", "bars 2, 6"; "такт 3", "такты 2, 6": the bars' numbers as written, one bar or more.</summary>
    internal string Bars(string numbers, bool one) =>
        one ? T($"bar {numbers}", $"такт {numbers}") : T($"bars {numbers}", $"такты {numbers}");

    /// <summary>"in bar 5", "in bars 5–6"; "в такте 5", "в тактах 5–6".</summary>
    internal string InBars(int first, int last) => InBars(Range(first, last), first == last);

    /// <summary>"in bars 1–2, 5 and 9"; "в тактах 1–2, 5 и 9": the bars' numbers as written, one bar or more.</summary>
    internal string InBars(string numbers, bool one) => $"{T("in", "в")} {BarsPrepositional(numbers, one)}";

    /// <summary>"bar 5", "bars 5–6, 9"; "такте 5", "тактах 5–6, 9": the Russian in the prepositional, after «в».</summary>
    internal string BarsPrepositional(string numbers, bool one) =>
        one ? T($"bar {numbers}", $"такте {numbers}") : T($"bars {numbers}", $"тактах {numbers}");

    /// <summary>"bar 15", "bars 15–16"; "такта 15", "тактов 15–16": the Russian in the genitive.</summary>
    internal string BarsGenitive(int first, int last) =>
        first == last ? T($"bar {first}", $"такта {first}") : T($"bars {first}–{last}", $"тактов {first}–{last}");

    /// <summary>"minor 3rd", "octave and a major 2nd", "2 octaves"; "малая терция", "октава и большая секунда", "2 октавы".</summary>
    internal string IntervalName(int semitones)
    {
        int size = Math.Abs(semitones);
        int octaves = size / 12;
        int rest = size % 12;
        if (IsRussian)
        {
            return RussianInterval(octaves, rest, s_russianIntervalNames, "октава");
        }

        if (octaves == 0)
        {
            return s_intervalNames[rest];
        }

        string octaveText = octaves == 1 ? "octave" : $"{octaves} octaves";
        return rest == 0 ? octaveText : $"{octaveText} and {WithArticle(s_intervalNames[rest])}";
    }

    /// <summary>
    /// The interval as English says it inside a sentence, with its article ("a minor 3rd"), or as
    /// Russian says it after "на" ("на малую терцию выше"), without the "на".
    /// </summary>
    internal string IntervalBy(int semitones)
    {
        if (!IsRussian)
        {
            return WithArticle(IntervalName(semitones));
        }

        int size = Math.Abs(semitones);
        return RussianInterval(size / 12, size % 12, s_russianIntervalAccusative, "октаву");
    }

    private static string RussianInterval(int octaves, int rest, string[] names, string octave)
    {
        if (octaves == 0)
        {
            return names[rest];
        }

        string octaveText = octaves == 1 ? octave : $"{octaves} {RussianForm(octaves, "октава", "октавы", "октав")}";
        return rest == 0 ? octaveText : $"{octaveText} и {names[rest]}";
    }

    /// <summary>A key signature in a sentence: "no sharps or flats", "3 sharps"; "без диезов и бемолей", "3 диеза".</summary>
    internal string Accidentals(int fifths) => fifths == 0 ? T("no sharps or flats", "без диезов и бемолей") : SharpsOrFlats(fifths);

    /// <summary>A key signature as a label: "no sharps or flats", "2 flats"; "без знаков", "2 бемоля".</summary>
    internal string AccidentalsLabel(int fifths) => fifths == 0 ? T("no sharps or flats", "без знаков") : SharpsOrFlats(fifths);

    private string SharpsOrFlats(int fifths) => fifths > 0
        ? Count(fifths, "sharp", "sharps", "диез", "диеза", "диезов")
        : Count(-fifths, "flat", "flats", "бемоль", "бемоля", "бемолей");

    /// <summary>"a minor 3rd", "an octave", "a unison", "2 octaves": the English noun with its indefinite article.</summary>
    internal static string WithArticle(string noun) => noun[0] switch
    {
        >= '0' and <= '9' => noun,
        _ when noun.StartsWith("uni", StringComparison.Ordinal) => "a " + noun,
        'a' or 'e' or 'i' or 'o' or 'u' => "an " + noun,
        _ => "a " + noun,
    };

    /// <summary>"1 note", "3 notes": an English count, for a sentence written in English only.</summary>
    internal static string EnglishCount(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    /// <summary>
    /// The items joined by <paramref name="separator"/>, the list cut after <paramref name="max"/> with how many more:
    /// "a, b and 3 more"; "a, b и ещё 3". <paramref name="beforeMore"/> goes before the "and".
    /// </summary>
    internal string Listed(IReadOnlyList<string> items, int max, string separator, string beforeMore) =>
        items.Count <= max
            ? string.Join(separator, items)
            : T($"{string.Join(separator, items.Take(max))}{beforeMore}and {items.Count - max} more", $"{string.Join(separator, items.Take(max))}{beforeMore}и ещё {items.Count - max}");
}