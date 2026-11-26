using System.Globalization;

using MotiffLab.MusicCore.Analysis;
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

    /// <summary>A bridge candidate music-core made at a seam (<see cref="DraftBridges"/>).</summary>
    Bridge,

    /// <summary>A place's base with a приём of another place carried into it by music-core (<see cref="TechniqueCarry"/>).</summary>
    Carried,
}

/// <summary>
/// The texts of drafts (decision record 0034, section 2; spec «Экран (iPad)»), in the language of <see cref="Words"/>: a
/// place's label over its start («① Ноты 9–16 + Запись · 2 варианта»), a draft's line in Sources («из нот с PDF и
/// записи»), the kind of a приём («октава») and the name a new draft takes («Черновик 2»).
/// </summary>
public static class DraftLabels
{
    /// <summary>What the variant was copied from: a bridge or a carried приём music-core made, another piece's Notes, Notes heard in a recording, read from a PDF, or a file's.</summary>
    public static DraftSourceKind Kind(DraftVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return DraftBridges.KindOf(variant) is not null ? DraftSourceKind.Bridge
            : TechniqueCarry.KindOf(variant) is not null ? DraftSourceKind.Carried
            : variant.From.Project is not null ? DraftSourceKind.OtherPiece
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
        List<string> parts = [$"{Circled(index + 1)} {Name(Kind(first), words)}{Bars(first)}"];
        parts.AddRange(place.Variants.Select(Kind).Distinct().Where(kind => kind != Kind(first)).Select(kind => $"+ {Name(kind, words)}"));
        string label = string.Join(" ", parts);
        return place.Variants.Count > 1 ? $"{label} · {words.Count(place.Variants.Count, "variant", "variants", "вариант", "варианта", "вариантов")}" : label;
    }

    /// <summary>
    /// A variant on the draft's map: what it was copied from with the bars there, and the part taken when not all:
    /// «Запись 9–12 · правая рука», "Recording 9–12 · right hand"; a bridge candidate by its kind, «Доминанта»; a carried
    /// приём by its kind, «Перенесённый приём: октава», "Carried technique: octave".
    /// </summary>
    public static string Variant(DraftVariant variant, Words words)
    {
        ArgumentNullException.ThrowIfNull(variant);
        if (DraftBridges.KindOf(variant) is { } bridge)
        {
            return DraftBridges.Label(bridge, words);
        }

        if (TechniqueCarry.KindOf(variant) is { } carried)
        {
            return words.T($"Carried technique: {Technique(carried, words)}", $"Перенесённый приём: {Technique(carried, words)}");
        }

        string label = $"{Name(Kind(variant), words)}{Bars(variant)}";
        string? part = variant.From.Part switch
        {
            DraftPart.Right => words.T("right hand", "правая рука"),
            DraftPart.Left => words.T("left hand", "левая рука"),
            DraftPart.Melody => words.T("melody", "мелодия"),
            _ => null,
        };
        return part is null ? label : $"{label} · {part}";
    }

