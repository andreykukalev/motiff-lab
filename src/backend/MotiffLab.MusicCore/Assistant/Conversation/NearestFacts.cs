using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>
/// The facts of a digest nearest to the question (assistant option A, item 5), for the engine's template: deterministic,
/// from what the digest itself says. A fact is near when its bars meet the bars the question names, in words («такты 5–8»,
/// «bar 12») or by a section's capital letter («часть B», «section A′»), or, when it names none, the selected bars of a passage's
/// digest; and when its kind is one the question's words ask about («аккорды», «tempo», «как учить»), in either language.
/// Facts near both ways come first, then near by bars, then by kind, then the rest; within each, a section the question
/// names by its letter, then the nearest bars, then the narrowest (when the question names bars), then the digest's order,
/// one fact of each kind before a second of any.
/// </summary>
internal static partial class NearestFacts
{
    /// <summary>
    /// The kinds of fact a word of the question asks about: each word whole, its endings written out where a shorter word
    /// would read as another («часть», never «часто»; "part", never "particular").
    /// </summary>
    private static readonly (Regex Words, string[] Kinds)[] s_kinds =
    [
        (WholeWords(@"тональн\w*", @"лад(?:а|у|ом|е)?", @"минор\w*", @"мажор\w*", @"модуляци\w*", "keys?", "minor", "major", @"modulat\w*"), ["key"]),
        (WholeWords(@"обзор\w*", "целом", "overview", "overall", "summary"), ["summary"]),
        (WholeWords("форм(?:а|ы|у|ой|е)", "част(?:ь|и|ью|ей|ям|ях|ями)", @"раздел\w*", @"структур\w*", @"построен\w*", @"повтор\w*", @"возвращ\w*", @"реприз\w*", "forms?", "sections?", "parts?", "structure", @"repeat\w*", @"return\w*", "reprise"), ["form", "structure"]),
        (WholeWords(@"фраз\w*", @"дыхани\w*", "phrases?", @"breath\w*"), ["phrase"]),
        (WholeWords(@"предложени\w*", @"период\w*", "sentences?", "periods?"), ["sentence"]),
        (WholeWords(@"аккорд\w*", @"гармони\w*", @"ступен\w*", "chords?", @"harmon\w*", "degrees?"), ["chords", "chord", "harmony"]),
        (WholeWords(@"каденци\w*", @"каданс\w*", @"заверш\w*", "cadences?", "closes?"), ["cadence"]),
        (WholeWords(@"плотн\w*", @"трудн\w*", @"сложн\w*", @"кульминаци\w*", "dense", "densest", "busy", "busiest", "hard", "hardest", @"difficult\w*", "tricky", "climax"), ["density"]),
        (WholeWords("нов(?:ый|ое|ого|ая|ой|ую|ые|ых|ом)", @"материал\w*", "new", "material"), ["newMaterial"]),
        (WholeWords("рук(?:а|и|у|ой|ами|ах)?", @"диапазон\w*", @"регистр\w*", "лев(?:ой|ая|ую)", "прав(?:ой|ая|ую)", "hands?", "ranges?", "registers?", "left", "right"), ["range"]),
        (WholeWords("темп(?:а|е|у|ом|ы)?", @"скорост\w*", @"медлен\w*", @"быстр\w*", @"метроном\w*", "tempo", "bpm", "speed", @"slow\w*", @"fast\w*", @"metronom\w*"), ["tempo"]),
        (WholeWords(@"провер\w*", @"ошиб\w*", @"распозна\w*", @"check\w*", "errors?", "mistakes?", @"scan\w*"), ["check"]),
        (WholeWords(@"мелоди\w*", @"melod\w*", "tunes?"), ["melody"]),
        (WholeWords(@"ритм\w*", @"rhythm\w*"), ["rhythm"]),
        (WholeWords(@"фактур\w*", "textures?"), ["texture"]),
        (WholeWords(@"характер\w*", @"настроени\w*", "character", "mood"), ["character"]),
        (WholeWords("учить", "учи", @"разуч\w*", @"выуч\w*", @"занима\w*", @"упражн\w*", @"практи\w*", "начать", "learn", "learning", @"practi[cs]\w*", "study", "start"), ["form", "density", "newMaterial", "range", "tempo"]),
    ];

