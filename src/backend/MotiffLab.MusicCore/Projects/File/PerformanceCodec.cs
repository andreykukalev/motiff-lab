using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.ProvenanceCodec;
using static MotiffLab.MusicCore.Projects.File.SpanCodec;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// The performances in the project file (decision record 0022): each a compared Notes with how it lines up with the
/// working notes, the bars that are not a difference, the bars taken and the merge draft last applied.
/// </summary>
internal static class PerformanceCodec
{
    /// <summary>
    /// A compared Notes (version 16): its id, name, provenance and MIDI, and in "performance" how it lines up with the
    /// working notes, the bars that are not a difference and the bars taken (0022, section 8).
    /// </summary>
    internal static void WritePerformanceNotes(Utf8JsonWriter json, Project project, SavedPerformance performance)
    {
        json.WriteStartObject();
        json.WriteString("id", performance.Id);
        json.WriteString("name", performance.Name);
        WriteProvenance(json, project.ProvenanceOf(performance));
        json.WriteBase64String("music", performance.Midi);
        json.WriteStartObject("performance");
        json.WriteString("source", performance.Source.ToString());
        json.WriteNumber("recordingStart", performance.RecordingStart);
        json.WriteStartArray("anchors");
        foreach (Anchor anchor in performance.Anchors)
        {
            json.WriteStartArray();
            json.WriteNumberValue(anchor.Score.Value);
            json.WriteNumberValue(anchor.Microseconds);
            json.WriteEndArray();
        }

        json.WriteEndArray();
        json.WriteStartArray("dismissed");
        foreach (int bar in performance.Dismissed)
        {
            json.WriteNumberValue(bar);
        }

        json.WriteEndArray();
        json.WriteStartArray("taken");
        foreach (TakenBar bar in performance.Taken)
        {
            WriteTakenBar(json, bar);
        }

        json.WriteEndArray();
        WriteMerge(json, performance.Merge);
        json.WriteEndObject();
        WriteUnknownFields(json, project.UnknownFieldsAt(ProjectFile.NotesPath + performance.Id));
        json.WriteEndObject();
    }

    /// <summary>The merge draft last applied (testing items 32 and 34): optional, so a reader without it keeps what was taken.</summary>
    private static void WriteMerge(Utf8JsonWriter json, MergeRecord? merge)
    {
        if (merge is null)
        {
            return;
        }

        json.WriteStartObject("merge");
        if (merge.AppliedAt is { } at)
        {
            json.WriteString("appliedAt", at.ToString("O", CultureInfo.InvariantCulture));
        }

        json.WriteStartArray("bars");
        foreach (MergeDecision decision in merge.Decisions)
        {
            json.WriteStartObject();
            json.WriteNumber("bar", decision.Bar);
            json.WriteString("choice", decision.Choice.ToString());
            if (decision.Own is { } own)
            {
                json.WritePropertyName("own");
                WriteTakenBar(json, own);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteTakenBar(Utf8JsonWriter json, TakenBar bar)
    {
        json.WriteStartObject();
        json.WriteNumber("bar", bar.Bar);
        json.WriteStartArray("removed");
        foreach (string id in bar.Removed)
        {
            json.WriteStringValue(id);
        }

        json.WriteEndArray();
        json.WriteStartArray("added");
        foreach (TakenNote note in bar.Added)
        {
            json.WriteStartObject();
            json.WriteString("id", note.Id);
            json.WriteNumber("pitch", note.Pitch.MidiNumber);
            WriteSpan(json, note.Span);
            json.WriteNumber("velocity", note.Velocity);
            json.WriteNumber("track", note.Track);
            if (note.Hand is { } hand)
            {
                json.WriteString("hand", hand.ToString());
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>A performance from its comparison (<paramref name="comparison"/>: its source, start, anchors, dismissed and taken bars, merge) and its notes.</summary>
    internal static SavedPerformance ReadComparison(JsonElement comparison, string id, string name, byte[] midi, string? engine)
    {
        List<Anchor> anchors = [.. Property(comparison, "anchors", JsonValueKind.Array).EnumerateArray().Select(anchor =>
            anchor.ValueKind == JsonValueKind.Array && anchor.GetArrayLength() == 2 && anchor[0].TryGetInt64(out long tick) && tick >= 0 && anchor[1].TryGetInt64(out long micros)
                ? new Anchor(new Tick(tick), micros)
                : throw Damaged("performance anchors"))];
        List<int> dismissed = [.. Property(comparison, "dismissed", JsonValueKind.Array).EnumerateArray().Select(bar => bar.TryGetInt32(out int number) && number >= 0 ? number : throw Damaged("performance"))];
        List<TakenBar> taken = [.. Property(comparison, "taken", JsonValueKind.Array).EnumerateArray().Select(ReadTakenBar)];
        MergeRecord? merge = null;
        if (comparison.TryGetProperty("merge", out JsonElement kept))
        {
            Expect(kept, JsonValueKind.Object, "performance");
            merge = new MergeRecord(
                [.. Property(kept, "bars", JsonValueKind.Array).EnumerateArray().Select(decision =>
                {
                    Expect(decision, JsonValueKind.Object, "performance");
                    MergeChoice choice = Name<MergeChoice>(decision, "choice");
                    return new MergeDecision(Integer(decision, "bar"), choice, decision.TryGetProperty("own", out JsonElement own) ? ReadTakenBar(own) : null);
                })],
                kept.TryGetProperty("appliedAt", out _) ? JsonRead.Time(kept, "appliedAt") : null);
        }

        return new SavedPerformance(id, name, Name<PerformanceSource>(comparison, "source"), midi, Long(comparison, "recordingStart"), PerformanceAlignment.FromAnchors(anchors).Anchors)
        {
            Engine = engine,
            Dismissed = dismissed,
            Taken = taken,
            Merge = merge,
        };
    }

    private static TakenBar ReadTakenBar(JsonElement bar)
    {
        Expect(bar, JsonValueKind.Object, "performance");
        return new TakenBar(
            Integer(bar, "bar"),
            [.. Property(bar, "removed", JsonValueKind.Array).EnumerateArray().Select(id => id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()) ? id.GetString()! : throw Damaged("performance"))],
            [.. Property(bar, "added", JsonValueKind.Array).EnumerateArray().Select(note =>
            {
                Expect(note, JsonValueKind.Object, "performance");
                int pitch = Integer(note, "pitch");
                Hand? hand = note.TryGetProperty("hand", out _) ? Name<Hand>(note, "hand") : null;
                string id = Text(note, "id");
                return pitch is >= 0 and <= Pitch.MaxMidiNumber && PerformanceTake.IsTaken(id)
                    ? new TakenNote(id, new Pitch(pitch), ReadSpan(note, "performance"), Integer(note, "velocity"), Integer(note, "track"), hand)
                    : throw Damaged("performance");
            })]);
    }

    internal static SavedPerformance ReadPerformanceNotes(JsonElement entry, JsonElement comparison)
    {
        Expect(comparison, JsonValueKind.Object, "performance");
        NotesProvenance provenance = ReadProvenance(Property(entry, "provenance", JsonValueKind.Object));
        try
        {
            return ReadComparison(comparison, Text(entry, "id"), Text(entry, "name"), Property(entry, "music", JsonValueKind.String).GetBytesFromBase64(), provenance.Engine) with
            {
                From = provenance.Original?.Original,
                At = provenance.At,
                Provenance = provenance,
            };
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw Damaged("performance");
        }
    }
}