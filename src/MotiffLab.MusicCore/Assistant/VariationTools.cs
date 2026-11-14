using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Variations;

using static MotiffLab.MusicCore.Assistant.ToolArguments;

namespace MotiffLab.MusicCore.Assistant;

/// <summary>
/// The 14 variation operations as tools (0028 section 2), one tool each: their parameters and ranges are the operations'
/// own (the Keep table, <see cref="OperationRules"/>, each operation's validation), the result is a draft with the diff,
/// the Keep checks and the explanation written from the diff. The engine refuses with its own reason
/// (<see cref="IVariationOperation.DisabledReason(IReadOnlySet{KeepProperty}, Words)"/>, <see cref="IVariationOperation.UnavailableReason"/>).
/// </summary>
internal static class VariationTools
{
    private static ToolSchema Indices => ToolSchema.OrNull(ToolSchema.List(ToolSchema.Integer(0, AssistantTools.MaxIndex), 1, AssistantTools.MaxIndex + 1, unique: true));

    private const string IndicesText = "Which melody intervals (counted from 0) to change, or null for all of them.";

    internal static IEnumerable<AssistantTool> All() => [.. LineTools(), .. TextureTools()];

    /// <summary>The operations on rhythm, articulation, accompaniment and the line.</summary>
    private static IEnumerable<AssistantTool> LineTools()
    {
        yield return Variation(RhythmReshape.Instance, "rhythm", (p, _) => new RhythmReshapeParameters(ToolSchema.Read<RhythmReshapeKind>(p.GetProperty("kind")), p.GetProperty("eighths").GetInt32()),
            ("kind", ToolSchema.Choice<RhythmReshapeKind>(), "The rhythm change."),
            ("eighths", ToolSchema.Integer(RhythmReshapeParameters.MinEighths, RhythmReshapeParameters.MaxEighths), "For displace: how many eighths later the notes move; ignored by the other kinds."));
        yield return Variation(Articulation.Instance, "articulation", (p, _) => new ArticulationParameters(ToolSchema.Read<ArticulationKind>(p.GetProperty("kind"))),
            ("kind", ToolSchema.Choice<ArticulationKind>(), "The articulation."));
        yield return Variation(AccompanimentPattern.Instance, "accompaniment", (p, _) => new AccompanimentPatternParameters(ToolSchema.Read<AccompanimentPatternKind>(p.GetProperty("kind")), ToolSchema.Read<PatternUnit>(p.GetProperty("unit"))),
            ("kind", ToolSchema.Choice<AccompanimentPatternKind>(), "The accompaniment figure."),
            ("unit", ToolSchema.Choice<PatternUnit>(), "The note value the figure moves in."));
        yield return Variation(MelodicSequence.Instance, "sequence", (p, _) => new MelodicSequenceParameters(p.GetProperty("steps").GetInt32(), p.GetProperty("repeats").GetInt32(), p.GetProperty("diatonic").GetBoolean()),
            ("steps", ToolSchema.IntegerOf(-4, -3, -2, -1, 1, 2, 3, 4), "Scale steps (or semitones when not diatonic) each repeat moves; never 0."),
            ("repeats", ToolSchema.Integer(MelodicSequenceParameters.MinRepeats, MelodicSequenceParameters.MaxRepeats), "How many times the passage is repeated."),
            ("diatonic", ToolSchema.Boolean, "Count in steps of the key's scale (true) or in semitones (false)."));
        yield return Variation(IntervalReshape.Instance, "intervals", (p, _) => new IntervalReshapeParameters(p.GetProperty("steps").GetInt32(), IntervalsOf(p, "intervals")),
            ("steps", ToolSchema.IntegerOf(-2, -1, 1, 2), "Scale steps to widen (positive) or narrow (negative) the intervals by."),
            ("intervals", Indices, IndicesText));
        yield return Variation(ChangeDirection.Instance, "direction", (p, _) => new ChangeDirectionParameters(p.GetProperty("diatonic").GetBoolean(), IntervalsOf(p, "intervals")),
            ("diatonic", ToolSchema.Boolean, "Mirror in steps of the key's scale (true) or in semitones (false)."),
            ("intervals", Indices, IndicesText));
    }

