using System.Text;

using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>One fact as the digest carries it: its stable id, how sure it is, and its sentence.</summary>
public sealed record DigestFact(string Id, FactCertainty Certainty, string Text);

/// <summary>
/// The compact list of engine facts about a motif or a variation, with stable ids and the engine
/// version (S1-AI0, S1-AI1, decision record 0003). It is the only thing AI may see, and its
/// templates are the explanation when AI is off (<see cref="FactTemplates"/>).
/// </summary>
/// <remarks>
/// <para>
/// The digest is built from fact lists only, so it cannot hold the file, its name, title or
/// composer, or the piece's note list: just the facts the analysers state about the selection.
/// A fact's basis stays on the device; its certainty travels with it.
/// </para>
/// <para>
/// Building a digest is pure: the same facts give the same digest, and the same notes, analysis
/// settings and <see cref="EngineVersion"/> give the same ids (decision record 0009), so a notebook
/// entry, an observation or an AI answer can cite them later.
/// </para>
/// </remarks>
/// <param name="Subject">What the facts describe: <see cref="MotifSubject"/> or <see cref="VariationSubject"/>.</param>
/// <param name="Facts">Grouped by lens in <see cref="FactLens"/> order, then variation facts; within a group, in the analysers' order.</param>
public sealed record FactDigest(string EngineVersion, string Subject, IReadOnlyList<DigestFact> Facts)
{
    /// <summary>The analysis engine's version. Raise it whenever a fact id or the meaning of a fact changes.</summary>
    public const string CurrentEngineVersion = "3";

    public const string MotifSubject = "motif";

    public const string VariationSubject = "variation";

    public const string OperationId = "variation.operation";

    public const string ChangesId = "variation.changes";

    public const string KeepId = "variation.keep";

    /// <summary>The digest of a motif from the facts of any analysers, for example melody, rhythm and key, structure and character.</summary>
    /// <exception cref="ArgumentException">Two facts share an id.</exception>
    public static FactDigest ForMotif(IEnumerable<MotifFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return Create(MotifSubject, MotifFacts(facts));
    }

    /// <summary>The digest of a variation: the facts of the motif it varies, then what the step changed and kept.</summary>
    /// <param name="motifFacts">The facts of the motif before the step.</param>
    /// <exception cref="ArgumentException">Two facts share an id.</exception>
    public static FactDigest ForVariation(IEnumerable<MotifFact> motifFacts, Variation variation)
    {
        ArgumentNullException.ThrowIfNull(motifFacts);
        ArgumentNullException.ThrowIfNull(variation);

        return Create(VariationSubject, [.. MotifFacts(motifFacts), .. VariationFacts(variation)]);
    }

    /// <summary>True when the digest holds a fact with this id, so an explanation may cite it.</summary>
    public bool Contains(string id) => Facts.Any(fact => fact.Id == id);

    /// <summary>The fact with this id, or null.</summary>
    public DigestFact? Find(string id) => Facts.FirstOrDefault(fact => fact.Id == id);

    /// <summary>The digest as plain text, one fact a line, for the "What is sent" panel and snapshot tests.</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        text.Append("engine ").Append(EngineVersion).Append('\n');
        text.Append("subject ").Append(Subject).Append('\n');
        foreach (DigestFact fact in Facts)
        {
            text.Append(fact.Id).Append(" [").Append(CertaintyName(fact.Certainty)).Append("] ").Append(fact.Text).Append('\n');
        }

        return text.ToString();
    }

    private static FactDigest Create(string subject, List<DigestFact> facts)
    {
        string? duplicate = facts.GroupBy(fact => fact.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            throw new ArgumentException($"Two facts share the id \"{duplicate}\".", nameof(facts));
        }

        return new FactDigest(CurrentEngineVersion, subject, facts);
    }

    private static List<DigestFact> MotifFacts(IEnumerable<MotifFact> facts) =>
        [.. facts.OrderBy(fact => fact.Lens).Select(fact => new DigestFact(fact.Id, fact.Certainty, fact.Text))];

    private static List<DigestFact> VariationFacts(Variation variation)
    {
        var kept = new List<string>();
        var lost = new List<string>();
        var notChecked = new List<string>();
        foreach (KeepCheck check in variation.Checks)
        {
            (check.Status switch
            {
                KeepStatus.Kept => kept,
                KeepStatus.NoLongerKept => lost,
                _ => notChecked,
            }).Add(PropertyName(check.Property));
        }

        var keep = new List<string>();
        if (kept.Count > 0)
        {
            keep.Add($"Kept: {string.Join(", ", kept)}.");
        }

        if (lost.Count > 0)
        {
            keep.Add($"No longer kept: {string.Join(", ", lost)}.");
        }

        if (notChecked.Count > 0)
        {
            keep.Add($"Not checked yet: {string.Join(", ", notChecked)}.");
        }

        return
        [
            new DigestFact(OperationId, FactCertainty.Detected, $"Operation: {variation.Operation}."),
            new DigestFact(ChangesId, FactCertainty.Detected, variation.Explanation),
            new DigestFact(KeepId, FactCertainty.Detected, keep.Count == 0 ? "No property was asked to be kept." : string.Join(" ", keep)),
        ];
    }

    /// <summary>"pitch order", as the Keep table names it.</summary>
    private static string PropertyName(KeepProperty property) => property switch
    {
        KeepProperty.PitchOrder => "pitch order",
        _ => property.ToString().ToLowerInvariant(),
    };

    private static string CertaintyName(FactCertainty certainty) => certainty switch
    {
        FactCertainty.Detected => "detected",
        FactCertainty.Inferred => "inferred",
        FactCertainty.Interpretation => "interpretation",
        _ => "uncertain",
    };
}