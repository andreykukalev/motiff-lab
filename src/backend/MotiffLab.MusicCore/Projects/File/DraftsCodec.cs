using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

using static MotiffLab.MusicCore.Projects.File.AssemblyCodec;
using static MotiffLab.MusicCore.Projects.File.JsonRead;
using static MotiffLab.MusicCore.Projects.File.JsonWrite;
using static MotiffLab.MusicCore.Projects.File.MarksCodec;
using static MotiffLab.MusicCore.Projects.File.ProvenanceCodec;
using static MotiffLab.MusicCore.Projects.File.SpanCodec;
using static MotiffLab.MusicCore.Projects.File.UnknownEntities;

namespace MotiffLab.MusicCore.Projects.File;

/// <summary>
/// The piece's drafts in the workspace (version 17, decision record 0034; the format is docs/project-file.md, "Drafts").
/// A draft with a value this build does not know, at any depth, is kept whole as an unknown entity (0034, section 7):
/// no draft is played, analysed or synced as the piece's notes, so keeping it aside changes nothing that sounds.
/// </summary>
internal static class DraftsCodec
{
    private const string What = "drafts";

    /// <summary>The kinds of a step, in the file in lower case; a kind a later build adds keeps the draft unknown.</summary>
    private enum StepKind
    {
        Take,
        Leave,
        Transpose,
        Tempo,
    }

    /// <summary>"drafts", the known drafts and the unknown ones where they stood; left out while the piece has none.</summary>
    internal static void WriteDrafts(Utf8JsonWriter json, Project project)
    {
        List<UnknownEntity> unknown = KeptUnknown(project.Unknown, Project.DraftsList, project.Drafts.Select(static draft => draft.Id), null, out _);
        if (project.Drafts.Count > 0 || unknown.Count > 0)
        {
            WriteList(json, Project.DraftsList, project.Drafts, unknown, draft => WriteDraft(json, draft));
        }
    }

    /// <summary>The drafts of a workspace; none when it has no "drafts", or in a file before version 16, which has no workspace.</summary>
    internal static List<Draft> ReadDrafts(JsonElement work, int version, Unknowns unknowns)
    {
        if (version < 16 || !work.TryGetProperty(Project.DraftsList, out JsonElement drafts))
        {
            return [];
        }

        List<Draft> read = ReadList(Expected(drafts, JsonValueKind.Array), Project.DraftsList, ReadDraft, unknowns);
        return read.Select(static draft => draft.Id).Distinct(StringComparer.Ordinal).Count() == read.Count ? read : throw Damaged(What);
    }

    /// <summary>Whether two drafts are written the same: the same draft, unchanged.</summary>
    internal static bool Same(Draft one, Draft other) => Written(one).AsSpan().SequenceEqual(Written(other));