    /// <summary>The operations on texture, register and harmony.</summary>
    private static IEnumerable<AssistantTool> TextureTools()
    {
        yield return Variation(DensityAndRests.Instance, "density", (p, _) => new DensityParameters(ToolSchema.Read<DensityKind>(p.GetProperty("kind")), ToolSchema.Read<PatternUnit>(p.GetProperty("unit"))),
            ("kind", ToolSchema.Choice<DensityKind>(), "Repeat notes, merge repeats, give more air or fill rests."),
            ("unit", ToolSchema.Choice(ToolSchema.Camel(nameof(PatternUnit.Eighths)), ToolSchema.Camel(nameof(PatternUnit.Sixteenths))), "The note value the step works in."));
        yield return Variation(BassPattern.Instance, "bass", (p, _) => new BassPatternParameters(ToolSchema.Read<BassPatternKind>(p.GetProperty("kind"))),
            ("kind", ToolSchema.Choice<BassPatternKind>(), "The bass figure."));
        yield return Variation(AccentShift.Instance, "accents", AccentsOf,
            ("pattern", ToolSchema.Choice<AccentPattern>(), "Where the accents fall; custom needs onsets."),
            ("onsets", Indices, "For custom: which onsets (counted from 0) are accented; null for the other patterns."));
        yield return Variation(VoicingAndRegister.Instance, "voicing", (p, _) => new VoicingParameters(ToolSchema.Read<VoicingKind>(p.GetProperty("kind"))),
            ("kind", ToolSchema.Choice<VoicingKind>(), "The voicing or register change."));
        yield return Variation(Simplify.Instance, "simplify", (p, _) => new SimplifyParameters(p.GetProperty("level").GetInt32()),
            ("level", ToolSchema.Integer(SimplifyParameters.MinLevel, SimplifyParameters.MaxLevel), "How far to simplify, 1 (least) to 3."));
        yield return Variation(PassingNotes.Instance, "passingNotes", (p, _) => new PassingNotesParameters(ToolSchema.Read<PassingNoteKind>(p.GetProperty("kind")), IntervalsOf(p, "intervals")),
            ("kind", ToolSchema.Choice<PassingNoteKind>(), "Passing notes or neighbour notes."),
            ("intervals", Indices, IndicesText));
        yield return Variation(Ornament.Instance, "ornament", (p, _) => new OrnamentParameters(ToolSchema.Read<OrnamentKind>(p.GetProperty("kind"))),
            ("kind", ToolSchema.Choice<OrnamentKind>(), "The ornament."));
        yield return Variation(Reharmonise.Instance, "reharmonise", (p, _) => new ReharmoniseParameters(ToolSchema.Read<ReharmoniseKind>(p.GetProperty("kind"))),
            ("kind", ToolSchema.Choice<ReharmoniseKind>(), "Move to the relative or the parallel key."));
    }

    /// <summary>A variation tool: the range, hand, scope, Keep choices and seed every operation takes, then its own parameters.</summary>
    private static AssistantTool Variation(
        IVariationOperation operation,
        string slug,
        Func<JsonElement, Words, OperationParameters> read,
        params (string Name, ToolSchema Schema, string Description)[] own)
    {
        KeepRow row = KeepTable.Find(operation.Id) ?? throw new InvalidOperationException($"Operation {operation.Id} has no Keep table row.");
        string[] scopes = [.. Enum.GetValues<OperationScope>().Where(operation.Supports).Select(scope => ToolSchema.Camel(scope.ToString()))];
        ToolSchema schema = ToolSchema.Object(
        [
            ("bars", BarsSchema, "The bars to vary, at most 32."),
            ("hand", HandSchema, "One hand's notes, or both."),
            ("scope", ToolSchema.Choice(scopes), "Which layer of the selected notes the step may change; the others stay exactly as they are."),
            ("keep", ToolSchema.List(ToolSchema.Choice<KeepProperty>(), 0, Enum.GetValues<KeepProperty>().Length, unique: true), "What must be kept; the engine refuses an operation the Keep choices rule out."),
            ("seed", ToolSchema.Integer(0, AssistantTools.MaxSeed), "Picks among equal choices; the same seed gives the same notes."),
            .. own,
        ]);
        string description = $"{row.Name} ({operation.Id}): {row.WhatItChanges} Makes a draft for the player to hear; nothing is applied.";
        return new AssistantTool($"variation.{slug}", ToolKind.ProposeDraft, description, schema, (piece, parameters) => RunVariation(piece, parameters, operation, $"variation.{slug}", read));
    }

    private static ToolResult RunVariation(AssistantPiece piece, JsonElement parameters, IVariationOperation operation, string name, Func<JsonElement, Words, OperationParameters> read)
    {
        if (RangeRefusal(piece, parameters) is { } range)
        {
            return ToolResult.Refused(range);
        }

        Words w = piece.Words;
        var keep = parameters.GetProperty("keep").EnumerateArray().Select(ToolSchema.Read<KeepProperty>).ToHashSet();
        if (operation.DisabledReason(keep, w) is { } disabled)
        {
            return ToolResult.Refused(disabled);
        }

        OperationParameters values = read(parameters, w) with { Scope = ToolSchema.Read<OperationScope>(parameters.GetProperty("scope")) };
        (int from, int to) = BarsOf(parameters);
        RangeReading reading = piece.Read(from, to, HandOf(parameters));
        IReadOnlyList<Note> notes = reading.NotesOf(piece.Notes);
        var context = new VariationContext(reading.Selection.Span, keep, parameters.GetProperty("seed").GetInt32(), reading.Motif.Key?.Key, piece.Meter, w);
        if (operation.UnavailableReason(notes, context, values) is { } unavailable)
        {
            return ToolResult.Refused(unavailable);
        }

        Variation variation = operation.Apply(notes, context, values);
        return ToolResult.Proposed([new AssistantDraft(name, notes, variation)]);
    }

    private static IntervalSet? IntervalsOf(JsonElement parameters, string name) =>
        parameters.GetProperty(name) is { ValueKind: JsonValueKind.Array } list ? new IntervalSet(list.EnumerateArray().Select(static index => index.GetInt32())) : null;

    private static AccentShiftParameters AccentsOf(JsonElement parameters, Words w)
    {
        AccentPattern pattern = ToolSchema.Read<AccentPattern>(parameters.GetProperty("pattern"));
        IntervalSet? onsets = IntervalsOf(parameters, "onsets");
        return (pattern == AccentPattern.Custom) == (onsets is not null)
            ? new AccentShiftParameters(pattern, onsets)
            : throw new InvalidOperationException(w.T(
                "A custom accent pattern needs \"onsets\"; the other patterns take null.",
                "Своему рисунку акцентов нужны «onsets»; остальным рисункам — null."));
    }
}