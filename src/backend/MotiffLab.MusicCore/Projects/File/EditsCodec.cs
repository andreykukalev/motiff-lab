using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Projects.File.JsonRead;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// The player's note corrections in the project file (version 3; an added note since 14, the group and who proposed
/// one since 16), and a note's values, which a correction and a hand edit both hold.
/// </summary>
internal static class EditsCodec
{
    internal static void WriteEdits(Utf8JsonWriter json, NoteEditLog edits)
    {
        json.WriteStartArray("edits");
        foreach (NoteEdit edit in edits.Edits)
        {
            json.WriteStartObject();
            json.WriteString("noteId", edit.NoteId);
            if (edit.Before is { } before)
            {
                WriteValues(json, "before", before);
            }
            else
            {
                // A note the player added (version 14, testing item 15): its track and hand, then its values.
                json.WriteStartObject("added");
                json.WriteNumber("track", edit.Track);
                if (edit.Hand is { } hand)
                {
                    json.WriteString("hand", hand.ToString());
                }

                json.WriteEndObject();
            }

            if (edit.After is { } after)
            {
                WriteValues(json, "after", after);
            }

            // Version 16: corrections accepted together, and who proposed one when it was not the player.
            if (edit.Group is { } group)
            {
                json.WriteString("group", group);
            }

            if (edit.Origin is { } origin)
            {
                json.WriteString("origin", origin);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    internal static NoteEdit ReadEdit(JsonElement edit)
    {
        Expect(edit, JsonValueKind.Object, "note corrections");
        if (edit.TryGetProperty("added", out JsonElement added))
        {
            Expect(added, JsonValueKind.Object, "note corrections");
            int track = Integer(added, "track");
            if (track < 0 || !edit.TryGetProperty("after", out JsonElement values))
            {
                throw Damaged("note corrections");
            }

            return WithGroupAndOrigin(new NoteEdit(Text(edit, "noteId"), null, ReadValues(values))
            {
                Track = track,
                Hand = added.TryGetProperty("hand", out _) ? Name<Hand>(added, "hand") : null,
            }, edit);
        }

        return WithGroupAndOrigin(new NoteEdit(
            Text(edit, "noteId"),
            ReadValues(Property(edit, "before", JsonValueKind.Object)),
            edit.TryGetProperty("after", out JsonElement after) ? ReadValues(after) : null), edit);
    }

    /// <summary>A correction with the group it was accepted in and who proposed it (version 16); the player's, alone, without them.</summary>
    private static NoteEdit WithGroupAndOrigin(NoteEdit read, JsonElement edit)
    {
        try
        {
            return read with
            {
                Group = edit.TryGetProperty("group", out _) ? Text(edit, "group") : null,
                Origin = edit.TryGetProperty("origin", out _) ? Text(edit, "origin") : null,
            };
        }
        catch (ArgumentException)
        {
            throw Damaged("note corrections");
        }
    }

    internal static void WriteValues(Utf8JsonWriter json, string name, NoteValues values)
    {
        json.WriteStartObject(name);
        json.WriteNumber("pitch", values.Pitch.MidiNumber);
        json.WriteNumber("onset", values.Span.Onset.Value);
        json.WriteNumber("duration", values.Span.Duration);
        json.WriteNumber("velocity", values.Velocity);
        json.WriteEndObject();
    }

    internal static NoteValues ReadValues(JsonElement values, string what = "note corrections")
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
}