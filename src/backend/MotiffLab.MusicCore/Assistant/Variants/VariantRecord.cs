using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assistant.Variants;

/// <summary>What a variant is now: kept as an idea, or kept and in the working notes. Worked out, never stored.</summary>
public enum VariantState
{
    /// <summary>Not kept: the draft being heard, or one discarded.</summary>
    Draft,

    /// <summary>Kept as an idea.</summary>
    Kept,

    /// <summary>Kept, and a carry of the assembly journal holds it in the working notes.</summary>
    InWork,
}

/// <summary>
/// A kept variant as the project file holds it (version 16; the AI companion study, section 4.6; docs/project-file.md,
/// "Ideas"): an idea whose variation is the variant's steps (<see cref="PhraseVariant.ToKept"/>), whose <c>notes</c> are
/// the Notes it was built on, and whose provenance is its record: the operation ("variation", or "assistant" with the
/// player's request and the model), when, and in <c>params</c> the chip, the seed, the variant it was made from, the bars
/// it adds or takes away and the values of the notes under it when it was made. So a variant comes back after a reload
/// with its lineage and is made again over the notes it was built on. An idea with no record (any earlier file, or one
/// kept in another way) reads as before: its steps over the notes under its passage as they are now. Pure: the same
/// variant gives the same bytes.
/// </summary>
public static class VariantRecord
{
    private const string ChipField = "chip";
    private const string RequestField = "request";
    private const string ModelField = "model";
    private const string SeedField = "seed";
    private const string ParentField = "parent";
    private const string BarChangeField = "barChange";
    private const string BaseField = "base";

    /// <summary>The variant as an idea to keep: its steps, its name, and its record in the provenance.</summary>
    /// <param name="notes">The Notes it was built on, by id; null for the working notes.</param>
    public static Observation ToObservation(PhraseVariant variant, string? notes, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(variant);
        KeptVariation kept = variant.ToKept();
        return Observation.Create(variant.Id, kept.Passage, kept, variant.Name, null, at) with { Notes = notes, Provenance = ProvenanceOf(variant) };
    }

    /// <summary>The variant's record, in the shape of a Notes' provenance.</summary>
    public static NotesProvenance ProvenanceOf(PhraseVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return new NotesProvenance(variant.Provenance.Op) { ParamsJson = ParamsOf(variant), At = variant.Provenance.At };
    }

    /// <summary>
    /// The variant an idea keeps: from its record when it has one, with the values of the notes under it as they were;
    /// otherwise its steps over its passage with the notes under it as they are in <paramref name="notes"/>. Null for an
    /// idea that keeps a note alone.
    /// </summary>
    public static PhraseVariant? Read(Observation observation, IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(notes);
        if (observation.Variation is not { } kept)
        {
            return null;
        }

        var variant = new PhraseVariant(observation.Id, observation.Name, kept.Passage, BaseNow(kept.NoteIds, notes), kept.Steps, kept.Keep, kept.Key, new VariantProvenance(VariantOrigin.Variation, observation.CreatedAt));
        return observation.Provenance is { } provenance && Recorded(variant, provenance) is { } recorded ? recorded : variant;
    }

