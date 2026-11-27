using System.Globalization;

using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Scan;

/// <summary>What the player marks as printed otherwise than the notes were read («Как напечатано», step 3; concept 3.1).</summary>
public enum ReadingMarkKind
{
    /// <summary>The time signature printed from the bar on, until the next such mark ("5/4").</summary>
    Meter,

    /// <summary>The key signature printed from the bar on, until the next such mark: its sharps (1 to 7) or flats (-1 to -7), 0 for none.</summary>
    Key,

    /// <summary>The clef printed on a staff over the bars ("treble" or "bass").</summary>
    Clef,

    /// <summary>An octave sign printed on a staff over the bars ("8va" an octave higher, "8vb" an octave lower).</summary>
    Octave,

    /// <summary>Whether the bars are printed in triplets ("yes") or not ("no").</summary>
    Triplets,

    /// <summary>The two hands' staves swapped in the bars.</summary>
    Hands,

    /// <summary>The bars read badly, with nothing more said.</summary>
    BadlyRead,
}

/// <summary>
/// One mark «что не так» on notes read from a PDF or photos («Как напечатано», step 3; project file 18): what the player
/// says the page prints where the reading went wrong, kept with the notes (<c>ScanOrigin.Marks</c>) and used by the next
/// reading of the same pages (<see cref="ReadingMarks"/>). A wrong note is never marked: the player corrects it. What a
/// mark says is the player's, shown amber; it changes no note until the pages are read again.
/// </summary>
/// <param name="Kind">What is marked.</param>
/// <param name="Bar">The first bar, as the workspace numbers it.</param>
/// <param name="Last">The last bar; a meter or key mark holds from <paramref name="Bar"/> on, and its last is its first.</param>
/// <param name="Staff">The staff of a clef or octave mark, from 1 (the upper); null for the others.</param>
/// <param name="Value">What the page prints: a meter's "5/4", a key's sharps or flats, a clef's name, an octave's sign, triplets' "yes" or "no"; null for hands and a bar read badly.</param>
public sealed record ReadingMark(ReadingMarkKind Kind, int Bar, int Last, int? Staff, string? Value)
{
    /// <summary>The clefs a clef mark names, with the sign and line MusicXML writes them by.</summary>
    internal static readonly IReadOnlyDictionary<string, (char Sign, int Line)> Clefs = new Dictionary<string, (char, int)>(StringComparer.Ordinal)
    {
        ["treble"] = ('G', 2),
        ["bass"] = ('F', 4),
    };

    public ReadingMarkKind Kind { get; } = Enum.IsDefined(Kind) ? Kind : throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown mark.");

    public int Bar { get; } = Bar >= 0 ? Bar : throw new ArgumentOutOfRangeException(nameof(Bar), Bar, "A bar number is not negative.");

    public int Last { get; } = Last >= Bar && (Last == Bar || !FromBarOn(Kind))
        ? Last
        : throw new ArgumentOutOfRangeException(nameof(Last), Last, "The last bar is at or after the first; a meter or key mark has one bar.");

    public int? Staff { get; } = (Kind is ReadingMarkKind.Clef or ReadingMarkKind.Octave) == Staff is >= 1
        ? Staff
        : throw new ArgumentException("A clef or octave mark names its staff, from 1; no other mark does.", nameof(Staff));

    public string? Value { get; } = Valid(Kind, Value) ? Value : throw new ArgumentException($"\"{Value}\" is not what a {Kind} mark says.", nameof(Value));

    /// <summary>Whether a mark of the kind holds from its bar on, until the next of its kind, rather than over its bars.</summary>
    public static bool FromBarOn(ReadingMarkKind kind) => kind is ReadingMarkKind.Meter or ReadingMarkKind.Key;

    /// <summary>The time signature a meter mark says.</summary>
    internal TimeSignature Meter => Kind == ReadingMarkKind.Meter && Signature(Value) is { } meter ? meter : throw new InvalidOperationException("Not a meter mark.");

