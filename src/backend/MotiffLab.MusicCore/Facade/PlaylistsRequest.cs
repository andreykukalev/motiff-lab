using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Playlists;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The library's playlists on piece 0 (version 1.7, the score-reading spec 6, the 1.7 plan's lead decision 3;
/// <see cref="LibraryPlaylists"/>, docs/playlists-file.md): read, each edit, the move from the pieces' playlists, and the
/// merge of two copies for the sync. The playlists file travels as its text in "file", as a project file does; no "file"
/// is no playlists yet. Each answer but playlists.resolve's is {"file": the file to keep, "readOnly", "playlists": [{"id",
/// "name", "pieces"}]}; an edit of playlists a later build saved answers <c>readOnly</c>, one naming a playlist there is
/// not answers <c>notFound</c>, and an empty name <c>badRequest</c>.
/// </summary>
internal static class PlaylistsRequest
{
    public static readonly FacadeCall<MusicCoreFacade>[] Calls =
    [
        new("playlists", static (_, fields) => Read(fields)),
        new("playlists.create", static (facade, fields) => Create(fields, facade.Ids)),
        new("playlists.rename", static (_, fields) => Rename(fields)),
        new("playlists.delete", static (_, fields) => Edit(fields, (playlists, id) => playlists.Deleted(id))),
        new("playlists.add", static (_, fields) => Edit(fields, (playlists, id) => playlists.Added(id, fields.Texts("pieces")))),
        new("playlists.remove", static (_, fields) => Edit(fields, (playlists, id) => playlists.Removed(id, fields.Texts("pieces")))),
        new("playlists.move", static (_, fields) => Edit(fields, (playlists, id) => playlists.Moved(id, fields.Texts("pieces"), fields.Text("before")))),
        new("playlists.forget", static (_, fields) => Answered(In(fields).Forgotten(fields.Texts("pieces")), saved: true)),
        new("playlists.rekey", static (_, fields) => Answered(In(fields).Rekeyed(Current(fields)), saved: true)),
        new("playlists.migrate", static (_, fields) => Answered(LibraryPlaylists.Migrated(Pieces(fields)), saved: true)),
        new("playlists.resolve", static (_, fields) => Resolve(fields)),
    ];

    /// <summary>{"file"?} → the playlists as the file holds them, by the rules (<see cref="PlaylistsFile.Read"/>), and the file written so.</summary>
    private static string Read(Request fields) => Answered(In(fields), saved: false);

    /// <summary>
    /// {"file"?, "name", "pieces"?: project ids} → also "id", the new playlist's, and "taken": null, or, when a playlist has
    /// the name already, "id" null and "taken" its id, nothing changed (the spec's «то же имя → предлагаем открыть существующий»).
    /// </summary>
    private static string Create(Request fields, IdSource ids)
    {
        PlaylistChange change = In(fields).Created(fields.Required("name"), fields.Texts("pieces"), ids);
        return Answered(change.Playlists, saved: true, change);
    }

    /// <summary>{"file", "id", "name"} → also "id" and "taken", as playlists.create: a name another playlist has changes nothing.</summary>
    private static string Rename(Request fields)
    {
        LibraryPlaylists playlists = In(fields);
        string id = Named(playlists, fields);
        PlaylistChange change = playlists.Renamed(id, fields.Required("name"));
        return Answered(change.Playlists, saved: true, change);
    }

    /// <summary>{"file", "id", …}: the playlist "id" names, edited.</summary>
    private static string Edit(Request fields, Func<LibraryPlaylists, string, LibraryPlaylists> edit)
    {
        LibraryPlaylists playlists = In(fields);
        return Answered(edit(playlists, Named(playlists, fields)), saved: true);
    }

    /// <summary>
    /// {"here", "savedHere", "online", "savedOnline" (ISO 8601), "base"?: the copy both last agreed on} → {"file": what to
    /// keep here, "upload": true when it goes online, "merged": false when a later build's copy was kept as it came, "readOnly",
    /// "conflicts": [{"in", "id"}]} (<see cref="PlaylistsMerge.Resolve"/>).
    /// </summary>
    private static string Resolve(Request fields)
    {
        PlaylistsResolution resolved = PlaylistsMerge.Resolve(
            File(fields.Required("here")), SyncRequest.Time(fields, "savedHere"), File(fields.Required("online")), SyncRequest.Time(fields, "savedOnline"),
            fields.Text("base") is { } shared ? File(shared) : null);
        return Answer.Write(json =>
        {
            json.WriteString("file", Encoding.UTF8.GetString(PlaylistsFile.Write(resolved.Kept)));
            json.WriteBoolean("upload", resolved.Upload);
            json.WriteBoolean("merged", resolved.Merged);
            json.WriteBoolean("readOnly", resolved.Kept.IsReadOnly);
            SyncRequest.Conflicts(json, resolved.Conflicts);
        });
    }

    /// <summary>The playlists and the file to keep: saved (refused when read-only) after an edit, written as they came when only read.</summary>
    private static string Answered(LibraryPlaylists playlists, bool saved, PlaylistChange? change = null)
    {
        string file = Encoding.UTF8.GetString(saved ? PlaylistsFile.Save(playlists) : PlaylistsFile.Write(playlists));
        return Answer.Write(json =>
        {
            json.WriteString("file", file);
            json.WriteBoolean("readOnly", playlists.IsReadOnly);
            json.WriteStartArray("playlists");
            foreach (Playlist playlist in playlists.All)
            {
                json.WriteStartObject();
                json.WriteString("id", playlist.Id);
                json.WriteString("name", playlist.Name);
                JsonWrite.Texts(json, "pieces", playlist.Pieces);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            if (change is not null)
            {
                json.WriteString("id", change.Id);
                json.WriteString("taken", change.Taken);
            }
        });
    }

    private static LibraryPlaylists In(Request fields) => fields.Text("file") is { } file ? File(file) : LibraryPlaylists.None;

    private static LibraryPlaylists File(string text) => PlaylistsFile.Read(Encoding.UTF8.GetBytes(text));

    private static string Named(LibraryPlaylists playlists, Request fields)
    {
        string id = fields.Required("id");
        return playlists.Find(id) is not null ? id : throw new FacadeException("notFound", $"There is no playlist \"{id}\".");
    }

    /// <summary>"pieces": [{"id", "formerIds"}] → each former id to the id now.</summary>
    private static Dictionary<string, string> Current(Request fields)
    {
        Dictionary<string, string> current = new(StringComparer.Ordinal);
        foreach (JsonElement piece in fields.Elements("pieces"))
        {
            using Request row = Request.Of(piece);
            string id = row.Required("id");
            foreach (string former in row.Texts("formerIds"))
            {
                current[former] = id;
            }
        }

        return current;
    }

    /// <summary>"pieces": [{"id", "playlists": names}] in the library's order.</summary>
    private static List<(string Piece, IReadOnlyList<string> Playlists)> Pieces(Request fields)
    {
        List<(string, IReadOnlyList<string>)> pieces = [];
        foreach (JsonElement piece in fields.Elements("pieces"))
        {
            using Request row = Request.Of(piece);
            pieces.Add((row.Required("id"), row.Texts("playlists")));
        }

        return pieces;
    }
}