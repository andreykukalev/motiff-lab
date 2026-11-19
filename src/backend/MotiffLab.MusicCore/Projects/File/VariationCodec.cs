using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

using static MotiffLab.MusicCore.Projects.File.EditsCodec;
using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.SpanCodec;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// The motifs and their variations in the project file: each variation a chain of steps with the pending one and the
/// Keep choices (version 2), every catalogue operation's parameters and scope (version 4), and the key the steps read.
/// </summary>
internal static class VariationCodec
{
    internal static void WriteMotifsAndVariations(Utf8JsonWriter json, Project project)
    {
        List<UnknownEntity> unknownMotifs = KeptUnknown(project.Unknown, "motifs", project.Motifs.Select(static motif => motif.Id), null, out HashSet<string> replacedMotifs);
        WriteList(json, "motifs", project.Motifs, unknownMotifs, motif =>
        {
            json.WriteStartObject();
            json.WriteString("id", motif.Id);
            json.WriteString("name", motif.Name);
            json.WriteString("kind", motif.Kind.ToString());
            json.WriteString("mode", motif.Mode.ToString());
            WriteRange(json, motif.Span, motif.Filter);
            WriteNoteIds(json, motif.NoteIds);
            if (motif.Notes is { } notes)
            {
                json.WriteString("notes", notes);
            }

            json.WriteEndObject();
        });

        WriteList(json, "variations", project.Variations, KeptUnknown(project.Unknown, "variations", project.Variations.Select(static variation => variation.Id), replacedMotifs, out _), variation =>
        {
            json.WriteStartObject();
            json.WriteString("id", variation.Id);
            json.WriteString("motifId", variation.MotifId);
            WriteChain(json, variation.Steps, variation.Pending, variation.Keep);
            WriteKey(json, variation.Key);
            json.WriteEndObject();
        });
    }

    /// <summary>
    /// The motifs and their variations. A motif and its variations are kept or left unknown together: a variation cannot
    /// open without its motif, and an app that replaces a motif drops its variations, which it cannot do with one it never
    /// sees.
    /// </summary>
    internal static List<SavedMotif> ReadMotifs(JsonElement root, int version, Unknowns unknowns, out List<SavedVariation> variations)
    {
        var motifs = ReadEntries(Property(root, "motifs", JsonValueKind.Array), "motifs", ReadMotif, unknowns);
        var read = ReadEntries(Property(root, "variations", JsonValueKind.Array), "variations", variation => ReadVariation(variation, version), unknowns, "motifId");
        HashSet<string> unknown = [.. Project.UnknownIds(unknowns.Kept, "motifs"), .. unknowns.Kept.Where(static entity => entity.List == "variations" && entity.Owner is not null).Select(static entity => entity.Owner!)];
        variations = Known(read, "variations", static variation => variation.MotifId, unknown, "motifId", unknowns);
        return Known(motifs, "motifs", static motif => motif.Id, unknown, null, unknowns);
    }

    private static SavedMotif ReadMotif(JsonElement motif)
    {
        Expect(motif, JsonValueKind.Object, "motifs");
        (TickSpan span, SelectionFilter filter) = ReadRange(motif, "motif");
        IReadOnlyList<string> noteIds = ReadNoteIds(motif, "motif notes");
        string name = Text(motif, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw Damaged("motif name");
        }

        return new SavedMotif(
            Text(motif, "id"),
            name,
            Name<MotifSelectionKind>(motif, "kind"),
            Name<MotifMode>(motif, "mode"),
            span,
            filter,
            noteIds)
        {
            Notes = motif.TryGetProperty("notes", out _) ? Text(motif, "notes") : null,
        };
    }