    /// <summary>The sharps (above 0) or flats (below 0) a key mark says.</summary>
    internal int Fifths => Kind == ReadingMarkKind.Key ? int.Parse(Value!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : throw new InvalidOperationException("Not a key mark.");

    /// <summary>Whether <paramref name="other"/> marks the same thing at the same place (kind, first bar, staff), so one takes the other's place.</summary>
    public bool SamePlace(ReadingMark other) => other.Kind == Kind && other.Bar == Bar && other.Staff == Staff;

    /// <summary>The parts of this mark's bars outside <paramref name="other"/>'s, each the same mark over them: none, one or two.</summary>
    internal IEnumerable<ReadingMark> Outside(ReadingMark other)
    {
        if (Bar < other.Bar)
        {
            yield return new ReadingMark(Kind, Bar, Math.Min(Last, other.Bar - 1), Staff, Value);
        }

        if (Last > other.Last)
        {
            yield return new ReadingMark(Kind, Math.Max(Bar, other.Last + 1), Last, Staff, Value);
        }
    }

    /// <summary>The mark on other bars (the notes read again): <paramref name="bar"/> on, as many bars as it had.</summary>
    internal ReadingMark At(int bar) => new(Kind, bar, bar + (Last - Bar), Staff, Value);

    /// <summary>The mark in words for Check: «Размер здесь 5/4, с такта 5», "Hands swapped, bars 3–4".</summary>
    public string Text(Words words)
    {
        string bars = Kind is ReadingMarkKind.Meter or ReadingMarkKind.Key
            ? words.T($"from bar {Bar}", $"с такта {Bar}")
            : words.Bars(Bar, Last);
        string staff = Staff switch
        {
            1 => words.T("upper staff", "верхний стан"),
            2 => words.T("lower staff", "нижний стан"),
            _ => words.T($"staff {Staff}", $"стан {Staff}"),
        };
        string what = Kind switch
        {
            ReadingMarkKind.Meter => words.T($"Time signature here: {Value}", $"Размер здесь {Value}"),
            ReadingMarkKind.Key => words.T($"Key signature here: {KeyText(words)}", $"Тональность здесь: {KeyText(words)}"),
            ReadingMarkKind.Clef => Value == "bass" ? words.T($"Bass clef here, {staff}", $"Здесь басовый ключ, {staff}") : words.T($"Treble clef here, {staff}", $"Здесь скрипичный ключ, {staff}"),
            ReadingMarkKind.Octave => Value == "8va" ? words.T($"8va here (an octave higher), {staff}", $"Здесь 8va (октавой выше), {staff}") : words.T($"8vb here (an octave lower), {staff}", $"Здесь 8vb (октавой ниже), {staff}"),
            ReadingMarkKind.Triplets => Value == "yes" ? words.T("Triplets here", "Здесь триоли") : words.T("No triplets here", "Здесь нет триолей"),
            ReadingMarkKind.Hands => words.T("Hands swapped", "Руки перепутаны"),
            _ => words.T("Read badly", "Прочитано плохо"),
        };
        return $"{what}, {bars}";
    }

    private string KeyText(Words words) => words.AccidentalsLabel(Fifths);

    private static bool Valid(ReadingMarkKind kind, string? value) => kind switch
    {
        ReadingMarkKind.Meter => Signature(value) is not null,
        ReadingMarkKind.Key => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int fifths) && fifths is >= -7 and <= 7
            && value == fifths.ToString(CultureInfo.InvariantCulture),
        ReadingMarkKind.Clef => value is not null && Clefs.ContainsKey(value),
        ReadingMarkKind.Octave => value is "8va" or "8vb",
        ReadingMarkKind.Triplets => value is "yes" or "no",
        _ => value is null,
    };

    /// <summary>"5/4" as a time signature; null when it is not one.</summary>
    private static TimeSignature? Signature(string? value)
    {
        string[] parts = value?.Split('/') ?? [];
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int beats)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int beatType)
            || $"{beats}/{beatType}" != value)
        {
            return null;
        }

        try
        {
            return new TimeSignature(beats, beatType);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}