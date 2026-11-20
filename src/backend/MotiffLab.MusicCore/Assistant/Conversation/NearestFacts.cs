using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>
/// The facts of a digest nearest to the question (assistant option A, item 5), for the engine's template: deterministic,
/// from what the digest itself says. A fact is near when its bars meet the bars the question names, in words («такты 5–8»,
/// «bar 12») or by a section's letter («часть B», «section A′»), or, when it names none, the selected bars of a passage's
/// digest; and when its kind is one the question's words ask about («аккорды», «tempo», «как учить»), in either language.
/// Facts near both ways come first, then near by bars, then by kind, then the rest; within each, the nearest bars, then the
/// narrowest (when the question names bars), then the digest's order, one fact of each kind before a second of any.
/// </summary>
internal static partial class NearestFacts
{
    /// <summary>The kinds of fact a stem of the question asks about; a stem matches the start of a word.</summary>
    private static readonly (Regex Words, string[] Kinds)[] s_kinds =
    [
        (Stems("тональн", "лад", "минор", "мажор", "модуляц", "key", "minor", "major", "modulat"), ["key"]),
        (Stems("обзор", "целом", "overview", "overall", "summary"), ["summary"]),
        (Stems("форм", "част", "раздел", "структур", "построен", "повтор", "возвращ", "реприз", "form", "section", "part", "structure", "repeat", "return", "reprise"), ["form", "structure"]),
        (Stems("фраз", "дыхан", "phrase", "breath"), ["phrase"]),
        (Stems("предложен", "период", "sentence", "period"), ["sentence"]),
        (Stems("аккорд", "гармон", "ступен", "chord", "harmon", "degree"), ["chords", "chord", "harmony"]),
        (Stems("каденц", "каданс", "заверш", "cadence", "close"), ["cadence"]),
        (Stems("плотн", "трудн", "сложн", "кульминац", "dense", "busy", "busiest", "hard", "difficult", "tricky", "climax"), ["density"]),
        (Stems("новы", "новое", "нового", "новая", "материал", "new", "material"), ["newMaterial"]),
        (Stems("рук", "диапазон", "регистр", "левой", "правой", "левая", "правая", "hand", "range", "register", "left", "right"), ["range"]),
        (Stems("темп", "скорост", "медлен", "быстр", "метроном", "tempo", "bpm", "speed", "slow", "fast", "metronom"), ["tempo"]),
        (Stems("провер", "ошиб", "распозн", "check", "error", "mistake", "scan"), ["check"]),
        (Stems("мелод", "melod", "tune"), ["melody"]),
        (Stems("ритм", "rhythm"), ["rhythm"]),
        (Stems("фактур", "texture"), ["texture"]),
        (Stems("характер", "настроен", "character", "mood"), ["character"]),
        (Stems("учить", "учи", "разуч", "выуч", "занима", "упражн", "практи", "начать", "learn", "practi", "study", "start"), ["form", "density", "newMaterial", "range", "tempo"]),
    ];

    /// <summary>The digest's facts (id and line), the nearest to <paramref name="question"/> first; every fact once.</summary>
    internal static IReadOnlyList<(string Id, string Line)> Of(JsonElement digest, string? question)
    {
        Fact[] facts = [.. Facts(digest)];
        string asked = question ?? "";
        List<(int From, int To)> named = [.. NamedBars(asked), .. NamedSections(asked, facts)];
        bool narrowest = named.Count > 0;
        if (!narrowest && Text(digest, "scope") == "selection" && Bars(digest) is { } selection)
        {
            named.Add(selection);
        }

        HashSet<string> kinds = [.. s_kinds.Where(entry => entry.Words.IsMatch(asked)).SelectMany(static entry => entry.Kinds)];
        var ranked = facts
            .Select(fact => (Fact: fact, Distance: Distance(fact, named), Kind: kinds.Contains(fact.Kind)))
            .Select(static near => (near.Fact, near.Distance, Group: near.Distance == 0 ? (near.Kind ? 0 : 1) : near.Kind ? 2 : 3))
            .OrderBy(static near => near.Group).ThenBy(static near => near.Distance).ThenBy(near => narrowest ? near.Fact.Width : 0).ThenBy(static near => near.Fact.Index);
        return [.. ranked.GroupBy(static near => near.Group).SelectMany(static group => KindsFirst([.. group.Select(static near => near.Fact)])).Select(static fact => (fact.Id, fact.Line))];
    }

