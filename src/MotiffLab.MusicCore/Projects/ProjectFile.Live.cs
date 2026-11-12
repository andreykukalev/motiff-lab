using System.Text.Json;

using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The fields version 0.5 adds within version 15 (v0.5 data design, section 9; Andrei, 2026-11-12): whether the player
/// turned the corrections off ("editsOff", top level, written only when true), where a recording's bars change
/// signature ("transcription"."bars"."meters") and where in the recording file the stretch heard started
/// ("transcription"."sentFromMs"). Optional, like "partOf": versions 0.2 to 0.4 open the file, with the corrections on
/// and the bars in the grid's fallback "signature" from the same starts, and drop them when they write it.
/// </summary>
public static partial class ProjectFile
{
    /// <summary>"editsOff": true while the corrections are off; left out otherwise.</summary>
    private static void WriteEditsOff(Utf8JsonWriter json, Project project)
    {
        if (project.EditsOff)
        {
            json.WriteBoolean("editsOff", true);
        }
    }

    /// <summary>The project with the corrections off when its file says so; "editsOff" that is not true or false is damage.</summary>
    private static Project WithEditsOffIn(this Project project, JsonElement root) =>
        !root.TryGetProperty("editsOff", out JsonElement off) ? project : off.ValueKind switch
        {
            JsonValueKind.True => project with { EditsOff = true },
            JsonValueKind.False => project,
            _ => throw Damaged("editsOff"),
        };

    /// <summary>"sentFromMs" in "transcription": where in the recording file the stretch heard started; left out when not kept.</summary>
    private static void WriteSentFrom(Utf8JsonWriter json, TranscriptionOrigin transcription)
    {
        if (transcription.SentFromMilliseconds is { } from)
        {
            json.WriteNumber("sentFromMs", from);
        }
    }

    /// <summary>The "sentFromMs" of a "transcription", null when left out; a value that is not a whole number of 0 or more is damage.</summary>
    private static long? SentFromIn(JsonElement transcription) =>
        !transcription.TryGetProperty("sentFromMs", out JsonElement from) ? null
            : from.ValueKind == JsonValueKind.Number && from.TryGetInt64(out long value) && value >= 0 ? value
            : throw Damaged("sentFromMs");

    /// <summary>"meters": [{"at": 16th, "signature": "7/8"}] in a grid's "bars"; left out without a change of signature.</summary>
    private static void WriteMeters(Utf8JsonWriter json, BarGrid bars)
    {
        if (bars.Meters.Count == 0)
        {
            return;
        }

        json.WriteStartArray("meters");
        foreach (GridMeter meter in bars.Meters)
        {
            json.WriteStartObject();
            json.WriteNumber("at", meter.At);
            json.WriteString("signature", meter.Signature.ToString());
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>The grid with the changes of signature its "bars" keep; a change that is not well formed is damage.</summary>
    private static BarGrid WithMetersIn(BarGrid grid, JsonElement bars)
    {
        if (!bars.TryGetProperty("meters", out JsonElement meters))
        {
            return grid;
        }

        Expect(meters, JsonValueKind.Array, "bars");
        GridMeter[] read = [.. meters.EnumerateArray().Select(meter =>
        {
            Expect(meter, JsonValueKind.Object, "bars");
            return new GridMeter(Integer(meter, "at"), SignatureOf(Text(meter, "signature")) ?? throw Damaged("bars"));
        })];
        try
        {
            return grid with { Meters = read };
        }
        catch (ArgumentException)
        {
            throw Damaged("bars");
        }
    }

    /// <summary>A signature written as "7/8": any whole numbers a time signature can have; null otherwise.</summary>
    private static TimeSignature? SignatureOf(string text)
    {
        string[] parts = text.Split('/');
        if (parts.Length != 2
            || !int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int numerator)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int denominator))
        {
            return null;
        }

        try
        {
            return new TimeSignature(numerator, denominator);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}