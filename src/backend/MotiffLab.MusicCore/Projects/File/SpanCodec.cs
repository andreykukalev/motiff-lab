using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Projects.File.JsonRead;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// The shapes of time every part of the project file shares: a span ("onset", "duration" in ticks), a range with its
/// filter, a list of ticks and a list of note ids.
/// </summary>
internal static class SpanCodec
{
    internal static void WriteSpan(Utf8JsonWriter json, TickSpan span)
    {
        json.WriteNumber("onset", span.Onset.Value);
        json.WriteNumber("duration", span.Duration);
    }

    internal static TickSpan ReadSpan(JsonElement parent, string what)
    {
        long onset = Long(parent, "onset");
        long duration = Long(parent, "duration");
        return onset >= 0 && duration > 0 ? new TickSpan(new Tick(onset), duration) : throw Damaged($"{what} range");
    }

    internal static void WriteTicks(Utf8JsonWriter json, string name, IEnumerable<Tick> ticks)
    {
        json.WriteStartArray(name);
        foreach (Tick tick in ticks)
        {
            json.WriteNumberValue(tick.Value);
        }

        json.WriteEndArray();
    }

    internal static void WriteRange(Utf8JsonWriter json, TickSpan span, SelectionFilter filter)
    {
        json.WriteNumber("onset", span.Onset.Value);
        json.WriteNumber("duration", span.Duration);
        json.WriteStartObject("filter");
        if (filter.Hand is Hand hand)
        {
            json.WriteString("hand", hand.ToString());
        }

        if (filter.Track is int track)
        {
            json.WriteNumber("track", track);
        }

        if (filter.Voice is int voice)
        {
            json.WriteNumber("voice", voice);
        }

        json.WriteEndObject();
    }

    /// <summary>A range and its filter, written by <see cref="WriteRange"/>; <paramref name="what"/> names it in a damaged file's reason.</summary>
    internal static (TickSpan Span, SelectionFilter Filter) ReadRange(JsonElement parent, string what)
    {
        long onset = Long(parent, "onset");
        long duration = Long(parent, "duration");
        if (onset < 0 || duration <= 0)
        {
            throw Damaged($"{what} range");
        }

        JsonElement filter = Property(parent, "filter", JsonValueKind.Object);
        Hand? hand = filter.TryGetProperty("hand", out _) ? Name<Hand>(filter, "hand") : null;
        int? track = filter.TryGetProperty("track", out _) ? Integer(filter, "track") : null;
        int? voice = filter.TryGetProperty("voice", out _) ? Integer(filter, "voice") : null;
        try
        {
            return (new TickSpan(new Tick(onset), duration), (hand, track, voice) switch
            {
                (null, null, null) => SelectionFilter.All,
                ({ } h, null, null) => SelectionFilter.ForHand(h),
                (null, { } t, null) => SelectionFilter.ForTrack(t),
                (null, { } t, { } v) => SelectionFilter.ForVoice(t, v),
                _ => throw Damaged($"{what} filter"),
            });
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Damaged($"{what} filter");
        }
    }

    internal static void WriteNoteIds(Utf8JsonWriter json, IReadOnlyList<string> noteIds)
    {
        json.WriteStartArray("noteIds");
        foreach (string id in noteIds)
        {
            json.WriteStringValue(id);
        }

        json.WriteEndArray();
    }

    internal static List<string> ReadNoteIds(JsonElement parent, string what) =>
        Property(parent, "noteIds", JsonValueKind.Array)
            .EnumerateArray()
            .Select(id => id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()) ? id.GetString()! : throw Damaged(what))
            .ToList();
}