    /// <summary>The digest's facts (id and line), the nearest to <paramref name="question"/> first; every fact once.</summary>
    internal static IReadOnlyList<(string Id, string Line)> Of(JsonElement digest, string? question)
    {
        Fact[] facts = [.. Facts(digest)];
        string asked = question ?? "";
        string[] letters = [.. NamedSections(asked)];
        List<(int From, int To)> named = [.. NamedBars(asked), .. facts.Where(fact => Named(fact, letters) && fact.Bars is not null).Select(static fact => fact.Bars!.Value)];
        bool narrowest = named.Count > 0;
        if (!narrowest && Text(digest, "scope") == "selection" && Bars(digest) is { } selection)
        {
            named.Add(selection);
        }

        HashSet<string> kinds = [.. s_kinds.Where(entry => entry.Words.IsMatch(asked)).SelectMany(static entry => entry.Kinds)];
        var ranked = facts
            .Select(fact => (Fact: fact, Distance: Distance(fact, named), Kind: kinds.Contains(fact.Kind)))
            .Select(static near => (near.Fact, near.Distance, Group: near.Distance == 0 ? (near.Kind ? 0 : 1) : near.Kind ? 2 : 3))
            // The section named comes before a neighbour that shares a bar with it, as B and A′ share the bar where A′ starts.
            .OrderBy(static near => near.Group).ThenBy(near => Named(near.Fact, letters) ? 0 : 1).ThenBy(static near => near.Distance).ThenBy(near => narrowest ? near.Fact.Width : 0).ThenBy(static near => near.Fact.Index);
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

    /// <summary>Whether the fact is a section the question names by its letter.</summary>
    private static bool Named(Fact fact, string[] letters) => fact.Label is { } label && letters.Contains(label);

    /// <summary>The letters of the sections named: «часть B», «section A′» (an apostrophe for the prime).</summary>
    private static IEnumerable<string> NamedSections(string question) =>
        SectionWords().Matches(question)
            .Select(static match => match.Groups["label"].Value.Replace("''", "″", StringComparison.Ordinal).Replace('\'', '′'));

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

    /// <summary>Any of <paramref name="words"/>, each a whole word, in any case.</summary>
    private static Regex WholeWords(params string[] words) =>
        new($@"\b(?:{string.Join("|", words)})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    [GeneratedRegex(@"\b(?:такт\w*|bars?|measures?|m\.)\s*(?<list>\d{1,4}(?:\s*(?:-|–|—|to|по|до|,|и|and|or|или)\s*\d{1,4})*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex BarWords();

    [GeneratedRegex(@"(?<from>\d{1,4})(?:\s*(?:-|–|—|to|по|до)\s*(?<to>\d{1,4}))?", RegexOptions.CultureInvariant, 100)]
    private static partial Regex BarNumbers();

    [GeneratedRegex(@"\b(?:sections?|parts?|част(?:ь|и|ью)|раздел(?:а|е|ом)?)\s+(?<label>(?-i:[A-Z])(?:[′″‴]|'{1,2})?)(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex SectionWords();

    /// <summary>One fact of the digest as the template reads it: its place in the digest, id, line, kind, a section's letter and its bars.</summary>
    private sealed record Fact(int Index, string Id, string Line, string Kind, string? Label, (int From, int To)? Bars)
    {
        /// <summary>How many bars the fact spans; a fact with no bars is the widest.</summary>
        public int Width => Bars is { } bars ? bars.To - bars.From : int.MaxValue;
    }
}