    /// <summary>
    /// The draft's line under its name in Sources: what its variants, in places and on the shelf, were copied from, each
    /// once in the order first met: «из нот с PDF и записи», "from the PDF's notes and the recording"; «пока пусто», "empty
    /// so far", for a draft with no variant (a file may hold one).
    /// </summary>
    public static string Caption(Draft draft, Words words)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<string> kinds = [.. draft.Variants.Select(Kind).Distinct().Select(kind => Of(kind, words))];
        return kinds.Count == 0 ? words.T("empty so far", "пока пусто") : words.T($"from {words.Joined(kinds)}", $"из {words.Joined(kinds)}");
    }

    /// <summary>The kind of a приём in a word, the spec's nouns: «заполнение», «другой бас», «упрощение», «украшение», «октава», «ритм», «вставка тактов», «иначе».</summary>
    public static string Technique(TechniqueKind kind, Words words) => kind switch
    {
        TechniqueKind.Fill => words.T("fill", "заполнение"),
        TechniqueKind.Bass => words.T("another bass", "другой бас"),
        TechniqueKind.Simplify => words.T("simplification", "упрощение"),
        TechniqueKind.Ornament => words.T("ornament", "украшение"),
        TechniqueKind.Octave => words.T("octave", "октава"),
        TechniqueKind.Rhythm => words.T("rhythm", "ритм"),
        TechniqueKind.InsertBars => words.T("inserted bars", "вставка тактов"),
        _ => words.T("other", "иначе"),
    };

    /// <summary>A key as a seam names it: «соль мажор», "G major"; by its signature, «1 диез», "1 sharp", where its source states no mode.</summary>
    public static string Key(KeySignature key, Words words)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Mode is { } mode ? new MusicalKey(key.Fifths, mode).NameIn(words) : KeyReference.CountOf(key.Fifths, words);
    }

    /// <summary>
    /// The offer to transpose the place at <paramref name="index"/> (from 0) to <paramref name="key"/>: «② в соль мажор», "② to
    /// G major"; «② в тональность ①», "② into ①'s key", where the key has no mode to name it by.
    /// </summary>
    public static string Transpose(int index, KeySignature key, Words words)
    {
        ArgumentNullException.ThrowIfNull(key);
        string place = Circled(index + 1);
        return key.Mode is not null
            ? words.T($"{place} to {Key(key, words)}", $"{place} в {Key(key, words)}")
            : words.T($"{place} into {Circled(index)}'s key", $"{place} в тональность {Circled(index)}");
    }

    /// <summary>The offer to play a place at the tempo of the place before it, at <paramref name="index"/> (from 0): «Темп как в ①», "Tempo as in ①".</summary>
    public static string TempoAsIn(int index, Words words) => words.T($"Tempo as in {Circled(index + 1)}", $"Темп как в {Circled(index + 1)}");

    /// <summary>
    /// The register of the place at <paramref name="index"/> (from 0) against the place before it, or the offer to move it an
    /// octave: «② на октаву выше», "② an octave higher"; «② на октаву ниже», "② an octave lower".
    /// </summary>
    public static string Octave(int index, bool up, Words words) => up
        ? words.T($"{Circled(index + 1)} an octave higher", $"{Circled(index + 1)} на октаву выше")
        : words.T($"{Circled(index + 1)} an octave lower", $"{Circled(index + 1)} на октаву ниже");

    /// <summary>The offer of a bridge at a seam: «Связка · 3 кандидата», "Bridge · 3 candidates".</summary>
    public static string Bridge(int candidates, Words words) =>
        $"{Name(DraftSourceKind.Bridge, words)} · {words.Count(candidates, "candidate", "candidates", "кандидат", "кандидата", "кандидатов")}";

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

    /// <summary>The bars a variant was copied from, after a space: " 9–16", " 9"; none when it does not say.</summary>
    private static string Bars(DraftVariant variant) => variant.From.Bars is var (from, to) ? from == to ? $" {from}" : $" {from}–{to}" : "";

    /// <summary>The number in a circle, ① to ⑳; past 20 in brackets.</summary>
    internal static string Circled(int number) =>
        number is >= 1 and <= 20 ? ((char)('①' + number - 1)).ToString() : string.Create(CultureInfo.InvariantCulture, $"({number})");

    private static string Name(DraftSourceKind kind, Words words) => kind switch
    {
        DraftSourceKind.Recording => words.T("Recording", "Запись"),
        DraftSourceKind.OtherPiece => words.T("Another piece", "Другая пьеса"),
        DraftSourceKind.Bridge => words.T("Bridge", "Связка"),
        DraftSourceKind.Carried => words.T("Technique", "Приём"),
        _ => words.T("Notes", "Ноты"),
    };

    private static string Of(DraftSourceKind kind, Words words) => kind switch
    {
        DraftSourceKind.PdfNotes => words.T("the PDF's notes", "нот с PDF"),
        DraftSourceKind.Recording => words.T("the recording", "записи"),
        DraftSourceKind.OtherPiece => words.T("another piece", "другой пьесы"),
        DraftSourceKind.Bridge => words.T("a bridge", "связки"),
        DraftSourceKind.Carried => words.T("a carried technique", "перенесённого приёма"),
        _ => words.T("the file's notes", "нот из файла"),
    };
}