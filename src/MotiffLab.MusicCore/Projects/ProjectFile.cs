using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The project file: one JSON document that holds a project, used both for autosave on the device
/// and for export and re-import as a backup (S1-P1, AC-S1-20).
/// </summary>
/// <remarks>
/// Written by hand rather than by reflection, so the layout is fixed and the same project always
/// gives the same bytes. Enums are written by name. Reading checks every field and throws
/// <see cref="ProjectFileException"/> with a plain reason; it never half-reads a project.
/// </remarks>
public static class ProjectFile
{
    public const string FormatName = "motiff-lab-project";

    /// <summary>2 saves each variation as a chain of steps; files of version 1 hold one step and still open.</summary>
    public const int Version = 2;

    /// <summary>The extension an exported project gets.</summary>
    public const string Extension = ".motiff.json";

    public static byte[] Write(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("format", FormatName);
            json.WriteNumber("version", Version);
            json.WriteString("id", project.Id);
            json.WriteString("fileName", project.FileName);
            json.WriteString("createdAt", project.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteString("lastOpened", project.LastOpened.ToString("O", CultureInfo.InvariantCulture));
            json.WriteBase64String("source", project.Source);

            json.WriteStartArray("motifs");
            foreach (SavedMotif motif in project.Motifs)
            {
                json.WriteStartObject();
                json.WriteString("id", motif.Id);
                json.WriteString("name", motif.Name);
                json.WriteString("kind", motif.Kind.ToString());
                json.WriteString("mode", motif.Mode.ToString());
                json.WriteNumber("onset", motif.Span.Onset.Value);
                json.WriteNumber("duration", motif.Span.Duration);
                json.WriteStartObject("filter");
                if (motif.Filter.Hand is Hand hand)
                {
                    json.WriteString("hand", hand.ToString());
                }

                if (motif.Filter.Track is int track)
                {
                    json.WriteNumber("track", track);
                }

                if (motif.Filter.Voice is int voice)
                {
                    json.WriteNumber("voice", voice);
                }

                json.WriteEndObject();
                json.WriteStartArray("noteIds");
                foreach (string id in motif.NoteIds)
                {
                    json.WriteStringValue(id);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartArray("variations");
            foreach (SavedVariation variation in project.Variations)
            {
                json.WriteStartObject();
                json.WriteString("id", variation.Id);
                json.WriteString("motifId", variation.MotifId);
                json.WriteStartArray("steps");
                foreach (SavedStep step in variation.Steps)
                {
                    json.WriteStartObject();
                    WriteStep(json, step);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                if (variation.Pending is { } pending)
                {
                    json.WriteStartObject("pending");
                    WriteStep(json, pending);
                    json.WriteEndObject();
                }

                json.WriteStartArray("keep");
                foreach (KeepProperty property in variation.Keep)
                {
                    json.WriteStringValue(property.ToString());
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static Project Read(ReadOnlySpan<byte> data)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(data.ToArray());
        }
        catch (JsonException)
        {
            throw new ProjectFileException("This is not a Motiff Lab project file: it is not valid JSON.");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out JsonElement format) || format.ValueKind != JsonValueKind.String || format.GetString() != FormatName)
            {
                throw new ProjectFileException("This is not a Motiff Lab project file.");
            }

            int version = Integer(root, "version");
            if (version > Version)
            {
                throw new ProjectFileException("This project was saved by a newer version of Motiff Lab. Update the app and try again.");
            }

            if (version < 1)
            {
                throw Damaged("version");
            }

            byte[] source;
            try
            {
                source = Property(root, "source", JsonValueKind.String).GetBytesFromBase64();
            }
            catch (FormatException)
            {
                throw Damaged("source");
            }

            var motifs = Property(root, "motifs", JsonValueKind.Array).EnumerateArray().Select(ReadMotif).ToList();
            var variations = Property(root, "variations", JsonValueKind.Array).EnumerateArray().Select(variation => ReadVariation(variation, version)).ToList();
            return new Project(
                Text(root, "id"),
                Text(root, "fileName"),
                source,
                Time(root, "createdAt"),
                Time(root, "lastOpened"),
                motifs,
                variations);
        }
    }

    private static SavedMotif ReadMotif(JsonElement motif)
    {
        Expect(motif, JsonValueKind.Object, "motifs");
        long onset = Long(motif, "onset");
        long duration = Long(motif, "duration");
        if (onset < 0 || duration <= 0)
        {
            throw Damaged("motif range");
        }

        JsonElement filter = Property(motif, "filter", JsonValueKind.Object);
        Hand? hand = filter.TryGetProperty("hand", out _) ? Name<Hand>(filter, "hand") : null;
        int? track = filter.TryGetProperty("track", out _) ? Integer(filter, "track") : null;
        int? voice = filter.TryGetProperty("voice", out _) ? Integer(filter, "voice") : null;
        SelectionFilter selectionFilter;
        try
        {
            selectionFilter = (hand, track, voice) switch
            {
                (null, null, null) => SelectionFilter.All,
                ({ } h, null, null) => SelectionFilter.ForHand(h),
                (null, { } t, null) => SelectionFilter.ForTrack(t),
                (null, { } t, { } v) => SelectionFilter.ForVoice(t, v),
                _ => throw Damaged("motif filter"),
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Damaged("motif filter");
        }

        var noteIds = Property(motif, "noteIds", JsonValueKind.Array)
            .EnumerateArray()
            .Select(id => id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()) ? id.GetString()! : throw Damaged("motif notes"))
            .ToList();
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
            new TickSpan(new Tick(onset), duration),
            selectionFilter,
            noteIds);
    }

    private static void WriteStep(Utf8JsonWriter json, SavedStep step)
    {
        json.WriteString("operation", step.Operation);
        json.WriteStartObject("parameters");
        switch (step.Parameters)
        {
            case RhythmReshapeParameters reshape:
                json.WriteString("kind", reshape.Kind.ToString());
                json.WriteNumber("eighths", reshape.Eighths);
                break;
            default:
                throw new ArgumentException($"Operation {step.Operation} has parameters the project file cannot write.", nameof(step));
        }

        json.WriteEndObject();
    }

    private static SavedVariation ReadVariation(JsonElement variation, int version)
    {
        Expect(variation, JsonValueKind.Object, "variations");
        var keep = Property(variation, "keep", JsonValueKind.Array)
            .EnumerateArray()
            .Select(property => property.ValueKind == JsonValueKind.String && TryName(property.GetString(), out KeepProperty value) ? value : throw Damaged("Keep choices"))
            .Distinct()
            .Order()
            .ToList();

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
            keep);
    }

    private static SavedStep ReadStep(JsonElement step)
    {
        Expect(step, JsonValueKind.Object, "variation steps");
        string operation = Text(step, "operation");
        JsonElement parameters = Property(step, "parameters", JsonValueKind.Object);
        if (operation != RhythmReshape.OperationId)
        {
            throw new ProjectFileException($"A variation uses an operation ({operation}) this version of Motiff Lab does not have.");
        }

        int eighths = Integer(parameters, "eighths");
        if (eighths is < RhythmReshapeParameters.MinEighths or > RhythmReshapeParameters.MaxEighths)
        {
            throw Damaged("variation parameters");
        }

        return new SavedStep(operation, new RhythmReshapeParameters(Name<RhythmReshapeKind>(parameters, "kind"), eighths));
    }

    private static JsonElement Property(JsonElement parent, string name, JsonValueKind kind)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != kind)
        {
            throw Damaged(name);
        }

