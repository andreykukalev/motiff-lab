using System.Text.Json;

using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>What plays and what the roll shows (0018 section 4): the playback score, its tempo and the hands, and the roll's flat arrays (packed by <c>PianoRollData</c>, written with <see cref="JsonWrite"/>'s list writers: D12).</summary>
internal static partial class Answer
{
    /// <summary>The piano roll's data, with the field names the roll reads (Scripts/roll/roll-model.ts, RollPayload).</summary>
    public static void Roll(Utf8JsonWriter json, RollPayload payload, RollNames names)
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
        json.WriteEndObject();
    }

    /// <summary>
    /// "tempo": {"perMinute", "source"} of the tempo at the start as played, quarter notes a minute; "piece": the same as
    /// the piece states it, before the player's tempo; "player": the player's tempo for the piece, or null (version 0.5.4).
    /// "beat": "quarter", or "dottedQuarter" in 6/8 and the other compound meters, the note the player reads and types the
    /// tempo in (<see cref="TempoBeats"/>); "perMinute" and "player" stay quarter notes, "beatsPerMinute" counts the beat.
    /// </summary>
    public static void PlaybackTempoAnswer(Utf8JsonWriter json, PlaybackScore score)
    {
        TempoBeat beat = TempoBeats.At(score.Meter, Tick.Zero);
        json.WriteStartObject("tempo");
        json.WriteNumber("perMinute", score.StartTempo.PerMinute);
        json.WriteString("source", score.StartTempo.Source.Name());
        json.WriteString("beat", beat.Name());
        json.WriteNumber("beatsPerMinute", beat.PerMinute(score.StartTempo.MicrosecondsPerQuarter));
        json.WriteStartObject("piece");
        json.WriteNumber("perMinute", score.PieceTempos[0].PerMinute);
        json.WriteString("source", score.PieceTempos[0].Source.Name());
        json.WriteNumber("beatsPerMinute", beat.PerMinute(score.PieceTempos[0].MicrosecondsPerQuarter));
        json.WriteEndObject();
        if (score.StartTempo.Source == TempoSource.Player)
        {
            json.WriteNumber("player", score.StartTempo.PerMinute);
        }
        else
        {
            json.WriteNull("player");
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// A playback score: its end, notes, pedal, tempos, beats and bars. Within contract 12 (version 0.5.2, the iPad's one
    /// playback position) each beat also tells its "bar" and its "beat" in the bar, and each bar the "firstBeat" its start
    /// falls in, counted as <see cref="MeterMap.ToPosition"/> counts them, so an app writes a place as bar.beat ("16.2")
    /// without counting beats itself.
    /// </summary>
    /// <remarks>
    /// Within contract 12 (version 0.5.4, #511 section 17, fields added) each tempo tells its "source" ("notated",
    /// "inferred", "assumed" or "player"), and "tempo" what the tempo control shows: see <see cref="PlaybackTempoAnswer"/>.
    /// </remarks>
    public static void Playback(Utf8JsonWriter json, PlaybackScore score)
    {
        json.WriteNumber("end", score.End.Value);
        json.WriteStartArray("notes");
        foreach (PlaybackNote note in score.Notes)
        {
            json.WriteStartObject();
            json.WriteNumber("onset", note.Span.Onset.Value);
            json.WriteNumber("duration", note.Span.Duration);
            json.WriteNumber("pitch", note.Pitch);
            json.WriteNumber("velocity", note.Velocity);
            json.WriteNumber("track", note.Track);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("pedal");
        foreach (PlaybackPedal held in score.Pedal)
        {
            json.WriteStartObject();
            json.WriteNumber("onset", held.Span.Onset.Value);
            json.WriteNumber("duration", held.Span.Duration);
            json.WriteNumber("track", held.Track);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("tempos");
        foreach (PlaybackTempo tempo in score.Tempos)
        {
            json.WriteStartObject();
            json.WriteNumber("at", tempo.At.Value);
            json.WriteNumber("microsecondsPerQuarter", tempo.MicrosecondsPerQuarter);
            json.WriteString("source", tempo.Source.Name());
            json.WriteEndObject();
        }

        json.WriteEndArray();
        PlaybackTempoAnswer(json, score);
        json.WriteStartArray("beats");
        foreach (PlaybackBeat beat in score.Beats)
        {
            json.WriteStartObject();
            json.WriteNumber("at", beat.At.Value);
            json.WriteBoolean("downbeat", beat.IsDownbeat);
            MetricPosition place = score.Meter.ToPosition(beat.At);
            json.WriteNumber("bar", place.Bar);
            json.WriteNumber("beat", place.Beat);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("bars");
        foreach (PlaybackBar bar in score.Bars)
        {
            json.WriteStartObject();
            json.WriteNumber("number", bar.Number);
            json.WriteNumber("onset", bar.Span.Onset.Value);
            json.WriteNumber("end", bar.Span.End.Value);
            json.WriteNumber("beatTicks", bar.BeatTicks);
            json.WriteNumber("beatsPerBar", bar.BeatsPerBar);
            json.WriteBoolean("pickup", bar.IsPickup);
            json.WriteNumber("firstBeat", score.Meter.ToPosition(bar.Span.Onset).Beat);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>Which hands the piece's notes carry and whether any was inferred: {"hands": {right, left, inferred}} (#328).</summary>
    public static void Hands(Utf8JsonWriter json, PlaybackHands hands)
    {
        json.WriteStartObject("hands");
        json.WriteBoolean("right", hands.Right);
        json.WriteBoolean("left", hands.Left);
        json.WriteBoolean("inferred", hands.Inferred);
        json.WriteEndObject();
    }
}