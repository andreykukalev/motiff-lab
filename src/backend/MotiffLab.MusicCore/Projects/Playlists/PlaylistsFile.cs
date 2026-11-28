using System.Text.Json;

using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects.File;

namespace MotiffLab.MusicCore.Projects.Playlists;

/// <summary>
/// The playlists file (docs/playlists-file.md; version 1.7, the 1.7 plan's lead decision 3), read and written by hand as
/// the project file is (<see cref="ProjectFile"/>), so the same playlists give the same bytes: "format", "version",
/// "minReader" and "playlists", each with "id", "name" and "pieces". A field this build does not know, at the top or in a
/// playlist, is kept and written back after its own (0026 S6); a file of a later version whose "minReader" this build meets
/// reads read-only (<see cref="LibraryPlaylists.Newer"/>), one with a larger "minReader" is refused.
/// </summary>
internal static class PlaylistsFile
{
    public const string FormatName = "motiff-lab-playlists";

    /// <summary>The version this build writes: 1 (version 1.7).</summary>
    public const int Version = 1;

    /// <summary>The oldest build version that reads a file this build writes.</summary>
    public const int MinReader = 1;

    private static readonly HashSet<string> s_rootFields = new(StringComparer.Ordinal) { "format", "version", "minReader", "playlists" };
    private static readonly HashSet<string> s_playlistFields = new(StringComparer.Ordinal) { "id", "name", "pieces" };

    /// <summary>
    /// The playlists as a file of <see cref="Version"/>; ones read from a later version keep its "version" and "minReader",
    /// the larger of its own and this build's, so they read back read-only. Such a file is never kept (<see cref="Save"/>).
    /// </summary>
    public static byte[] Write(LibraryPlaylists playlists)
    {
        ArgumentNullException.ThrowIfNull(playlists);
        (int version, int minReader) = playlists.Newer is { } newer ? (Math.Max(newer.Version, Version), Math.Max(newer.MinReader, MinReader)) : (Version, MinReader);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("format", FormatName);
            json.WriteNumber("version", version);
            json.WriteNumber("minReader", minReader);
            json.WriteStartArray("playlists");
            foreach (Playlist playlist in playlists.All)
            {
                json.WriteStartObject();
                json.WriteString("id", playlist.Id);
                json.WriteString("name", playlist.Name);
                JsonWrite.Texts(json, "pieces", playlist.Pieces);
                UnknownEntities.WriteUnknownFields(json, playlist.Unknown);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            UnknownEntities.WriteUnknownFields(json, playlists.Unknown);
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>The file the playlists are kept in, on the device or online; ones read from a later version are never kept (0026 S6).</summary>
    /// <exception cref="ProjectFileException">They are read-only (<see cref="ProjectFileException.ReadOnly"/>).</exception>
    public static byte[] Save(LibraryPlaylists playlists)
    {
        ArgumentNullException.ThrowIfNull(playlists);
        return playlists.IsReadOnly ? throw new ProjectFileException("These playlists were saved by a newer version of Motiff Lab: they are read-only here and are not saved. Update the app to change them.")
        {
            RussianMessage = "Эти плейлисты сохранены более новой версией Motiff Lab: здесь они только для чтения и не сохраняются. Обновите приложение, чтобы менять их.",
            ReadOnly = true,
        } : Write(playlists);
    }

    /// <summary>
    /// The playlists a file holds: each name one line (<see cref="PieceMetadata.Clean"/>), each piece once on a playlist, and
    /// one playlist per name (<see cref="LibraryPlaylists.Joined"/>), so a file another writer made reads by the rules.
    /// </summary>
    /// <exception cref="ProjectFileException">Not a playlists file, of a later version this build may not read, or damaged.</exception>
    public static LibraryPlaylists Read(ReadOnlySpan<byte> data)
    {
        using JsonDocument document = Parse(data);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out JsonElement format) || format.ValueKind != JsonValueKind.String || format.GetString() != FormatName)
        {
            throw new ProjectFileException("This is not a Motiff Lab playlists file.");
        }

        int version = Whole(root, "version");
        int minReader = Whole(root, "minReader");
        if (minReader > Version)
        {
            throw new ProjectFileException("These playlists were saved by a newer version of Motiff Lab. Update the app and try again.")
            {
                RussianMessage = "Эти плейлисты сохранены более новой версией Motiff Lab. Обновите приложение и попробуйте ещё раз.",
            };
        }

        if (!root.TryGetProperty("playlists", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            throw Damaged("playlists");
        }

        List<Playlist> all = [.. list.EnumerateArray().Select(ReadPlaylist)];
        if (all.DistinctBy(static playlist => playlist.Id, StringComparer.Ordinal).Count() != all.Count)
        {
            throw Damaged("ids");
        }

        return new LibraryPlaylists
        {
            All = all,
            Unknown = UnknownEntities.UnknownFieldsOf(root, s_rootFields),
            Newer = version > Version ? new NewerFile(version, minReader) : null,
        }.Joined();
    }

    private static Playlist ReadPlaylist(JsonElement playlist)
    {
        if (playlist.ValueKind != JsonValueKind.Object)
        {
            throw Damaged("playlists");
        }

        string id = Text(playlist, "id") is { Length: > 0 } text ? text : throw Damaged("ids");
        string name = PieceMetadata.Clean(Text(playlist, "name")) ?? throw Damaged("names");
        if (!playlist.TryGetProperty("pieces", out JsonElement pieces) || pieces.ValueKind != JsonValueKind.Array
            || pieces.EnumerateArray().Any(static piece => piece.ValueKind != JsonValueKind.String))
        {
            throw Damaged("pieces");
        }

        return new Playlist(id, name, LibraryPlaylists.Distinct(pieces.EnumerateArray().Select(static piece => piece.GetString()!)))
        {
            Unknown = UnknownEntities.UnknownFieldsOf(playlist, s_playlistFields),
        };
    }

    private static JsonDocument Parse(ReadOnlySpan<byte> data)
    {
        try
        {
            return JsonDocument.Parse(data.ToArray());
        }
        catch (JsonException)
        {
            throw new ProjectFileException("This is not a Motiff Lab playlists file: it is not valid JSON.");
        }
    }

    private static string Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Damaged(name);

    /// <summary>A whole number of 1 or more, as "version" and "minReader" are.</summary>
    private static int Whole(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number >= 1
            ? number
            : throw Damaged(name);

    private static ProjectFileException Damaged(string what) => new($"This playlists file is damaged: its {what} cannot be read.");
}