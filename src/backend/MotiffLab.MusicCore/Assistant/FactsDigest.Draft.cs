using System.Globalization;

using MotiffLab.MusicCore.Assistant.Drafts;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>The digest of a draft's place (decision record 0034, section 6; the plan's answer 7).</summary>
public sealed partial class FactsDigest
{
    /// <summary>
    /// The digest of the place a question in a draft is about: the place and where its base and the variant built from came
    /// from (what kind of Notes and their bars, never a file, a name or a title), the приёмы of that variant against the base,
    /// and the seams either side of the place. Its bars are the place's, from 1, as the приёмы name them.
    /// </summary>
    public static FactsDigest OfDraftPlace(DraftScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        AssistantPiece piece = scope.Base;
        Words w = scope.Words;
        var header = new List<(string, string)> { ("meter", Meters(piece.Meter, piece.FirstBar, piece.LastBar)) };
        if (KeySignature.InForce(piece.Piece.Keys, Tick.Zero) is { } key)
        {
            header.Add(("key", DraftLabels.Key(key, w)));
        }

        List<DigestItem> items =
        [
            new("place", "place", "imported", $"{DraftLabels.Place(scope.Place, scope.Index, w)}: {Bars(piece.FirstBar, piece.LastBar, w)}") { Bars = (piece.FirstBar, piece.LastBar) },
            Source("source.base", scope.Place.BaseVariant, w),
        ];
        if (scope.Variant is { } variant)
        {
            items.Add(Source("source.variant", variant, w));
            items.AddRange(scope.Draft.TechniquesOf(scope.Place.Id).Where(technique => technique.Variant == variant.Id).Select(technique => TechniqueItem(technique, w)));
        }

        foreach (DraftSeam seam in DraftSeams.Of(scope.Draft))
        {
            if (seam.After.Place == scope.Place.Id || seam.Before.Place == scope.Place.Id)
            {
                items.Add(SeamItem(seam, seam.After.Place == scope.Place.Id ? "seam.before" : "seam.after", w));
            }
        }

        return new FactsDigest("draftPlace", piece.FirstBar, piece.LastBar, null, items, header);
    }

    /// <summary>The id of the приём <paramref name="number"/> in a place's digest, as the draft's tools cite it.</summary>
    internal static string TechniqueId(int number) => string.Create(CultureInfo.InvariantCulture, $"technique.{number}");

    private static DigestItem Source(string id, DraftVariant variant, Words w) => new(
        id,
        "source",
        DraftLabels.Kind(variant) switch
        {
            DraftSourceKind.Recording => "detected",
            DraftSourceKind.Bridge => "inferred",
            _ => "imported",
        },
        DraftLabels.Variant(variant, w));

    /// <summary>«Приём 2: октава, такты 2, 6 · 100 % · уверенный · открыт».</summary>
    private static DigestItem TechniqueItem(PlaceTechnique technique, Words w)
    {
        int[] bars = [.. technique.Occurrences.Select(static occurrence => occurrence.Bar)];
        string where = bars.Length == 1 ? w.T($"bar {bars[0]}", $"такт {bars[0]}") : w.T($"bars {string.Join(", ", bars)}", $"такты {string.Join(", ", bars)}");
        string sure = technique.IsSure ? w.T("sure", "уверенный") : w.T("not sure", "неуверенный");
        string state = technique.State switch
        {
            TechniqueState.Taken => w.T("taken", "взят"),
            TechniqueState.Left => w.T("left", "оставлен"),
            _ => w.T("open", "открыт"),
        };
        return new DigestItem(
            TechniqueId(technique.Number),
            "technique",
            "inferred",
            string.Create(CultureInfo.InvariantCulture, $"{w.T("Technique", "Приём")} {technique.Number}: {DraftLabels.Technique(technique.Kind, w)}, {where} · {technique.Confidence} % · {sure} · {state}"))
        { Bars = (bars.Min(), bars.Max()) };
    }

    /// <summary>«Стык ①–②: ля минор → си минор; 90 → 72 в минуту; ② на октаву выше; Связка · 3 кандидата».</summary>
    private static DigestItem SeamItem(DraftSeam seam, string id, Words w)
    {
        List<string> parts = [];
        if (seam.KeyChanges)
        {
            parts.Add($"{DraftLabels.Key(seam.Before.Key!, w)} → {DraftLabels.Key(seam.After.Key!, w)}");
        }

        if (seam.MeterChanges)
        {
            parts.Add($"{seam.Before.Meter} → {seam.After.Meter}");
        }

        if (seam.TempoChanges)
        {
            parts.Add(w.T($"{seam.Before.PerMinute} → {seam.After.PerMinute} a minute", $"{seam.Before.PerMinute} → {seam.After.PerMinute} в минуту"));
        }

        if (seam.Register != 0)
        {
            parts.Add(DraftLabels.Octave(seam.After.Index, seam.Register > 0, w));
        }

        if (DraftBridges.Of(seam).Count is > 0 and int candidates)
        {
            parts.Add(DraftLabels.Bridge(candidates, w));
        }

        if (seam.Left)
        {
            parts.Add(w.T("kept as it is", "оставлен как есть"));
        }

        string between = $"{DraftLabels.Circled(seam.Before.Index + 1)}–{DraftLabels.Circled(seam.After.Index + 1)}";
        string what = parts.Count > 0 ? string.Join("; ", parts) : w.T("nothing differs", "ничего не отличается");
        return new DigestItem(id, "seam", "inferred", w.T($"Seam {between}: {what}", $"Стык {between}: {what}"));
    }
}