    private static byte[] Written(Draft draft)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            WriteDraft(json, draft);
        }

        return buffer.ToArray();
    }

    private static JsonElement Expected(JsonElement element, JsonValueKind kind)
    {
        Expect(element, kind, What);
        return element;
    }

    private static void WriteDraft(Utf8JsonWriter json, Draft draft)
    {
        json.WriteStartObject();
        json.WriteString("id", draft.Id);
        json.WriteString("name", draft.Name);
        json.WriteString("createdAt", draft.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        json.WriteStartArray("places");
        foreach (DraftPlace place in draft.Places)
        {
            json.WriteStartObject();
            json.WriteString("id", place.Id);
            json.WriteString("base", place.Base);
            WriteVariants(json, "variants", place.Variants);
            if (!place.Marks.IsEmpty)
            {
                WriteMarks(json, place.Marks);
            }

            if (place.Bridge)
            {
                json.WriteBoolean("bridge", true);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        WriteVariants(json, "shelf", draft.Shelf);
        json.WriteNumber("next", draft.Next);
        json.WriteStartArray("steps");
        foreach (DraftStep step in draft.Steps)
        {
            WriteStep(json, step);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteVariants(Utf8JsonWriter json, string name, IReadOnlyList<DraftVariant> variants)
    {
        json.WriteStartArray(name);
        foreach (DraftVariant variant in variants)
        {
            json.WriteStartObject();
            json.WriteString("id", variant.Id);
            json.WriteString("addedAt", variant.AddedAt.ToString("O", CultureInfo.InvariantCulture));
            WriteSource(json, variant.From);
            WriteProvenance(json, variant.Provenance);
            json.WriteStartObject("music");
            WriteMusic(json, variant.Music, tempoSources: true);
            json.WriteEndObject();
            if (variant.Anchors.Count > 0)
            {
                json.WriteStartArray("anchors");
                foreach (DraftAnchor anchor in variant.Anchors)
                {
                    json.WriteStartObject();
                    json.WriteNumber("place", anchor.Place.Value);
                    json.WriteNumber("variant", anchor.Variant.Value);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteSource(Utf8JsonWriter json, DraftSource from)
    {
        json.WriteStartObject("from");
        json.WriteString("notes", from.Notes);
        if (from.Project is { } project)
        {
            json.WriteString("project", project);
        }

        json.WriteStartObject("span");
        WriteSpan(json, from.Span);
        json.WriteEndObject();
        if (from.Bars is var (first, last))
        {
            WriteNumbers(json, "bars", [first, last]);
        }

        if (from.Milliseconds is var (start, end))
        {
            WriteNumbers(json, "ms", [start, end]);
        }

        json.WriteString("part", Lower(from.Part));
        json.WriteEndObject();
    }

    private static void WriteStep(Utf8JsonWriter json, DraftStep step)
    {
        json.WriteStartObject();
        json.WriteNumber("number", step.Number);
        json.WriteString("at", step.At.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("kind", step switch { DraftTake => "take", DraftLeave => "leave", DraftTranspose => "transpose", _ => "tempo" });
        json.WriteString("place", step.Place);
        switch (step)
        {
            case DraftTake take:
                json.WriteString("variant", take.Variant);
                WriteTechnique(json, take.Technique);
                if (take.Part is { } part)
                {
                    json.WriteString("part", Lower(part));
                }

                WriteIds(json, "removed", take.Removed);
                WriteIds(json, "added", take.Added);
                if (take.Insert is { } insert)
                {
                    json.WriteStartObject("insert");
                    json.WriteNumber("at", insert.At.Value);
                    json.WriteStartObject("span");
                    WriteSpan(json, insert.Span);
                    json.WriteEndObject();
                    json.WriteEndObject();
                }

                break;
            case DraftLeave leave:
                if (leave.Variant is { } variant)
                {
                    json.WriteString("variant", variant);
                }

                WriteTechnique(json, leave.Technique);
                if (leave.Seam)
                {
                    json.WriteBoolean("seam", true);
                }

                break;
            case DraftTranspose transpose:
                json.WriteNumber("semitones", transpose.Semitones);
                break;
            case DraftTempo tempo:
                json.WriteNumber("quarterNotesPerMinute", tempo.QuarterNotesPerMinute);
                break;
        }

        if (step.Group is { } group)
        {
            json.WriteString("group", group);
        }

        if (step.Origin is { } origin)
        {
            json.WriteString("origin", origin);
        }

        json.WriteEndObject();
    }

    private static void WriteTechnique(Utf8JsonWriter json, DraftTechnique? technique)
    {
        if (technique is null)
        {
            return;
        }

        json.WriteStartObject("technique");
        json.WriteString("kind", Lower(technique.Kind));
        WriteNumbers(json, "bars", technique.Bars.Select(static bar => (long)bar));
        json.WriteEndObject();
    }

    /// <summary>A take's note ids, left out when there are none (a take that inserts bars).</summary>
    private static void WriteIds(Utf8JsonWriter json, string name, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        json.WriteStartArray(name);
        foreach (string id in ids)
        {
            json.WriteStringValue(id);
        }

        json.WriteEndArray();
    }

    private static Draft ReadDraft(JsonElement draft)
    {
        Expect(draft, JsonValueKind.Object, What);
        try
        {
            List<DraftPlace> places = [.. Elements(Property(draft, "places", JsonValueKind.Array), What).Select(ReadPlace)];
            List<DraftVariant> shelf = [.. Elements(Property(draft, "shelf", JsonValueKind.Array), What).Select(ReadVariant)];
            List<DraftStep> steps = [.. Elements(Property(draft, "steps", JsonValueKind.Array), What).Select(ReadStep)];
            return new Draft(Text(draft, "id"), Text(draft, "name"), JsonRead.Time(draft, "createdAt"), places, shelf, steps, Integer(draft, "next"));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            throw Damaged(What);
        }
    }

    private static DraftPlace ReadPlace(JsonElement place)
    {
        Expect(place, JsonValueKind.Object, What);
        return new DraftPlace(Text(place, "id"), Text(place, "base"), [.. Elements(Property(place, "variants", JsonValueKind.Array), What).Select(ReadVariant)])
        {
            // Inside a draft nothing is kept apart: an unknown value in its marks keeps the whole draft unknown.
            Marks = place.TryGetProperty("marks", out JsonElement marks) ? ReadMarks(Expected(marks, JsonValueKind.Object), new Unknowns(tolerant: false)) : Marks.Empty,
            Bridge = place.TryGetProperty("bridge", out _) && Boolean(place, "bridge"),
        };
    }

    private static DraftVariant ReadVariant(JsonElement variant)
    {
        Expect(variant, JsonValueKind.Object, What);
        JsonElement from = Property(variant, "from", JsonValueKind.Object);
        var source = new DraftSource(Text(from, "notes"), ReadSpan(Property(from, "span", JsonValueKind.Object), What))
        {
            Project = from.TryGetProperty("project", out _) ? Text(from, "project") : null,
            Bars = from.TryGetProperty("bars", out JsonElement bars) ? (checked((int)Pair(bars).First), checked((int)Pair(bars).Last)) : null,
            Milliseconds = from.TryGetProperty("ms", out JsonElement ms) ? Pair(ms) : null,
            Part = LowerName<DraftPart>(from, "part"),
        };
        return new DraftVariant(Text(variant, "id"), JsonRead.Time(variant, "addedAt"), source, ReadProvenance(Property(variant, "provenance", JsonValueKind.Object)), ReadMusic(Property(variant, "music", JsonValueKind.Object), What))
        {
            Anchors = variant.TryGetProperty("anchors", out JsonElement anchors)
                ? [.. Elements(anchors, What).Select(static anchor => new DraftAnchor(new Tick(Long(anchor, "place")), new Tick(Long(anchor, "variant"))))]
                : [],
        };
    }

    private static DraftStep ReadStep(JsonElement step)
    {
        Expect(step, JsonValueKind.Object, What);
        int number = Integer(step, "number");
        DateTimeOffset at = JsonRead.Time(step, "at");
        string place = Text(step, "place");
        DraftStep read = LowerName<StepKind>(step, "kind") switch
        {
            StepKind.Take => new DraftTake(number, at, place, Text(step, "variant"))
            {
                Technique = ReadTechnique(step),
                Part = step.TryGetProperty("part", out _) ? LowerName<DraftPart>(step, "part") : null,
                Removed = ReadIds(step, "removed"),
                Added = ReadIds(step, "added"),
                Insert = step.TryGetProperty("insert", out JsonElement insert)
                    ? new DraftInsert(new Tick(Long(Expected(insert, JsonValueKind.Object), "at")), ReadSpan(Property(insert, "span", JsonValueKind.Object), What))
                    : null,
            },
            StepKind.Leave => new DraftLeave(number, at, place)
            {
                Variant = step.TryGetProperty("variant", out _) ? Text(step, "variant") : null,
                Technique = ReadTechnique(step),
                Seam = step.TryGetProperty("seam", out _) && Boolean(step, "seam"),
            },
            StepKind.Transpose => new DraftTranspose(number, at, place, Integer(step, "semitones")),
            _ => new DraftTempo(number, at, place, Integer(step, "quarterNotesPerMinute")),
        };
        return read with
        {
            Group = step.TryGetProperty("group", out _) ? Text(step, "group") : null,
            Origin = step.TryGetProperty("origin", out _) ? Text(step, "origin") : null,
        };
    }

    private static DraftTechnique? ReadTechnique(JsonElement step)
    {
        if (!step.TryGetProperty("technique", out JsonElement technique))
        {
            return null;
        }

        Expect(technique, JsonValueKind.Object, What);
        int[] bars = [.. Elements(Property(technique, "bars", JsonValueKind.Array), What).Select(static bar => bar.ValueKind == JsonValueKind.Number && bar.TryGetInt32(out int number) ? number : throw Damaged(What))];
        return new DraftTechnique(LowerName<TechniqueKind>(technique, "kind"), bars);
    }

    private static List<string> ReadIds(JsonElement step, string name) =>
        step.TryGetProperty(name, out JsonElement ids)
            ? [.. Elements(ids, What).Select(static id => id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } text ? text : throw Damaged(What))]
            : [];
}