    /// <summary>A chain's steps, the pending one and the Keep choices, as a variation and a kept variation save them.</summary>
    internal static void WriteChain(Utf8JsonWriter json, IReadOnlyList<SavedStep> steps, SavedStep? pending, IReadOnlyList<KeepProperty> keep)
    {
        json.WriteStartArray("steps");
        foreach (SavedStep step in steps)
        {
            json.WriteStartObject();
            WriteStep(json, step);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        if (pending is not null)
        {
            json.WriteStartObject("pending");
            WriteStep(json, pending);
            json.WriteEndObject();
        }

        json.WriteStartArray("keep");
        foreach (KeepProperty property in keep)
        {
            json.WriteStringValue(property.ToString());
        }

        json.WriteEndArray();
    }

    private static List<KeepProperty> ReadKeep(JsonElement parent) =>
        Property(parent, "keep", JsonValueKind.Array)
            .EnumerateArray()
            .Select(property => property.ValueKind == JsonValueKind.String && TryName(property.GetString(), out KeepProperty value) ? value : throw Damaged("Keep choices"))
            .Distinct()
            .Order()
            .ToList();

    internal static KeptVariation ReadKeptVariation(JsonElement variation)
    {
        (TickSpan span, SelectionFilter filter) = ReadRange(variation, "kept variation");
        List<string> noteIds = ReadNoteIds(variation, "kept variation notes");
        if (noteIds.Count == 0)
        {
            throw Damaged("kept variation notes");
        }

        List<SavedStep> steps = Property(variation, "steps", JsonValueKind.Array).EnumerateArray().Select(ReadStep).ToList();
        SavedStep? pending = variation.TryGetProperty("pending", out JsonElement step) ? ReadStep(step) : null;
        if (steps.Count == 0 && pending is null)
        {
            throw Damaged("kept variation steps");
        }

        return new KeptVariation(new SavedPassage(span, filter), noteIds, steps, pending, ReadKeep(variation), ReadKey(variation, "kept variation key"));
    }

    /// <summary>The key a chain's steps read, as an optional "key" object with "fifths" and "mode".</summary>
    internal static void WriteKey(Utf8JsonWriter json, MusicalKey? key)
    {
        if (key is { } known)
        {
            json.WriteStartObject("key");
            json.WriteNumber("fifths", known.Fifths);
            json.WriteString("mode", known.Mode.ToString());
            json.WriteEndObject();
        }
    }

    private static MusicalKey? ReadKey(JsonElement variation, string what)
    {
        if (!variation.TryGetProperty("key", out _))
        {
            return null;
        }

        JsonElement reading = Property(variation, "key", JsonValueKind.Object);
        int fifths = Integer(reading, "fifths");
        return fifths is >= -7 and <= 7 ? new MusicalKey(fifths, Name<KeyMode>(reading, "mode")) : throw Damaged(what);
    }

    private static SavedVariation ReadVariation(JsonElement variation, int version)
    {
        Expect(variation, JsonValueKind.Object, "variations");
        List<KeepProperty> keep = ReadKeep(variation);

        // Version 1 saved one step on the variation itself.
        List<SavedStep> steps = version == 1
            ? [ReadStep(variation)]
            : Property(variation, "steps", JsonValueKind.Array).EnumerateArray().Select(ReadStep).ToList();
        SavedStep? pending = version > 1 && variation.TryGetProperty("pending", out JsonElement step) ? ReadStep(step) : null;
        return new SavedVariation(
            Text(variation, "id"),
            Text(variation, "motifId"),
            steps,
            pending,
            keep)
        {
            Key = ReadKey(variation, "variation key"),
        };
    }

    /// <summary>One variation step as the file writes it; the facade (0020) uses the same shape.</summary>
    internal static void WriteStep(Utf8JsonWriter json, SavedStep step)
    {
        json.WriteString("operation", step.Operation);
        json.WriteStartObject("parameters");
        switch (step.Parameters)
        {
            case RhythmReshapeParameters reshape:
                json.WriteString("kind", reshape.Kind.ToString());
                json.WriteNumber("eighths", reshape.Eighths);
                break;
            case ArticulationParameters articulation:
                json.WriteString("kind", articulation.Kind.ToString());
                break;
            case AccompanimentPatternParameters pattern:
                json.WriteString("kind", pattern.Kind.ToString());
                json.WriteString("unit", pattern.Unit.ToString());
                break;
            case MelodicSequenceParameters sequence:
                json.WriteNumber("steps", sequence.Steps);
                json.WriteNumber("repeats", sequence.Repeats);
                json.WriteBoolean("diatonic", sequence.Diatonic);
                break;
            case IntervalReshapeParameters reshape:
                json.WriteNumber("steps", reshape.Steps);
                WriteIntervals(json, reshape.Intervals);
                break;
            case ChangeDirectionParameters direction:
                json.WriteBoolean("diatonic", direction.Diatonic);
                WriteIntervals(json, direction.Intervals);
                break;
            case DensityParameters density:
                json.WriteString("kind", density.Kind.ToString());
                json.WriteString("unit", density.Unit.ToString());
                break;
            case BassPatternParameters bass:
                json.WriteString("kind", bass.Kind.ToString());
                break;
            case AccentShiftParameters accents:
                json.WriteString("pattern", accents.Pattern.ToString());
                if (accents.Onsets is { } onsets)
                {
                    json.WriteStartArray("onsets");
                    foreach (int index in onsets.Indices)
                    {
                        json.WriteNumberValue(index);
                    }

                    json.WriteEndArray();
                }

                break;
            case VoicingParameters voicing:
                json.WriteString("kind", voicing.Kind.ToString());
                break;
            case SimplifyParameters simplify:
                json.WriteNumber("level", simplify.Level);
                break;
            case PassingNotesParameters passing:
                json.WriteString("kind", passing.Kind.ToString());
                WriteIntervals(json, passing.Intervals);
                break;
            case OrnamentParameters ornament:
                json.WriteString("kind", ornament.Kind.ToString());
                break;
            case ReharmoniseParameters reharmonise:
                json.WriteString("kind", reharmonise.Kind.ToString());
                break;
            case HandEditParameters edit:
                json.WriteString("kind", edit.Kind.ToString());
                json.WriteString("noteId", edit.NoteId);
                if (edit.Values is { } values)
                {
                    WriteValues(json, "values", values);
                }

                break;
            default:
                throw new ArgumentException($"Operation {step.Operation} has parameters the project file cannot write.", nameof(step));
        }

        if (step.Parameters.Scope != OperationScope.All)
        {
            json.WriteString("scope", step.Parameters.Scope.ToString());
        }

        json.WriteEndObject();
    }

    /// <summary>The picked intervals, numbered from 0; nothing when every interval is picked.</summary>
    private static void WriteIntervals(Utf8JsonWriter json, IntervalSet? intervals)
    {
        if (intervals is null)
        {
            return;
        }

        json.WriteStartArray("intervals");
        foreach (int index in intervals.Indices)
        {
            json.WriteNumberValue(index);
        }

        json.WriteEndArray();
    }

    /// <summary>One variation step as the file reads it: an object with "operation" and "parameters".</summary>
    internal static SavedStep ReadStep(JsonElement step)
    {
        Expect(step, JsonValueKind.Object, "variation steps");
        string operation = Text(step, "operation");
        JsonElement parameters = Property(step, "parameters", JsonValueKind.Object);
        OperationParameters read = operation switch
        {
            HandEdit.OperationId => ReadHandEdit(parameters),
            RhythmReshape.OperationId => ReadRhythmReshape(parameters),
            Articulation.OperationId => new ArticulationParameters(Name<ArticulationKind>(parameters, "kind")),
            AccompanimentPattern.OperationId => ReadAccompanimentPattern(parameters),
            MelodicSequence.OperationId => ReadMelodicSequence(parameters),
            IntervalReshape.OperationId => ReadIntervalReshape(parameters),
            ChangeDirection.OperationId => new ChangeDirectionParameters(Boolean(parameters, "diatonic"), ReadIntervals(parameters)),
            DensityAndRests.OperationId => ReadDensity(parameters),
            BassPattern.OperationId => new BassPatternParameters(Name<BassPatternKind>(parameters, "kind")),
            Ornament.OperationId => new OrnamentParameters(Name<OrnamentKind>(parameters, "kind")),
            AccentShift.OperationId => ReadAccents(parameters),
            VoicingAndRegister.OperationId => new VoicingParameters(Name<VoicingKind>(parameters, "kind")),
            Simplify.OperationId => Integer(parameters, "level") is int level and >= SimplifyParameters.MinLevel and <= SimplifyParameters.MaxLevel
                ? new SimplifyParameters(level)
                : throw Damaged("variation parameters"),
            PassingNotes.OperationId => new PassingNotesParameters(Name<PassingNoteKind>(parameters, "kind"), ReadIntervals(parameters)),
            Reharmonise.OperationId => new ReharmoniseParameters(Name<ReharmoniseKind>(parameters, "kind")),
            _ => throw new ProjectFileException($"A variation uses an operation ({operation}) this version of Motiff Lab does not have."),
        };
        OperationScope scope = parameters.TryGetProperty("scope", out _) ? Name<OperationScope>(parameters, "scope") : OperationScope.All;
        return new SavedStep(operation, read with { Scope = scope });
    }

    private static HandEditParameters ReadHandEdit(JsonElement parameters)
    {
        HandEditKind kind = Name<HandEditKind>(parameters, "kind");
        NoteValues? values = kind == HandEditKind.Remove ? null : ReadValues(Property(parameters, "values", JsonValueKind.Object), "variation steps");
        return new HandEditParameters(kind, Text(parameters, "noteId"), values);
    }

    private static AccentShiftParameters ReadAccents(JsonElement parameters)
    {
        AccentPattern pattern = Name<AccentPattern>(parameters, "pattern");
        IntervalSet? onsets = null;
        if (parameters.TryGetProperty("onsets", out _))
        {
            int[] indices = [.. Property(parameters, "onsets", JsonValueKind.Array)
                .EnumerateArray()
                .Select(index => index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out int value) && value >= 0 ? value : throw Damaged("variation parameters"))];
            onsets = indices.Length > 0 ? new IntervalSet(indices) : throw Damaged("variation parameters");
        }

        return (pattern == AccentPattern.Custom) == (onsets is not null)
            ? new AccentShiftParameters(pattern, onsets)
            : throw Damaged("variation parameters");
    }

