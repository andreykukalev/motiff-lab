using System.Globalization;

using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>What a variant was copied from, for its place's label and its draft's line in Sources.</summary>
public enum DraftSourceKind
{
    /// <summary>Notes read from a PDF or photos of pages.</summary>
    PdfNotes,

    /// <summary>Notes of a file, entered or put together.</summary>
    FileNotes,

    /// <summary>Notes heard in a recording.</summary>
    Recording,

    /// <summary>Notes of another piece.</summary>
    OtherPiece,
}

/// <summary>
/// The texts of drafts (decision record 0034, section 2; spec «Экран (iPad)»), in the language of <see cref="Words"/>: a
/// place's label over its start («① Ноты 9–16 + Запись · 2 варианта»), a draft's line in Sources («из нот с PDF и
/// записи») and the name a new draft takes («Черновик 2»).
/// </summary>
public static class DraftLabels
{
    /// <summary>What the variant was copied from: another piece's Notes, Notes heard in a recording, read from a PDF, or a file's.</summary>
    public static DraftSourceKind Kind(DraftVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return variant.From.Project is not null ? DraftSourceKind.OtherPiece
            : variant.Provenance.Op == NotesProvenance.AudioRecognition || variant.From.Milliseconds is not null ? DraftSourceKind.Recording
            : variant.Provenance.Op == NotesProvenance.PdfRecognition ? DraftSourceKind.PdfNotes
            : DraftSourceKind.FileNotes;
    }

    /// <summary>
    /// The label of the place at <paramref name="index"/> (from 0) of its draft: its number in a circle, what its base was
    /// copied from with the bars there, what its other variants were copied from, and how many variants it has when more
    /// than one: «① Ноты 9–16 + Запись · 2 варианта», "① Notes 9–16 + Recording · 2 variants".
    /// </summary>
    public static string Place(DraftPlace place, int index, Words words)
    {
        ArgumentNullException.ThrowIfNull(place);
        DraftVariant first = place.BaseVariant;
        string bars = first.From.Bars is var (from, to) ? from == to ? $" {from}" : $" {from}–{to}" : "";
        List<string> parts = [$"{Circled(index + 1)} {Name(Kind(first), words)}{bars}"];
        parts.AddRange(place.Variants.Select(Kind).Distinct().Where(kind => kind != Kind(first)).Select(kind => $"+ {Name(kind, words)}"));
        string label = string.Join(" ", parts);
        return place.Variants.Count > 1 ? $"{label} · {words.Count(place.Variants.Count, "variant", "variants", "вариант", "варианта", "вариантов")}" : label;
    }

    /// <summary>
    /// The draft's line under its name in Sources: what its variants, in places and on the shelf, were copied from, each
    /// once in the order first met: «из нот с PDF и записи», "from the PDF's notes and the recording"; «пока пусто», "empty
    /// so far", for a draft with no variant (a file may hold one).
    /// </summary>
    public static string Caption(Draft draft, Words words)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<string> kinds = [.. draft.Places.SelectMany(static place => place.Variants).Concat(draft.Shelf).Select(Kind).Distinct().Select(kind => Of(kind, words))];
        return kinds.Count == 0 ? words.T("empty so far", "пока пусто") : words.T($"from {words.Joined(kinds)}", $"из {words.Joined(kinds)}");
    }

    /// <summary>The name of a new draft of <paramref name="project"/>: «Черновик N», "Draft N", N one more than its drafts, the first such name not taken.</summary>
    public static string NewName(Project project, Words words)
    {
        ArgumentNullException.ThrowIfNull(project);
        HashSet<string> taken = [.. project.Drafts.Select(static draft => draft.Name)];
        int number = project.Drafts.Count + 1;
        while (taken.Contains(NameNumbered(number, words)))
        {
            number++;
        }

        return NameNumbered(number, words);
    }

    private static string NameNumbered(int number, Words words) => string.Create(CultureInfo.InvariantCulture, $"{words.T("Draft", "Черновик")} {number}");

    /// <summary>The number in a circle, ① to ⑳; past 20 in brackets.</summary>
    private static string Circled(int number) =>
        number is >= 1 and <= 20 ? ((char)('①' + number - 1)).ToString() : string.Create(CultureInfo.InvariantCulture, $"({number})");

    private static string Name(DraftSourceKind kind, Words words) => kind switch
    {
        DraftSourceKind.Recording => words.T("Recording", "Запись"),
        DraftSourceKind.OtherPiece => words.T("Another piece", "Другая пьеса"),
        _ => words.T("Notes", "Ноты"),
    };

    private static string Of(DraftSourceKind kind, Words words) => kind switch
    {
        DraftSourceKind.PdfNotes => words.T("the PDF's notes", "нот с PDF"),
        DraftSourceKind.Recording => words.T("the recording", "записи"),
        DraftSourceKind.OtherPiece => words.T("another piece", "другой пьесы"),
        _ => words.T("the file's notes", "нот из файла"),
    };
}