    /// <summary>
    /// The notes the variant was built on: of <paramref name="candidates"/> (the working notes first, then the notes before
    /// the assembly), the first that hold every note under it with the values it was made on; failing that, the first that
    /// hold every note under it, with those notes given back the values they had; failing that, the first.
    /// </summary>
    public static (IReadOnlyList<Note> Notes, MeterMap Meter) BuiltOn(PhraseVariant variant, IReadOnlyList<(IReadOnlyList<Note> Notes, MeterMap Meter)> candidates)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("The notes to build on are given.", nameof(candidates));
        }

        Dictionary<string, NoteValues> values = variant.Base.Where(static note => note.Values != default).ToDictionary(static note => note.Id, static note => note.Values, StringComparer.Ordinal);
        foreach ((IReadOnlyList<Note> Notes, MeterMap Meter) candidate in candidates)
        {
            if (variant.Base.All(under => candidate.Notes.Any(note => note.Id == under.Id && (!values.TryGetValue(under.Id, out NoteValues was) || NoteValues.Of(note) == was))))
            {
                return candidate;
            }
        }

        foreach ((IReadOnlyList<Note> Notes, MeterMap Meter) candidate in candidates)
        {
            var ids = candidate.Notes.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
            if (variant.Base.Count > 0 && variant.Base.All(under => ids.Contains(under.Id)))
            {
                return ([.. candidate.Notes.Select(note => values.TryGetValue(note.Id, out NoteValues was) ? note with { Pitch = was.Pitch, Span = was.Span, Velocity = was.Velocity } : note)], candidate.Meter);
            }
        }

        return candidates[0];
    }

    /// <summary>What the variant kept as <paramref name="id"/> is in <paramref name="project"/>: kept, in the working notes, or not kept.</summary>
    public static VariantState StateOf(Project project, string id)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return project.Observations.All(observation => observation.Id != id) ? VariantState.Draft
            : VariantPromotion.Carried(project.Assembly.Steps).Contains(id, StringComparer.Ordinal) ? VariantState.InWork
            : VariantState.Kept;
    }

    private static BaseNote[] BaseNow(IReadOnlyList<string> ids, IReadOnlyList<Note> notes)
    {
        Dictionary<string, Note> byId = notes.ToDictionary(static note => note.Id, StringComparer.Ordinal);
        return [.. ids.Select(id => new BaseNote(id, byId.TryGetValue(id, out Note? note) ? NoteValues.Of(note) : default))];
    }

    private static string ParamsOf(PhraseVariant variant)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            VariantProvenance provenance = variant.Provenance;
            WriteText(json, ChipField, provenance.ChipId);
            WriteText(json, RequestField, provenance.Request);
            WriteText(json, ModelField, provenance.Model);
            json.WriteNumber(SeedField, provenance.Seed);
            WriteText(json, ParentField, variant.ParentId);
            if (variant.BarChange != 0)
            {
                json.WriteNumber(BarChangeField, variant.BarChange);
            }

            json.WriteStartArray(BaseField);
            foreach (BaseNote note in variant.Base)
            {
                json.WriteStartObject();
                json.WriteString("id", note.Id);
                if (note.Values != default)
                {
                    json.WriteNumber("pitch", note.Values.Pitch.MidiNumber);
                    json.WriteNumber("onset", note.Values.Span.Onset.Value);
                    json.WriteNumber("duration", note.Values.Span.Duration);
                    json.WriteNumber("velocity", note.Values.Velocity);
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteText(Utf8JsonWriter json, string name, string? value)
    {
        if (value is not null)
        {
            json.WriteString(name, value);
        }
    }

    /// <summary>The variant with its record; null when the provenance is not a variant's or its params do not read, so the idea reads as before.</summary>
    private static PhraseVariant? Recorded(PhraseVariant variant, NotesProvenance provenance)
    {
        VariantOrigin? origin = provenance.Op switch
        {
            "variation" => VariantOrigin.Variation,
            NotesProvenance.Assistant => VariantOrigin.Assistant,
            _ => null,
        };
        if (origin is not { } kind || provenance.ParamsJson is not { } parameters)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(parameters);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(BaseField, out JsonElement under) || under.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            BaseNote[] notes = [.. under.EnumerateArray().Select(ReadBaseNote)];
            if (!notes.Select(static note => note.Id).SequenceEqual(variant.BaseIds, StringComparer.Ordinal))
            {
                return null;
            }

            int barChange = root.TryGetProperty(BarChangeField, out JsonElement bars) ? bars.GetInt32() : 0;
            if (barChange is < -PhraseVariant.MaxBarChange or > PhraseVariant.MaxBarChange)
            {
                return null;
            }

            var made = new VariantProvenance(kind, provenance.At ?? variant.Provenance.At)
            {
                ChipId = TextOf(root, ChipField),
                Request = TextOf(root, RequestField),
                Model = TextOf(root, ModelField),
                Seed = root.TryGetProperty(SeedField, out JsonElement seed) ? seed.GetInt32() : 0,
            };
            return variant with { Base = notes, Provenance = made, ParentId = TextOf(root, ParentField), BarChange = barChange };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static BaseNote ReadBaseNote(JsonElement note)
    {
        string id = note.GetProperty("id").GetString() ?? throw new FormatException("A note under a variant has an id.");
        if (!note.TryGetProperty("pitch", out JsonElement pitch))
        {
            return new BaseNote(id, default);
        }

        var span = new TickSpan(new Tick(note.GetProperty("onset").GetInt64()), note.GetProperty("duration").GetInt64());
        return new BaseNote(id, new NoteValues(new Pitch(pitch.GetInt32()), span, note.GetProperty("velocity").GetInt32()));
    }

    private static string? TextOf(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? throw new FormatException($"\"{name}\" is a text.") : null;
}