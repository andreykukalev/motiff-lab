using System.Globalization;
using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The sync calls on piece 0 (contract 12, calls added; <see cref="ProjectSync"/>), for the iPad, which does not read
/// project files itself (0026 S1): project.online, project.sync, project.pulled and project.resolve. Files travel as their
/// text, as project.metadata has them.
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

    /// <summary>{"here", "online", "known", "changedHere", "changedOnline", "deletedOnline"} (true or false) → {"step"}.</summary>
    public static string Next(Request fields)
    {
        SyncStep step = ProjectSync.Next(new SyncFacts(
            Flag(fields, "here"), Flag(fields, "online"), Flag(fields, "known"),
            Flag(fields, "changedHere"), Flag(fields, "changedOnline"), Flag(fields, "deletedOnline")));
        return Answer.Write(json => json.WriteString("step", step switch
        {
            SyncStep.Nothing => "nothing",
            SyncStep.Upload => "upload",
            SyncStep.Download => "download",
            SyncStep.Resolve => "resolve",
            SyncStep.Forget => "forget",
            _ => throw new InvalidOperationException(),
        }));
    }

    /// <summary>{"file": the online copy, "here"?: this device's} → {"file": what to keep here, with this device's PDF}.</summary>
    public static string Pulled(Request fields)
    {
        Project online = Read(fields.Required("file"));
        Project? here = fields.Text("here") is { } text ? Read(text) : null;
        return Answer.Write(json => json.WriteString("file", Text(ProjectSync.Pulled(online, here, HoldingDevice.Ipad))));
    }

    /// <summary>
    /// {"here", "savedHere", "online", "savedOnline" (ISO 8601), "copyTitle"} → {"file": what to keep here, "upload": true
    /// when it goes online, "copy": the previous copy as a piece of its own, or null}.
    /// </summary>
    public static string Resolve(Request fields)
    {
        SyncResolution resolved = ProjectSync.Resolve(
            Read(fields.Required("here")), Time(fields, "savedHere"),
            Read(fields.Required("online")), Time(fields, "savedOnline"),
            fields.Required("copyTitle"), MusicCoreFacade.NewProjectId(), HoldingDevice.Ipad);
        return Answer.Write(json =>
        {
            json.WriteString("file", Text(resolved.Kept));
            json.WriteBoolean("upload", resolved.Upload);
            if (resolved.PreviousCopy is { } copy)
            {
                json.WriteString("copy", Text(copy));
            }
            else
            {
                json.WriteNull("copy");
            }
        });
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
    /// that join a keeper are the same piece with nothing of their own: the app deletes them.
    /// </summary>
    public static string Join(Request fields)
    {
        IReadOnlyList<Project> projects = [.. fields.Texts("files").Select(Read)];
        HashSet<int> stay = [.. fields.Elements("stay").Select(static element => element.TryGetInt32(out int index) ? index : throw FacadeException.BadRequest("\"stay\" holds indices."))];
        IReadOnlyList<JoinedPiece> joined = SamePiece.Plan(projects, stay, HoldingDevice.Ipad);
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

    private static bool Flag(Request fields, string name) => fields.Bool(name) ?? false;

    private static DateTimeOffset Time(Request fields, string name) =>
        DateTimeOffset.TryParse(fields.Required(name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset time)
            ? time
            : throw FacadeException.BadRequest($"\"{name}\" is a time in ISO 8601.");
}