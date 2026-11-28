using System.Globalization;
using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The sync calls on piece 0 (contract 12, calls added; <see cref="ProjectSync"/>), for the iPad, which does not read
/// project files itself (0026 S1): project.online, project.sync, project.pulled and project.resolve. Files travel as their
/// text, as project.metadata has them. A copy a later build saved (<see cref="Project.IsReadOnly"/>, version 1.6) is kept
/// here as it came, read-only: project.pulled and project.resolve answer it with "readOnly": true, and project.online refuses
/// it (<c>readOnly</c>), so this build never sends it over the online copy.
/// </summary>
internal static class SyncRequest
{
    /// <summary>
    /// {"file": text, "now"?: ISO 8601} → {"file": the copy saved online}: without the PDF, and since version 0.5 also for a
    /// piece that is only a PDF (before, null for one). The PDF the iPad keeps in the piece is marked held by the iPad in the
    /// copy (version 0.3), so the web shows where it is.
    /// </summary>
    public static string Online(Request fields)
    {
        Project project = Read(fields.Required("file"));
        if (project.Document is not null && project.FindOriginal(PieceOriginal.DocumentId) is not null)
        {
            DateTimeOffset now = fields.Text("now") is not null ? Time(fields, "now") : DateTimeOffset.UtcNow;
            project = project.Fingerprinted().MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Ipad, held: true, now);
        }