    /// <summary>The first fact of each kind in order, then the others in order.</summary>
    private static IEnumerable<Fact> KindsFirst(IReadOnlyList<Fact> facts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Fact[] first = [.. facts.Where(fact => seen.Add(fact.Kind))];
        return first.Concat(facts.Except(first));
    }

    /// <summary>How many bars lie between the fact and the nearest named bars: 0 when they meet; a fact with no bars meets a passage's own bars.</summary>
    private static int Distance(Fact fact, List<(int From, int To)> named)
    {
        if (named.Count == 0)
        {
            return int.MaxValue;
        }

        return fact.Bars is not { } bars ? 0 : named.Min(range => Math.Max(0, Math.Max(range.From - bars.To, bars.From - range.To)));
    }

    /// <summary>The bars named in words: «такты 5–8», «такт 12 и 14», «bars 3 to 6», «m. 9».</summary>
    private static IEnumerable<(int From, int To)> NamedBars(string question) =>
        BarWords().Matches(question).SelectMany(static match => BarNumbers().Matches(match.Groups["list"].Value)).Select(static number =>
        {
            int from = int.Parse(number.Groups["from"].Value, CultureInfo.InvariantCulture);
            int to = number.Groups["to"].Success ? int.Parse(number.Groups["to"].Value, CultureInfo.InvariantCulture) : from;
            return (Math.Min(from, to), Math.Max(from, to));
        });

    /// <summary>The bars of the sections named by their letter: «часть B», «section A′» (an apostrophe for the prime).</summary>
    private static IEnumerable<(int From, int To)> NamedSections(string question, Fact[] facts) =>
        SectionWords().Matches(question)
            .Select(static match => match.Groups["label"].Value.ToUpperInvariant().Replace("''", "″", StringComparison.Ordinal).Replace('\'', '′'))
            .SelectMany(label => facts.Where(fact => fact.Label == label && fact.Bars is not null).Select(static fact => fact.Bars!.Value));

    private static IEnumerable<Fact> Facts(JsonElement digest) =>
        digest.ValueKind == JsonValueKind.Object && digest.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray()
                .Where(static item => Text(item, "id") is not null && Text(item, "text") is not null)
                .Select(static (item, index) => new Fact(index, Text(item, "id")!, Text(item, "text")!, Text(item, "kind") ?? "", Text(item, "label"), Bars(item)))
            : [];

    private static string? Text(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static (int From, int To)? Bars(JsonElement json) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty("bars", out JsonElement bars) && bars.ValueKind == JsonValueKind.Object
        && bars.TryGetProperty("from", out JsonElement from) && from.TryGetInt32(out int first)
        && bars.TryGetProperty("to", out JsonElement to) && to.TryGetInt32(out int last)
            ? (first, last)
            : null;

    private static Regex Stems(params string[] stems) =>
        new($@"\b(?:{string.Join("|", stems)})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    [GeneratedRegex(@"\b(?:такт\w*|bars?|measures?|m\.)\s*(?<list>\d{1,4}(?:\s*(?:-|–|—|to|по|до|,|и|and|or|или)\s*\d{1,4})*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex BarWords();

    [GeneratedRegex(@"(?<from>\d{1,4})(?:\s*(?:-|–|—|to|по|до)\s*(?<to>\d{1,4}))?", RegexOptions.CultureInvariant, 100)]
    private static partial Regex BarNumbers();

    [GeneratedRegex(@"\b(?:section|part|част\w*|раздел\w*)\s+(?<label>[A-Z](?:[′″‴]|'{1,2})?)(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex SectionWords();

    /// <summary>One fact of the digest as the template reads it: its place in the digest, id, line, kind, a section's letter and its bars.</summary>
    private sealed record Fact(int Index, string Id, string Line, string Kind, string? Label, (int From, int To)? Bars)
    {
        /// <summary>How many bars the fact spans; a fact with no bars is the widest.</summary>
        public int Width => Bars is { } bars ? bars.To - bars.From : int.MaxValue;
    }
}