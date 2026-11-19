using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The piece's sources by the layers of 0030 (version 0.6, step 5), for the Sources sheet and the switch of views on the
/// web and the iPad alike; the answer of "sources" stays as it was beside it.
/// </summary>
internal static partial class Answer
{
    /// <summary>
    /// "working" (the working notes' id, null with none), "originals": [{"id", "kind", "shown", "fileName", "name",
    /// "state" ("here" / "onAnotherDevice" / "nowhere" / "removed"), "removedAt", "kept", "pages", "durationMs", "hidden",
    /// "notes": [ids made from it], "pageMarks"}], "notes": [{"id", "role" ("working" / "other" / "version"), "name", "op",
    /// "engine", "at", "original", "state" (as an original's, or "fileChanged"; null with no original), "edits", "taken",
    /// "hidden"}], "views": [{"id", "kind" ("original" / "notes" / "performance"), "group", "working", "hidden", "op"}] in
    /// the switch's order, and "hidden", how many views are hidden (<see cref="PieceViews"/>).
    /// </summary>
    public static void SourceLayers(Utf8JsonWriter json, Project project, PieceViews views)
    {
        if (project.HasNotes)
        {
            json.WriteString("working", project.NotesId);
        }
        else
        {
            json.WriteNull("working");
        }

        json.WriteStartArray("originals");
        foreach (SourceOriginal original in views.Originals)
        {
            SourceOriginal(json, original);
        }

        json.WriteEndArray();
        json.WriteStartArray("notes");
        foreach (SourceNotes notes in views.Notes)
        {
            SourceNotes(json, notes);
        }

        json.WriteEndArray();
        json.WriteStartArray("views");
        foreach (SourceView view in views.Views)
        {
            json.WriteStartObject();
            json.WriteString("id", view.Id);
            json.WriteString("kind", Camel(view.Kind));
            OptionalText(json, "group", view.Group);
            json.WriteBoolean("working", view.Working);
            json.WriteBoolean("hidden", view.Hidden);
            json.WriteString("op", view.Operation);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("hidden", views.HiddenCount);
    }

    private static void SourceOriginal(Utf8JsonWriter json, SourceOriginal source)
    {
        PieceOriginal original = source.Original;
        json.WriteStartObject();
        json.WriteString("id", original.Id);
        json.WriteString("kind", original.Kind.ToString().ToLowerInvariant());
        json.WriteString("shown", original.Shown);
        json.WriteString("fileName", original.FileName);
        OptionalText(json, "name", original.Name);
        json.WriteString("state", Camel(source.State));
        OptionalText(json, "removedAt", source.RemovedAt?.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("kept", original.Kept.ToString().ToLowerInvariant());
        OptionalNumber(json, "pages", original.Pages);
        OptionalNumber(json, "durationMs", original.DurationMs);
        json.WriteBoolean("hidden", source.Hidden);
        json.WriteStartArray("notes");
        foreach (string id in source.Notes)
        {
            json.WriteStringValue(id);
        }

        json.WriteEndArray();
        json.WriteNumber("pageMarks", source.PageMarks);
        json.WriteEndObject();
    }

    private static void SourceNotes(Utf8JsonWriter json, SourceNotes notes)
    {
        json.WriteStartObject();
        json.WriteString("id", notes.Id);
        json.WriteString("role", Camel(notes.Role));
        OptionalText(json, "name", notes.Name);
        json.WriteString("op", notes.Provenance.Op);
        OptionalText(json, "engine", notes.Provenance.Engine);
        OptionalText(json, "at", notes.Provenance.At?.ToString("O", CultureInfo.InvariantCulture));
        OptionalText(json, "original", notes.Original);
        OptionalText(json, "state", notes.State is { } state ? Camel(state) : null);
        json.WriteNumber("edits", notes.Edits);
        json.WriteNumber("taken", notes.Taken);
        json.WriteBoolean("hidden", notes.Hidden);
        json.WriteEndObject();
    }

    /// <summary>An enum's name in lower camel case, as the facade's answers name values.</summary>
    private static string Camel<T>(T value)
        where T : struct, Enum
    {
        string name = value.ToString();
        return string.Concat(char.ToLowerInvariant(name[0]).ToString(), name.AsSpan(1));
    }
}