        Project? copy = project.ForOnline();
        return Answer.Write(json =>
        {
            if (copy is null)
            {
                json.WriteNull("file");
            }
            else
            {
                json.WriteString("file", Text(copy));
            }
        });
    }

    /// <summary>
    /// {"here", "online", "known", "changedHere", "changedOnline", "deletedOnline", "deletedHere"} (true or false) → {"step"}:
    /// "nothing", "upload", "download", "resolve", "forget" or, for a piece the player removed here, "deleteOnline".
    /// </summary>
    public static string Next(Request fields)
    {
        SyncStep step = ProjectSync.Next(new SyncFacts(
            Flag(fields, "here"), Flag(fields, "online"), Flag(fields, "known"),
            Flag(fields, "changedHere"), Flag(fields, "changedOnline"), Flag(fields, "deletedOnline"), Flag(fields, "deletedHere")));
        return Answer.Write(json => json.WriteString("step", step switch
        {
            SyncStep.Nothing => "nothing",
            SyncStep.Upload => "upload",
            SyncStep.Download => "download",
            SyncStep.Resolve => "resolve",
            SyncStep.Forget => "forget",
            SyncStep.DeleteOnline => "deleteOnline",
            _ => throw new InvalidOperationException(),
        }));
    }

    /// <summary>
    /// {"file": the online copy, "here"?: this device's} → {"file": what to keep here, with this device's PDF, "readOnly": true
    /// when a later build saved it}.
    /// </summary>
    public static string Pulled(Request fields)
    {
        Project online = Read(fields.Required("file"));
        Project? here = fields.Text("here") is { } text ? Read(text) : null;
        Project kept = ProjectSync.Pulled(online, here, HoldingDevice.Ipad);
        return Answer.Write(json =>
        {
            json.WriteString("file", Kept(kept));
            json.WriteBoolean("readOnly", kept.IsReadOnly);
        });
    }

    /// <summary>
    /// {"here", "savedHere", "online", "savedOnline" (ISO 8601), "copyTitle", "base"?: the copy both last agreed on,
    /// "sendsFile"?: true when the app sends "file" on "upload", false (before c-17b) when it sends "here"} →
    /// {"file": what to keep here, "upload": true when "file" ("here" without "sendsFile") goes online, "copy": the previous copy as a piece of its own, or
    /// null, "readOnly": true when "file" holds a later build's copy (kept here, never sent), "merged": false when the copies
    /// were not merged entity by entity (the merge failed its self-check, or one is a later build's: "file" is then the online
    /// copy and "copy" this device's), "conflicts": [{"in", "id"}] what of the copy that lost "file" does not hold}
    /// (<see cref="ProjectSync.Resolve"/>, version 1.6).
    /// </summary>
    public static string Resolve(Request fields)
    {
        SyncResolution resolved = ProjectSync.Resolve(
            Read(fields.Required("here")), Time(fields, "savedHere"),
            Read(fields.Required("online")), Time(fields, "savedOnline"),
            fields.Required("copyTitle"), MusicCoreFacade.NewProjectId(), HoldingDevice.Ipad,
            fields.Text("base") is { } shared ? Read(shared) : null, Flag(fields, "sendsFile"));
        return Answer.Write(json =>
        {
            json.WriteString("file", Kept(resolved.Kept));
            json.WriteBoolean("upload", resolved.Upload);
            if (resolved.PreviousCopy is { } copy)
            {
                json.WriteString("copy", Kept(copy));
            }
            else
            {
                json.WriteNull("copy");
            }

            json.WriteBoolean("readOnly", resolved.Kept.IsReadOnly);
            json.WriteBoolean("merged", resolved.Merged);
            Conflicts(json, resolved.Conflicts);
        });
    }

    /// <summary>"conflicts": [{"in", "id"}], what of the copy that lost a merge the copy kept does not hold (project.resolve, playlists.resolve).</summary>
    internal static void Conflicts(Utf8JsonWriter json, IEnumerable<MergeConflict> conflicts)
    {
        json.WriteStartArray("conflicts");
        foreach (MergeConflict conflict in conflicts)
        {
            json.WriteStartObject();
            json.WriteString("in", conflict.In);
            json.WriteString("id", conflict.Id);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>
    /// {"pieces": [{"id", "keys": the project summary's "pieceKeys"}]} → {"groups": [[ids]]}: the projects that may be one
    /// piece, to read and send to project.join (version 0.6.1, #582 item 1; <see cref="SamePiece.Candidates"/>).
    /// </summary>
    public static string Same(Request fields)
    {
        List<(string Id, IReadOnlyList<string> Keys)> rows = [];
        foreach (JsonElement piece in fields.Elements("pieces"))
        {
            using Request row = Request.Of(piece);
            rows.Add((row.Required("id"), row.Texts("keys")));
        }

        IReadOnlyList<IReadOnlyList<string>> groups = SamePiece.Candidates(rows);
        return Answer.Write(json =>
        {
            json.WriteStartArray("groups");
            foreach (IReadOnlyList<string> group in groups)
            {
                JsonWrite.Texts(json, null, group);
            }

            json.WriteEndArray();
        });
    }

    /// <summary>
    /// {"files": [project file texts that may be one piece], "stay"?: [indices of the files whose bytes are linked on this
    /// iPad, never joined away]} → {"joined": [{"keeper": index, "from": [indices that join it], "file": the keeper to save in
    /// its place}]}, only the pieces some file joined (version 0.6.1, #582 item 1; <see cref="SamePiece.Plan"/>). The files
    /// that join a keeper are the same piece with nothing of their own: the app deletes them. A file a later build saved
    /// (version 1.6) is left out: this build neither writes it nor joins it away, and the others are joined as without it.
    /// </summary>
    public static string Join(Request fields)
    {
        IReadOnlyList<Project> all = [.. fields.Texts("files").Select(Read)];
        int[] writable = [.. Enumerable.Range(0, all.Count).Where(index => !all[index].IsReadOnly)];
        HashSet<int> stayed = [.. fields.Elements("stay").Select(static element => element.TryGetInt32(out int index) ? index : throw FacadeException.BadRequest("\"stay\" holds indices."))];
        HashSet<int> stay = [.. Enumerable.Range(0, writable.Length).Where(index => stayed.Contains(writable[index]))];
        IReadOnlyList<JoinedPiece> joined = [.. SamePiece.Plan([.. writable.Select(index => all[index])], stay, HoldingDevice.Ipad)
            .Select(piece => piece with { Keeper = writable[piece.Keeper], Joined = [.. piece.Joined.Select(index => writable[index])] })];
        return Answer.Write(json =>
        {
            json.WriteStartArray("joined");
            foreach (JoinedPiece piece in joined)
            {
                json.WriteStartObject();
                json.WriteNumber("keeper", piece.Keeper);
                JsonWrite.Numbers(json, "from", piece.Joined);
                json.WriteString("file", Text(piece.Project));
                json.WriteEndObject();
            }

            json.WriteEndArray();
        });
    }

    private static Project Read(string file) => ProjectFile.Read(Encoding.UTF8.GetBytes(file));

    private static string Text(Project project) => Encoding.UTF8.GetString(ProjectFile.Save(project));

    /// <summary>The file to keep here: as <see cref="Text"/>, and a later build's copy as it came, read-only (<see cref="ProjectFile.Write"/>).</summary>
    private static string Kept(Project project) => project.IsReadOnly ? Encoding.UTF8.GetString(ProjectFile.Write(project)) : Text(project);

    private static bool Flag(Request fields, string name) => fields.Bool(name) ?? false;

    internal static DateTimeOffset Time(Request fields, string name) =>
        DateTimeOffset.TryParse(fields.Required(name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset time)
            ? time
            : throw FacadeException.BadRequest($"\"{name}\" is a time in ISO 8601.");
}