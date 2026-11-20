using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MotiffLab.MusicCore.Assistant.Conversation;

/// <summary>A piece of an answer: text, and the fact id it cites when it is a citation.</summary>
/// <param name="Cites">The fact id; null for plain text.</param>
public sealed record AnswerPart(string Text, string? Cites);

/// <summary>An answer as checked: its parts, whether it is the engine's template instead of the model's words, and why.</summary>
public sealed record CheckedAnswer(IReadOnlyList<AnswerPart> Parts, bool FellBack, string? Reason)
{
    /// <summary>The answer as text, each citation written back as [[id]].</summary>
    public string Text => string.Concat(Parts.Select(static part => part.Cites is { } id ? $"[[{id}]]" : part.Text));
}

/// <summary>
/// The check every answer of the model passes before the player sees it (0003 section 5; 0028 section 3; CLAUDE.md: AI
/// output cites fact ids from the digest, else template text): the answer cites its facts as [[id]]; it must cite at least
/// one, every id it cites must be one the turn gave it (the digest's or a tool result's, the same rule as
/// <see cref="FactsDigest.Unknown"/>), and it must be no longer than <see cref="MaxLength"/>. An answer that fails is
/// replaced by the engine's template: the digest's facts nearest to the question (<see cref="NearestFacts"/>) in the
/// engine's own words, each cited. Pure; the server and the device check the same way.
/// </summary>
public static partial class AnswerCheck
{
    /// <summary>The longest answer, in characters.</summary>
    public const int MaxLength = 2000;

    /// <summary>How many facts the template lists.</summary>
    public const int TemplateFacts = 4;

    // Why an answer is the template (CheckedAnswer.Reason, the reply's "reason"): first the check's own reasons ...

    /// <summary>The answer has no text: the model declined, or was cut off by its token limit.</summary>
    public const string Empty = "empty";

    /// <summary>The answer is longer than <see cref="MaxLength"/>.</summary>
    public const string TooLong = "tooLong";

    /// <summary>The answer cites no fact.</summary>
    public const string NoCitation = "noCitation";

    /// <summary>The answer cites an id the turn did not give.</summary>
    public const string UnknownFact = "unknownFact";

    // ... then the relay's: the model gave no answer it may pass on.

    /// <summary>The model did not answer within the turn's time.</summary>
    public const string Timeout = "timeout";

    /// <summary>The model's provider failed the turn.</summary>
    public const string ModelError = "modelError";

    /// <summary>The model called a tool its role lacks.</summary>
    public const string ToolNotAllowed = "toolNotAllowed";

    /// <summary>The model called a tool with arguments its schema refuses.</summary>
    public const string BadArguments = "badArguments";

    /// <summary>The model called more tools than a question may take.</summary>
    public const string TooManyCalls = "tooManyCalls";

    /// <summary>The ids an answer cites, in order, each once.</summary>
    public static IReadOnlyList<string> Cited(string text) =>
        [.. CitationPattern().Matches(text ?? "").Select(static match => match.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    /// <summary>The ids a turn gives the model: every string of an "ids" list in the context and in the tool results.</summary>
    public static IReadOnlySet<string> Known(JsonElement context, IEnumerable<ToolOutcome> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var known = new HashSet<string>(StringComparer.Ordinal);
        Collect(context, known);
        foreach (ToolOutcome result in results)
        {
            Collect(result.Content, known);
        }

        return known;
    }

    /// <summary>Whether any of <paramref name="results"/> offers a draft music-core made.</summary>
    public static bool Drafted(IEnumerable<ToolOutcome> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return results.Any(static result => result.Content.ValueKind == JsonValueKind.Object
            && result.Content.TryGetProperty("drafts", out JsonElement drafts) && drafts.ValueKind == JsonValueKind.Array && drafts.GetArrayLength() > 0);
    }

    /// <summary>
    /// The answer as the player may see it: the model's words when they pass, else the template, with the reason. With
    /// <paramref name="drafted"/> (a tool made a draft for this question), an answer may cite no fact: it says what the
    /// player asked for, and what changed is the draft's explanation, written by the engine; a fact it cites must still be known.
    /// The template answers <paramref name="question"/>, the player's last question.
    /// </summary>
    public static CheckedAnswer Check(string? text, IReadOnlySet<string> known, JsonElement context, Words words, bool drafted = false, string? question = null)
    {
        ArgumentNullException.ThrowIfNull(known);
        string answer = (text ?? "").Trim();
        IReadOnlyList<string> cited = Cited(answer);
        string? reason = answer.Length == 0 ? Empty
            : answer.Length > MaxLength ? TooLong
            : cited.Count == 0 && !drafted ? NoCitation
            : cited.Any(id => !known.Contains(id)) ? UnknownFact
            : null;
        return reason is null ? new CheckedAnswer(Parts(answer), false, null) : Template(context, words, reason, question);
    }

    /// <summary>The engine's template: a line that says the answer could not be checked, then the digest's facts nearest to <paramref name="question"/>, each cited.</summary>
    public static CheckedAnswer Template(JsonElement context, Words words, string reason, string? question = null)
    {
        var text = new StringBuilder(words.T(
            "The assistant's answer could not be checked against the engine's facts, so here is what the engine found:",
            "Ответ ассистента не удалось сверить с фактами движка, поэтому вот что нашёл движок:"));
        JsonElement digest = context.ValueKind == JsonValueKind.Object && context.TryGetProperty("digest", out JsonElement found) ? found : default;
        foreach ((string id, string line) in NearestFacts.Of(digest, question).Take(TemplateFacts))
        {
            text.Append('\n').Append("· ").Append(line).Append(" [[").Append(id).Append("]]");
        }

        return new CheckedAnswer(Parts(text.ToString()), true, reason);
    }

    /// <summary>The answer cut into text and citations.</summary>
    public static IReadOnlyList<AnswerPart> Parts(string text)
    {
        var parts = new List<AnswerPart>();
        int at = 0;
        foreach (Match match in CitationPattern().Matches(text ?? ""))
        {
            if (match.Index > at)
            {
                parts.Add(new AnswerPart(text![at..match.Index], null));
            }

            parts.Add(new AnswerPart(match.Value, match.Groups[1].Value));
            at = match.Index + match.Length;
        }

        if (at < (text?.Length ?? 0))
        {
            parts.Add(new AnswerPart(text![at..], null));
        }

        return parts;
    }

    private static void Collect(JsonElement json, HashSet<string> known)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in json.EnumerateObject())
            {
                if (property.Name == "ids" && property.Value.ValueKind == JsonValueKind.Array)
                {
                    known.UnionWith(property.Value.EnumerateArray().Where(static id => id.ValueKind == JsonValueKind.String).Select(static id => id.GetString()!));
                }
                else
                {
                    Collect(property.Value, known);
                }
            }
        }
        else if (json.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in json.EnumerateArray())
            {
                Collect(item, known);
            }
        }
    }

    [GeneratedRegex(@"\[\[([^\[\]\s]{1,80})\]\]", RegexOptions.CultureInvariant, 100)]
    private static partial Regex CitationPattern();
}