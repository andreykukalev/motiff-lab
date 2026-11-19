using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Pieces;

using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.JsonWrite;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// Where a Notes came from (decision record 0030, section 2): the operation, the originals with their fingerprint and
/// stretch, the inputs, the engine, its parameters and the time, keeping the fields this build does not know.
/// </summary>
internal static class ProvenanceCodec
{
    private static readonly HashSet<string> s_provenanceFields = new(StringComparer.Ordinal) { "op", "from", "inputs", "engine", "params", "at" };

    internal static void WriteProvenance(Utf8JsonWriter json, NotesProvenance provenance)
    {
        json.WriteStartObject("provenance");
        json.WriteString("op", provenance.Op);
        json.WriteStartArray("from");
        foreach (ProvenanceSource source in provenance.From)
        {
            WriteProvenanceSource(json, source);
        }

        json.WriteEndArray();
        if (provenance.Inputs.Count > 0)
        {
            json.WriteStartArray("inputs");
            foreach (string input in provenance.Inputs)
            {
                json.WriteStringValue(input);
            }

            json.WriteEndArray();
        }

        if (provenance.Engine is { } engine)
        {
            json.WriteString("engine", engine);
        }

        if (provenance.ParamsJson is { } parameters)
        {
            WriteUnknownFields(json, [new UnknownField("params", parameters)]);
        }

        if (provenance.At is { } at)
        {
            json.WriteString("at", at.ToString("O", CultureInfo.InvariantCulture));
        }

        WriteUnknownFields(json, provenance.Unknown);
        json.WriteEndObject();
    }

    private static void WriteProvenanceSource(Utf8JsonWriter json, ProvenanceSource source)
    {
        json.WriteStartObject();
        json.WriteString("original", source.Original);
        if (source.Sha256 is { } sha256)
        {
            json.WriteString("sha256", sha256);
        }

        if (source.Pages is var (first, last))
        {
            WriteNumbers(json, "pages", [first, last]);
        }

        if (source.Milliseconds is var (from, to))
        {
            WriteNumbers(json, "ms", [from, to]);
        }

        if (source.Project is { } project)
        {
            json.WriteString("project", project);
        }

        json.WriteEndObject();
    }

    /// <summary>A provenance (0030, section 2); an operation this build does not know is read in the same shape and kept.</summary>
    internal static NotesProvenance ReadProvenance(JsonElement provenance)
    {
        Expect(provenance, JsonValueKind.Object, "provenance");
        try
        {
            return new NotesProvenance(Text(provenance, "op"))
            {
                From = provenance.TryGetProperty("from", out JsonElement from) ? [.. Elements(from, "provenance").Select(ReadProvenanceSource)] : [],
                Inputs = provenance.TryGetProperty("inputs", out JsonElement inputs)
                    ? [.. Elements(inputs, "provenance").Select(input => input.ValueKind == JsonValueKind.String ? input.GetString()! : throw Damaged("provenance"))]
                    : [],
                Engine = provenance.TryGetProperty("engine", out _) ? Text(provenance, "engine") : null,
                ParamsJson = provenance.TryGetProperty("params", out JsonElement parameters) ? Expected(parameters, "provenance").GetRawText() : null,
                At = provenance.TryGetProperty("at", out _) ? JsonRead.Time(provenance, "at") : null,
                Unknown = UnknownFieldsOf(provenance, s_provenanceFields),
            };
        }
        catch (ArgumentException)
        {
            throw Damaged("provenance");
        }
    }

    private static ProvenanceSource ReadProvenanceSource(JsonElement source)
    {
        Expect(source, JsonValueKind.Object, "provenance");
        (long First, long Last)? pages = source.TryGetProperty("pages", out JsonElement range) ? Pair(range) : null;
        (long First, long Last)? ms = source.TryGetProperty("ms", out JsonElement stretch) ? Pair(stretch) : null;
        try
        {
            return new ProvenanceSource(Text(source, "original"))
            {
                Sha256 = source.TryGetProperty("sha256", out _) ? Text(source, "sha256") : null,
                Pages = pages is var (first, last) ? (checked((int)first), checked((int)last)) : null,
                Milliseconds = ms,
                Project = source.TryGetProperty("project", out _) ? Text(source, "project") : null,
            };
        }
        catch (OverflowException)
        {
            throw Damaged("provenance");
        }
    }
}