        return value;
    }

    private static void Expect(JsonElement element, JsonValueKind kind, string what)
    {
        if (element.ValueKind != kind)
        {
            throw Damaged(what);
        }
    }

    private static string Text(JsonElement parent, string name)
    {
        string value = Property(parent, name, JsonValueKind.String).GetString()!;
        return value.Length > 0 ? value : throw Damaged(name);
    }

    private static int Integer(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Number).TryGetInt32(out int value) ? value : throw Damaged(name);

    private static long Long(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Number).TryGetInt64(out long value) ? value : throw Damaged(name);

    private static DateTimeOffset Time(JsonElement parent, string name) =>
        DateTimeOffset.TryParseExact(Text(parent, name), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value)
            ? value
            : throw Damaged(name);

    private static T Name<T>(JsonElement parent, string name)
        where T : struct, Enum =>
        TryName(Text(parent, name), out T value) ? value : throw Damaged(name);

    /// <summary>An enum by its exact name; numbers are not names.</summary>
    private static bool TryName<T>(string? text, out T value)
        where T : struct, Enum
    {
        value = default;
        return text is not null && Enum.GetNames<T>().Contains(text, StringComparer.Ordinal) && Enum.TryParse(text, out value);
    }

    private static ProjectFileException Damaged(string what) =>
        new($"This project file is damaged: its {what} cannot be read.");
}