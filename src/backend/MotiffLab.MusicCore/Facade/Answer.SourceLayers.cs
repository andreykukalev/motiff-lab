using System.Globalization;
using System.Text.Json;

using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Drafts;

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
    /// the switch's order, "hidden", how many views are hidden (<see cref="PieceViews"/>), and since version 1.1 (0034) "drafts"
    /// (<see cref="Drafts"/>), the Sources group «Черновики»; a draft is a view of kind "draft" too, last in the switch, hidden as any other.
    /// </summary>
    public static void SourceLayers(Utf8JsonWriter json, Project project, PieceViews views, Words words)
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
            json.WriteString("kind", JsonWrite.Name(view.Kind));
            OptionalText(json, "group", view.Group);
            json.WriteBoolean("working", view.Working);
            json.WriteBoolean("hidden", view.Hidden);
            json.WriteString("op", view.Operation);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("hidden", views.HiddenCount);
        Drafts(json, project, words);
    }

    /// <summary>
    /// "drafts": [{"id", "name", "caption" («из нот с PDF и записи»), "createdAt", "places", "shelf", and since version 1.1
    /// step 6 "techniques" {"taken", "total"}, the приёмы of all its places (<see cref="Draft.Tally"/>: «взято 3 приёма из
    /// 4»)}] in the order they were made, for the Sources group «Черновики» (0034, spec «Экран (iPad)»), and since version
    /// 1.2 for another piece's group on the «В черновик» sheet (project.drafts).
    /// </summary>
    public static void Drafts(Utf8JsonWriter json, Project project, Words words)
    {
        json.WriteStartArray("drafts");
        foreach (Draft draft in project.Drafts)
        {
            json.WriteStartObject();
            json.WriteString("id", draft.Id);
            json.WriteString("name", draft.Name);
            json.WriteString("caption", DraftLabels.Caption(draft, words));
            json.WriteString("createdAt", draft.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteNumber("places", draft.Places.Count);
            json.WriteNumber("shelf", draft.Shelf.Count);
            (int taken, int total) = draft.Tally();
            json.WriteStartObject("techniques");
            json.WriteNumber("taken", taken);
            json.WriteNumber("total", total);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        json.WriteEndArray();
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
        json.WriteString("state", JsonWrite.Name(source.State));
        OptionalText(json, "removedAt", source.RemovedAt?.ToString("O", CultureInfo.InvariantCulture));
        json.WriteString("kept", original.Kept.ToString().ToLowerInvariant());
        OptionalNumber(json, "pages", original.Pages);
        OptionalNumber(json, "durationMs", original.DurationMs);
        json.WriteBoolean("hidden", source.Hidden);
        JsonWrite.Texts(json, "notes", source.Notes);
        json.WriteNumber("pageMarks", source.PageMarks);
        json.WriteEndObject();
    }

    private static void SourceNotes(Utf8JsonWriter json, SourceNotes notes)
    {
        json.WriteStartObject();
        json.WriteString("id", notes.Id);
        json.WriteString("role", JsonWrite.Name(notes.Role));
        OptionalText(json, "name", notes.Name);
        json.WriteString("op", notes.Provenance.Op);
        OptionalText(json, "engine", notes.Provenance.Engine);
        OptionalText(json, "at", notes.Provenance.At?.ToString("O", CultureInfo.InvariantCulture));
        OptionalText(json, "original", notes.Original);
        OptionalText(json, "state", notes.State is { } state ? JsonWrite.Name(state) : null);
        json.WriteNumber("edits", notes.Edits);
        json.WriteNumber("taken", notes.Taken);
        json.WriteBoolean("hidden", notes.Hidden);
        json.WriteEndObject();
    }
}