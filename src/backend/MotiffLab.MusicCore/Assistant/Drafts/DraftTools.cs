using System.Text.Json;

using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

namespace MotiffLab.MusicCore.Assistant.Drafts;

/// <summary>
/// The tools of the assistant in a draft (decision record 0034, section 6): read the приёмы of the variant built from and
/// the bridge candidates at the seam before the place, and propose taking приёмы or choosing a bridge. A proposal is made
/// only by the draft's own operations (<see cref="DraftTakes.Take"/>, <see cref="DraftBridges.Put"/>,
/// <see cref="Draft.WithBase"/>), the same the player's buttons call; nothing is applied. Each refuses outside a draft.
/// </summary>
internal static class DraftTools
{
    internal static IEnumerable<AssistantTool> All()
    {
        yield return new AssistantTool(
            "place.techniques",
            ToolKind.Read,
            "The techniques (приёмы: differences of one kind and shape in several bars) of the variant built from against the place's base: each one's number, id to cite, kind, the place's bars it occurs in (from 1), how sure music-core is (per cent of the bar's notes found in place), and whether it is open, taken or left. A technique is inferred by the engine.",
            ToolSchema.Object(),
            static (piece, _) => InDraft(piece, static scope => ToolResult.Read(json => WriteTechniques(json, scope))));
        yield return new AssistantTool(
            "place.take",
            ToolKind.ProposeDraft,
            "Proposes taking techniques of the variant built from into the place, everywhere they are open or only at one bar of the place, as the player's «Взять везде» and «Только здесь» do. music-core writes the steps; the player hears before and after and accepts or refuses.",
            ToolSchema.Object(
                ("techniques", ToolSchema.List(ToolSchema.Integer(1, AssistantTools.MaxIndex), 1, AssistantTools.MaxEdits, unique: true), "The techniques' numbers, in the order to take them."),
                ("bar", ToolSchema.OrNull(ToolSchema.Integer(1, AssistantTools.MaxBarNumber)), "Only at this bar of the place (from 1), or null for everywhere they are open.")),
            static (piece, parameters) => InDraft(piece, scope => Take(scope, parameters)));
        yield return new AssistantTool(
            "place.bridges",
            ToolKind.Read,
            "The bridge candidates music-core builds at the seam before the place (only where the keys either side differ), or the candidates of a bridge place: each one's kind, id to cite and the chords it sounds. Their notes are inferred.",
            ToolSchema.Object(),
            static (piece, _) => InDraft(piece, static scope => Bridged(scope) is ({ } draft, { } place) ? ToolResult.Read(json => WriteBridges(json, draft.FindPlace(place)!, scope.Words)) : NoBridge(scope)));
        yield return new AssistantTool(
            "place.bridge",
            ToolKind.ProposeDraft,
            "Proposes one bridge candidate as a place between the two at the seam before the place, or as the base of a bridge place. music-core builds its notes; the player hears before and after and accepts or refuses.",
            ToolSchema.Object(("kind", ToolSchema.Choice<BridgeKind>(), "The candidate: dominant, commonChord or bassWalk.")),
            static (piece, parameters) => InDraft(piece, scope => Bridge(scope, ToolSchema.Read<BridgeKind>(parameters.GetProperty("kind")))));
    }

    private static ToolResult InDraft(AssistantPiece piece, Func<DraftScope, ToolResult> run) =>
        piece.Draft is { } scope ? run(scope) : ToolResult.Refused(piece.Words.T("This tool works only in a draft.", "Этот инструмент работает только в черновике."));