    private static AccompanimentPatternParameters ReadAccompanimentPattern(JsonElement parameters) =>
        new(Name<AccompanimentPatternKind>(parameters, "kind"), Name<PatternUnit>(parameters, "unit"));

    private static DensityParameters ReadDensity(JsonElement parameters)
    {
        PatternUnit unit = Name<PatternUnit>(parameters, "unit");
        return unit is PatternUnit.Eighths or PatternUnit.Sixteenths
            ? new DensityParameters(Name<DensityKind>(parameters, "kind"), unit)
            : throw Damaged("variation parameters");
    }

    private static RhythmReshapeParameters ReadRhythmReshape(JsonElement parameters)
    {
        int eighths = Integer(parameters, "eighths");
        return eighths is < RhythmReshapeParameters.MinEighths or > RhythmReshapeParameters.MaxEighths
            ? throw Damaged("variation parameters")
            : new RhythmReshapeParameters(Name<RhythmReshapeKind>(parameters, "kind"), eighths);
    }

    private static MelodicSequenceParameters ReadMelodicSequence(JsonElement parameters)
    {
        int steps = Integer(parameters, "steps");
        int repeats = Integer(parameters, "repeats");
        return steps == 0 || Math.Abs(steps) > MelodicSequenceParameters.MaxSteps || repeats is < MelodicSequenceParameters.MinRepeats or > MelodicSequenceParameters.MaxRepeats
            ? throw Damaged("variation parameters")
            : new MelodicSequenceParameters(steps, repeats, Boolean(parameters, "diatonic"));
    }

    private static IntervalReshapeParameters ReadIntervalReshape(JsonElement parameters)
    {
        int steps = Integer(parameters, "steps");
        return steps == 0 || Math.Abs(steps) > IntervalReshapeParameters.MaxSteps
            ? throw Damaged("variation parameters")
            : new IntervalReshapeParameters(steps, ReadIntervals(parameters));
    }

    /// <summary>The picked intervals, or null when the file names none, which picks every interval.</summary>
    private static IntervalSet? ReadIntervals(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("intervals", out _))
        {
            return null;
        }

        int[] indices = [.. Property(parameters, "intervals", JsonValueKind.Array)
            .EnumerateArray()
            .Select(index => index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out int value) && value >= 0 ? value : throw Damaged("variation parameters"))];
        return indices.Length > 0 ? new IntervalSet(indices) : throw Damaged("variation parameters");
    }
}