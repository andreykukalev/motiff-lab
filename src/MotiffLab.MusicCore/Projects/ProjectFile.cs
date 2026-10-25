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

    /// <summary>
    /// 4 saves the melody operations (V4, V9, V10), Accompaniment pattern (V2), Articulation (V13)
    /// and each step's scope; 3 adds
    /// the edit log of note corrections; 2 saves each variation as a chain of steps. Files of earlier
    /// versions still open: their steps change the whole selection, versions 1 and 2 have no
    /// corrections, and version 1 holds one step per variation.
    /// </summary>
    public const int Version = 4;

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

            json.WriteStartArray("edits");
            foreach (NoteEdit edit in project.Edits.Edits)
            {
                json.WriteStartObject();
                json.WriteString("noteId", edit.NoteId);
                WriteValues(json, "before", edit.Before);
                if (edit.After is { } after)
                {
                    WriteValues(json, "after", after);
                }

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
            NoteEditLog edits = version >= 3
                ? new NoteEditLog(Property(root, "edits", JsonValueKind.Array).EnumerateArray().Select(ReadEdit))
                : NoteEditLog.Empty;
            return new Project(
                Text(root, "id"),
                Text(root, "fileName"),
                source,
                Time(root, "createdAt"),
                Time(root, "lastOpened"),
                motifs,
                variations)
            {
                Edits = edits,
            };
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

    private static void WriteValues(Utf8JsonWriter json, string name, NoteValues values)
    {
        json.WriteStartObject(name);
        json.WriteNumber("pitch", values.Pitch.MidiNumber);
        json.WriteNumber("onset", values.Span.Onset.Value);
        json.WriteNumber("duration", values.Span.Duration);
        json.WriteNumber("velocity", values.Velocity);
        json.WriteEndObject();
    }

    private static NoteEdit ReadEdit(JsonElement edit)
    {
        Expect(edit, JsonValueKind.Object, "note corrections");
        return new NoteEdit(
            Text(edit, "noteId"),
            ReadValues(Property(edit, "before", JsonValueKind.Object)),
            edit.TryGetProperty("after", out JsonElement after) ? ReadValues(after) : null);
    }

    private static NoteValues ReadValues(JsonElement values, string what = "note corrections")
    {
        Expect(values, JsonValueKind.Object, what);
        int pitch = Integer(values, "pitch");
        long onset = Long(values, "onset");
        long duration = Long(values, "duration");
        int velocity = Integer(values, "velocity");
        if (pitch is < 0 or > Pitch.MaxMidiNumber || onset < 0 || duration <= 0 || velocity is < Note.MinVelocity or > Note.MaxVelocity)
        {
            throw Damaged(what);
        }

        return new NoteValues(new Pitch(pitch), new TickSpan(new Tick(onset), duration), velocity);
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
        OperationParameters read = operation switch
        {
            HandEdit.OperationId => ReadHandEdit(parameters),
            RhythmReshape.OperationId => ReadRhythmReshape(parameters),
            Articulation.OperationId => new ArticulationParameters(Name<ArticulationKind>(parameters, "kind")),
            AccompanimentPattern.OperationId => new AccompanimentPatternParameters(Name<AccompanimentPatternKind>(parameters, "kind"), Name<PatternUnit>(parameters, "unit")),
            MelodicSequence.OperationId => ReadMelodicSequence(parameters),
            IntervalReshape.OperationId => ReadIntervalReshape(parameters),
            ChangeDirection.OperationId => new ChangeDirectionParameters(Boolean(parameters, "diatonic"), ReadIntervals(parameters)),
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

    private static bool Boolean(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw Damaged(name);

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