    private static void WriteTechniques(Utf8JsonWriter json, DraftScope scope)
    {
        PlaceTechnique[] techniques = [.. Of(scope)];
        json.WriteStartObject();
        json.WriteString("variant", scope.Variant is { } variant ? DraftLabels.Variant(variant, scope.Words) : null);
        json.WriteStartArray("techniques");
        foreach (PlaceTechnique technique in techniques)
        {
            json.WriteStartObject();
            json.WriteString("id", FactsDigest.TechniqueId(technique.Number));
            json.WriteNumber("number", technique.Number);
            json.WriteString("kind", ToolSchema.Camel(technique.Kind.ToString()));
            json.WriteString("label", DraftLabels.Technique(technique.Kind, scope.Words));
            json.WriteNumber("confidence", technique.Confidence);
            json.WriteBoolean("sure", technique.IsSure);
            json.WriteString("state", ToolSchema.Camel(technique.State.ToString()));
            json.WriteStartArray("occurrences");
            foreach (TechniqueOccurrence occurrence in technique.Occurrences)
            {
                json.WriteStartObject();
                json.WriteNumber("bar", occurrence.Bar);
                json.WriteNumber("matched", occurrence.Matched);
                json.WriteNumber("total", occurrence.Total);
                json.WriteString("state", ToolSchema.Camel(occurrence.State.ToString()));
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        Ids(json, techniques.Select(static technique => FactsDigest.TechniqueId(technique.Number)));
        json.WriteEndObject();
    }

    /// <summary>The приёмы of the variant built from; none without one.</summary>
    private static IEnumerable<PlaceTechnique> Of(DraftScope scope) =>
        scope.Draft.TechniquesOf(scope.Place.Id).Where(technique => technique.Variant == scope.Variant?.Id);

    /// <summary>The приёмы taken one after another, as one group when more than one, each step marked as the assistant's.</summary>
    private static ToolResult Take(DraftScope scope, JsonElement parameters)
    {
        Words w = scope.Words;
        int[] numbers = [.. parameters.GetProperty("techniques").EnumerateArray().Select(static number => number.GetInt32())];
        int? bar = parameters.GetProperty("bar") is { ValueKind: JsonValueKind.Number } only ? only.GetInt32() : null;
        PlaceTechnique[] techniques = [.. Of(scope)];
        foreach (int number in numbers)
        {
            if (techniques.FirstOrDefault(technique => technique.Number == number) is not { } technique)
            {
                return ToolResult.Refused(w.T($"The variant built from has no technique {number} in this place.", $"У варианта, из которого собирают, нет приёма {number} в этом месте."));
            }

            if (!technique.Open.Any(occurrence => bar is null || occurrence.Bar == bar))
            {
                return ToolResult.Refused(bar is null
                    ? w.T($"The technique {number} is decided everywhere.", $"Приём {number} уже решён везде.")
                    : w.T($"The technique {number} is not open at bar {bar}.", $"Приём {number} не открыт в такте {bar}."));
            }
        }

        string? group = numbers.Length > 1 ? Project.NextId("g_", scope.Draft.Steps.Select(static step => step.Group).OfType<string>()) : null;
        Draft after = numbers.Aggregate(scope.Draft, (draft, number) => DraftTakes.Take(draft, scope.Place.Id, number, bar, scope.At, group, DraftProposal.Origin));
        string place = DraftLabels.Circled(scope.Index + 1);
        string listed = string.Join(", ", numbers);
        string taken = string.Join("; ", numbers.Select(number => techniques.First(technique => technique.Number == number)).Select(technique =>
            w.T($"technique {technique.Number}, {DraftLabels.Technique(technique.Kind, w)}", $"приём {technique.Number}, {DraftLabels.Technique(technique.Kind, w)}")));
        string where = bar is { } at ? w.T($" at bar {at}", $" в такте {at}") : "";
        return ToolResult.Proposing(new DraftProposal(
            "place.take",
            scope.Place.Id,
            scope.Draft,
            after,
            numbers.Length == 1 ? w.T($"Take technique {listed}", $"Взять приём {listed}") : w.T($"Take techniques {listed}", $"Взять приёмы {listed}"),
            w.T($"Into {place}{where} from {DraftLabels.Variant(scope.Variant!, w)}: {taken}.", $"В {place}{where} из «{DraftLabels.Variant(scope.Variant!, w)}»: {taken}.")));
    }

    /// <summary>The draft with a bridge place before the place, and that place's id: the place itself when it is a bridge, else the candidates put; null where none is offered.</summary>
    private static (Draft Draft, string Place)? Bridged(DraftScope scope)
    {
        if (scope.Place.Bridge)
        {
            return (scope.Draft, scope.Place.Id);
        }

        try
        {
            Draft put = DraftBridges.Put(scope.Draft, scope.Place.Id, scope.At);
            return (put, put.Places[scope.Index].Id);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static ToolResult NoBridge(DraftScope scope) => ToolResult.Refused(scope.Words.T(
        "No bridge is offered before this place: the keys either side of its seam do not differ, or it is the first place.",
        "Связка перед этим местом не предлагается: тональности по обе стороны стыка одинаковы, или это первое место."));

    private static void WriteBridges(Utf8JsonWriter json, DraftPlace place, Words words)
    {
        json.WriteStartObject();
        json.WriteStartArray("candidates");
        foreach (DraftVariant variant in place.Variants)
        {
            BridgeKind kind = DraftBridges.KindOf(variant)!.Value;
            json.WriteStartObject();
            json.WriteString("id", BridgeId(kind));
            json.WriteString("kind", ToolSchema.Camel(kind.ToString()));
            json.WriteString("label", DraftBridges.Label(kind, words));
            json.WriteString("explanation", DraftBridges.Explanation(variant, words));
            json.WriteBoolean("base", variant.Id == place.Base);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        Ids(json, place.Variants.Select(static variant => BridgeId(DraftBridges.KindOf(variant)!.Value)));
        json.WriteEndObject();
    }

    /// <summary>The candidate of <paramref name="kind"/> as the base of the bridge place: put before the place, or chosen in it.</summary>
    private static ToolResult Bridge(DraftScope scope, BridgeKind kind)
    {
        Words w = scope.Words;
        if (Bridged(scope) is not ({ } draft, { } placeId))
        {
            return NoBridge(scope);
        }

        DraftPlace place = draft.FindPlace(placeId)!;
        if (place.Variants.FirstOrDefault(variant => DraftBridges.KindOf(variant) == kind) is not { } chosen)
        {
            return ToolResult.Refused(w.T($"There is no candidate «{DraftBridges.Label(kind, w)}» here.", $"Кандидата «{DraftBridges.Label(kind, w)}» здесь нет."));
        }

        if (scope.Place.Bridge && chosen.Id == place.Base)
        {
            return ToolResult.Refused(w.T($"«{DraftBridges.Label(kind, w)}» is the bridge's base already.", $"«{DraftBridges.Label(kind, w)}» уже основа связки."));
        }

        Draft after = chosen.Id == place.Base ? draft : draft.WithBase(placeId, chosen.Id);
        return ToolResult.Proposing(new DraftProposal(
            "place.bridge",
            placeId,
            scope.Draft,
            after,
            w.T($"Bridge: {DraftBridges.Label(kind, w)}", $"Связка: {DraftBridges.Label(kind, w)}"),
            DraftBridges.Explanation(chosen, w) ?? DraftBridges.Label(kind, w))
        { Structure = true });
    }

    private static string BridgeId(BridgeKind kind) => $"bridge.{ToolSchema.Camel(kind.ToString())}";

    private static void Ids(Utf8JsonWriter json, IEnumerable<string> ids)
    {
        json.WriteStartArray("ids");
        foreach (string id in ids)
        {
            json.WriteStringValue(id);
        }

        json.WriteEndArray();
    }
}