using System.Text.Json;

using MotiffLab.MusicCore.Playback;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// What the roll shows (0018 section 4, the facade's <c>roll</c>): the flat arrays packed by <see cref="PianoRollData"/> and
/// the names it writes, with the voices named for a player, written with <see cref="JsonWrite"/>'s list writers (D12).
/// Its own writer, out of <see cref="Answer"/>, which holds enough types.
/// </summary>
internal static class RollAnswer
{
    /// <summary>The piano roll's data, with the field names the roll reads (Scripts/roll/roll-model.ts, RollPayload).</summary>
    public static void Write(Utf8JsonWriter json, RollPayload payload, RollNames names)
    {
        JsonWrite.Texts(json, "ids", payload.Ids);
        JsonWrite.Numbers(json, "notes", payload.Notes);
        JsonWrite.Numbers(json, "bars", payload.Bars);
        json.WriteNumber("end", payload.End);
        json.WriteStartObject("names");
        JsonWrite.Texts(json, "notes", names.Notes);
        JsonWrite.Numbers(json, "keyTicks", names.KeyTicks);
        json.WriteStartArray("rows");
        foreach (string[] row in names.Rows)
        {
            JsonWrite.Texts(json, null, row);
        }

        json.WriteEndArray();
        JsonWrite.Texts(json, "short", names.Short);
        json.WriteStartArray("shortRows");
        foreach (string[] row in names.ShortRows)
        {
            JsonWrite.Texts(json, null, row);
        }

        json.WriteEndArray();
        JsonWrite.Texts(json, "octaves", names.Octaves);
        json.WriteStartArray("voices");
        foreach (VoiceName voice in names.Voices)
        {
            json.WriteStartObject();
            json.WriteNumber("track", voice.Track);
            json.WriteNumber("voice", voice.Voice);
            json.WriteString("name", voice.Name);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }
}