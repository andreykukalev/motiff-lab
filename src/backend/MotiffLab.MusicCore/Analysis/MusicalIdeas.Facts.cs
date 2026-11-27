using MotiffLab.MusicCore.Analysis.Phrasing;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// The ideas' names, titles and lines (why each was chosen) in both languages, written from what <c>MusicalIdeas.cs</c>
/// measured (<see cref="ThemeFound"/>, <see cref="ContrastFound"/>, <see cref="SequenceFound"/>, the peak's bar and
/// height). No notes, meter or search belong here.
/// </summary>
internal static partial class MusicalIdeas
{
    /// <summary>Notes per beat this many times the theme's, or fewer by as much, are said to be denser or sparser.</summary>
    private const double DensityRatio = 1.5;

    /// <summary>"Theme · bars 1–8"; "Тема · такты 1–8".</summary>
    private static string Title(string name, int firstBar, int lastBar, Words w) => $"{name} · {w.Bars(firstBar, lastBar)}";

    private static string MotifTitle(int firstBar, int lastBar, Words w) => Title(w.T("Motif", "Мотив"), firstBar, lastBar, w);

    private static (string Name, string Why) ThemeText(ThemeFound found, Words w)
    {
        string why = found.Period
            ? w.T("The first two phrases begin alike; the first ends on a half close, the second on a full one, as a question and its answer.",
                  "Первые две фразы начинаются одинаково; первая кончается половинным кадансом, вторая полным, как вопрос и ответ.")
            : w.T("The piece's first phrase", "Первая фраза пьесы") + found.FirstClose switch
            {
                CloseKind.Full => w.T(", ending on a full close.", ", кончается полным кадансом."),
                CloseKind.Half => w.T(", ending on a half close.", ", кончается половинным кадансом."),
                _ => ".",
            };
        if (found.Returns > 0)
        {
            why += " " + Returns(found, w);
        }

        return (w.T("Theme", "Тема"), why);
    }

    /// <summary>"Its opening comes back 9 times, 2 of them at another pitch, and opens A twice and B once."</summary>
    private static string Returns(ThemeFound found, Words w)
    {
        string text = w.T("Its opening comes back ", "Её начало возвращается ") + w.Times(found.Returns);
        if (found.Moved > 0)
        {
            text += w.T($", {found.Moved} of them at another pitch", $", из них {found.Moved} в другой высоте");
        }

        if (found.Opens.Count > 0)
        {
            text += w.T(" and opens ", " и открывает ") + w.Joined(w.Tallied(found.Opens));
        }

        return text + ".";
    }

    private static (string Name, string Why) ContrastText(ContrastFound found, Words w)
    {
        List<string> differences = [.. new[] { DensityAgainst(found, w), RegisterAgainst(found, w), KeyAgainst(found, w) }.OfType<string>()];
        string why = w.T($"Section {found.Label} begins here, with material not heard in {found.OpeningLabel}",
                         $"Здесь начинается раздел {found.Label}, с материалом, которого не было в {found.OpeningLabel}")
            + (differences.Count > 0 ? ": " + w.Joined(differences) : "") + ".";
        return (w.T($"Contrast · {found.Label}", $"Контраст · {found.Label}"), why);
    }

    /// <summary>"denser (2.3 notes a beat against 1.1)", "sparser (…)", or null when the two are close.</summary>
    private static string? DensityAgainst(ContrastFound found, Words w)
    {
        double density = found.Density;
        double against = found.Against;
        string numbers = w.T($" ({w.Decimal((decimal)density)} notes a beat against {w.Decimal((decimal)against)})",
                             $" ({w.Decimal((decimal)density)} ноты на долю против {w.Decimal((decimal)against)})");
        return against > 0 && density >= against * DensityRatio ? w.T("denser", "плотнее") + numbers
            : density > 0 && density * DensityRatio <= against ? w.T("sparser", "реже") + numbers
            : null;
    }

    /// <summary>"reaching an octave higher", "staying 7 semitones lower", by the highest notes, or null when they are close.</summary>
    private static string? RegisterAgainst(ContrastFound found, Words w)
    {
        int high = found.Higher;
        return high >= RegisterStep ? w.T("reaching ", "поднимается на ") + Interval(high, w) + w.T(" higher", " выше")
            : high <= -RegisterStep ? w.T("staying ", "остаётся на ") + Interval(-high, w) + w.T(" lower", " ниже")
            : null;
    }

    /// <summary>"in C major", with "(inferred)" when the key lane infers it, or null in the theme's key.</summary>
    private static string? KeyAgainst(ContrastFound found, Words w) =>
        found.Key is { } key
            ? w.T("in ", "в ") + key.Key.NameIn(w) + (key.Inferred ? w.T(" (inferred)", " (выведено)") : "")
            : null;

    private static (string Name, string Why) SequenceText(SequenceFound found, Words w)
    {
        int[] steps = found.Steps;
        string how = steps.Distinct().Count() == 1
            ? w.T("each time ", "каждый раз на ") + Interval(Math.Abs(steps[0]), w) + (steps[0] > 0 ? w.T(" higher", " выше") : w.T(" lower", " ниже"))
            : steps.All(step => step > 0) ? w.T("each time higher", "каждый раз выше")
            : steps.All(step => step < 0) ? w.T("each time lower", "каждый раз ниже")
            : w.T("each time at another pitch", "каждый раз в другой высоте");
        string why = w.T($"One bar heard {found.Statements} times in a row, {how}: a sequence.",
                         $"Один такт звучит {w.Times(found.Statements)} подряд, {how}: секвенция.");
        return (w.T("Sequence", "Секвенция"), why);
    }

    /// <summary>The peak's line: its <paramref name="bar"/> and how far its note lies <paramref name="above"/> the theme's highest, in semitones.</summary>
    private static (string Name, string Why) PeakText(int bar, int above, Words w)
    {
        string why = w.T($"Holds the highest note of the piece, in bar {bar}, ", $"Здесь самая высокая нота пьесы, в такте {bar}, на ")
            + Interval(above, w) + w.T(" above anything in the theme.", " выше всего в теме.");
        return (w.T("Highest point", "Вершина"), why);
    }

    /// <summary>"an octave", "2 octaves", "7 semitones"; "октаву", "2 октавы", "7 полутонов".</summary>
    private static string Interval(int semitones, Words w) => semitones switch
    {
        12 => w.T("an octave", "октаву"),
        _ when semitones % 12 == 0 => w.Count(semitones / 12, "octave", "octaves", "октаву", "октавы", "октав"),
        _ => w.Count(semitones, "semitone", "semitones", "полутон", "полутона", "полутонов"),
    };
}