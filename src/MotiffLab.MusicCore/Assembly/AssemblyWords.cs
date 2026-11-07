using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assembly;

/// <summary>The names of an assembly's sources as its sentences use them: «Кавер», «из Кавера», and the source of each step.</summary>
public sealed class AssemblyNames
{
    private readonly IReadOnlyDictionary<string, string> _names;
    private readonly IReadOnlyDictionary<int, string> _steps;
    private readonly Words _words;

    /// <param name="names">Each source's name by id.</param>
    /// <param name="steps">The journal, so a step's source can be named.</param>
    public AssemblyNames(IReadOnlyDictionary<string, string> names, IEnumerable<AssemblyStep> steps, Words words)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(steps);
        _names = names;
        _steps = steps.OfType<CarryStep>().ToDictionary(step => step.Number, step => step.SourceId);
        _words = words;
    }

    public string Of(string sourceId) => _names.GetValueOrDefault(sourceId) ?? _words.T("a removed version", "убранная версия");

    /// <summary>The name after «из»: «Кавера» for a one-word Russian name, as it is otherwise.</summary>
    public string GenitiveOf(string sourceId) => _words.IsRussian ? AssemblyWords.Genitive(Of(sourceId)) : Of(sourceId);

    public string OfStep(int number) => _steps.TryGetValue(number, out string? id) ? Of(id) : _words.T("a note change", "правка");

    public string GenitiveOfStep(int number) => _steps.TryGetValue(number, out string? id) ? GenitiveOf(id) : _words.T("a note change", "правки");
}

/// <summary>Small pieces of the assembly's sentences: lengths in bars, intervals, keys after «в», names after «из».</summary>
public static class AssemblyWords
{
    /// <summary>A length as part of a bar: «½ такта», «2 такта», «1½ такта», or beats when it is not a simple part: «2 доли».</summary>
    public static string Length(long ticks, TimeSignature signature, Words words)
    {
        long bar = signature.BarTicks;
        long whole = ticks / bar;
        long rest = ticks % bar;
        string? part = rest == 0 ? "" : (bar % rest == 0 ? (bar / rest) switch
        {
            2 => "½",
            3 => "⅓",
            4 => "¼",
            8 => "⅛",
            _ => null,
        } : rest * 3 == bar * 2 ? "⅔" : rest * 4 == bar * 3 ? "¾" : null);
        if (part is not null)
        {
            string amount = whole == 0 ? part : $"{whole}{part}";
            bool single = whole == 1 && part.Length == 0;
            return words.T(
                $"{amount} {(single ? "bar" : "bars")}",
                $"{amount} {(part.Length > 0 ? "такта" : Words.RussianForm((int)whole, "такт", "такта", "тактов"))}");
        }

        long beat = signature.BeatTicks;
        if (ticks % beat == 0)
        {
            int beats = (int)(ticks / beat);
            return words.Count(beats, "beat", "beats", "долю", "доли", "долей");
        }

        int eighths = (int)Math.Max(1, ticks / (CanonicalTime.TicksPerQuarter / 2));
        return words.Count(eighths, "eighth", "eighths", "восьмую", "восьмые", "восьмых");
    }

    /// <summary>«на тон вниз», "a tone down"; «на малую терцию вверх».</summary>
    public static string Interval(int semitones, Words words)
    {
        int size = Math.Abs(semitones);
        bool up = semitones > 0;
        (string english, string russian) = size switch
        {
            1 => ("a semitone", "полтона"),
            2 => ("a tone", "тон"),
            3 => ("a minor third", "малую терцию"),
            4 => ("a major third", "большую терцию"),
            5 => ("a fourth", "кварту"),
            6 => ("a tritone", "тритон"),
            7 => ("a fifth", "квинту"),
            _ => ($"{size} semitones", $"{size} {Words.RussianForm(size, "полутон", "полутона", "полутонов")}"),
        };
        return words.T($"{english} {(up ? "up" : "down")}", $"на {russian} {(up ? "вверх" : "вниз")}");
    }

    /// <summary>A key after «в»: «ля миноре», «ми-бемоль мажоре»; the English name as it is.</summary>
    public static string KeyPrepositional(MusicalKey key, Words words)
    {
        string name = key.NameIn(words);
        return words.IsRussian ? name + "е" : name;
    }

    /// <summary>A one-word Russian name after «из»: «Кавер» → «Кавера», «Запись» → «Записи», «Версия» → «Версии»; other names as they are.</summary>
    public static string Genitive(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length < 2 || name.Contains(' ', StringComparison.Ordinal) || !name.All(c => c is >= 'А' and <= 'я' or 'ё' or 'Ё'))
        {
            return name;
        }

        char last = char.ToLowerInvariant(name[^1]);
        string stem = name[..^1];
        return last switch
        {
            'а' => stem + (stem.Length > 0 && "гкхжчшщ".Contains(char.ToLowerInvariant(stem[^1]), StringComparison.Ordinal) ? "и" : "ы"),
            'я' => stem + "и",
            'ь' => stem + "и",
            'й' => stem + "я",
            'о' or 'е' or 'и' or 'ы' or 'у' or 'ю' or 'э' => name,
            _ => name + "а",
        };
    }
}