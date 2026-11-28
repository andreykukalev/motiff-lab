using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Projects.Playlists;

namespace MotiffLab.Api;

/// <summary>
/// The player's playlists (version 1.7, the 1.7 plan's question 2): one document per player, the playlists file as
/// music-core writes it (docs/playlists-file.md), kept beside their projects and erased with them (<c>DELETE /me</c>, the
/// test reset). The apps merge it themselves (<c>playlists.resolve</c>): a save with <c>If-Match</c> replaces only the copy
/// that still has that ETag (<c>If-Match: *</c>: any copy kept), a save without it only creates the document, and both
/// answer 412 otherwise, so the app fetches, merges and sends again, as it does a project. The API checks only the
/// envelope (<see cref="PlaylistsEnvelope"/>: the format, the version and the oldest reader) and the size, never the
/// playlists, so it keeps a file of a later version than its own exactly as it was sent. On every plan: sync between the
/// workspaces is free (0025, section 8), and the one document needs no quota of its own.
/// </summary>
internal static class PlaylistsEndpoints
{
    /// <summary>A playlists file above this size is refused: thousands of pieces on dozens of playlists take a few hundred kilobytes, and it packs to far less than a document holds.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    public static void MapPlaylists(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder playlists = app.MapGroup("/playlists").RequireAuthorization(Player.Policy);
        bool resets = TestReset.IsEnabled(app.ServiceProvider.GetRequiredService<IConfiguration>());

        // The file as sent, its ETag and, for the merge's "copy saved last", when it was saved (Last-Modified).
        playlists.MapGet("/", async (HttpContext http, IProjectStore store, CancellationToken cancel) =>
        {
            if (await store.GetPlaylistsAsync(Player.Id(http.User), cancel) is not { } stored)
            {
                return ProjectEndpoints.Problem(StatusCodes.Status404NotFound, "There are no playlists online yet.");
            }

            http.Response.Headers.ETag = stored.ETag;
            http.Response.GetTypedHeaders().LastModified = stored.SavedAt;
            return Results.Bytes(ProjectEndpoints.Unpack(stored.PackedFile), "application/json");
        });

        playlists.MapPut("/", (HttpContext http, IProjectStore store, CancellationToken cancel) => SaveAsync(http, store, resets, cancel));
    }

    /// <summary>Keeps the playlists file sent: over the stored one when <c>If-Match</c> still names it, else only as a new one.</summary>
    private static async Task<IResult> SaveAsync(HttpContext http, IProjectStore store, bool resets, CancellationToken cancel)
    {
        if (await TestReset.IsStaleAsync(http, store, resets, cancel))
        {
            return ProjectEndpoints.Problem(StatusCodes.Status409Conflict, "Everything was cleared for a new testing cycle since this device last looked; it clears its own library first.", "reset");
        }

        if (await ProjectEndpoints.ReadBodyAsync(http.Request, MaxFileBytes, cancel) is not { } file)
        {
            return ProjectEndpoints.Problem(StatusCodes.Status413PayloadTooLarge, $"The playlists are larger than {MaxFileBytes / 1024} KB.");
        }

        try
        {
            PlaylistsEnvelope.Read(file);
        }
        catch (ProjectFileException error)
        {
            return ProjectEndpoints.Problem(StatusCodes.Status400BadRequest, error.Message);
        }

        string owner = Player.Id(http.User);
        try
        {
            string? ifMatch = await ProjectEndpoints.IfMatchAsync(http.Request, async () => (await store.GetPlaylistsAsync(owner, cancel))?.ETag);
            http.Response.Headers.ETag = await store.PutPlaylistsAsync(owner, ProjectEndpoints.Pack(file), ifMatch, cancel);
            return Results.NoContent();
        }
        catch (ConflictException)
        {
            return ProjectEndpoints.Problem(StatusCodes.Status412PreconditionFailed, "The playlists changed online since this device read them.");
        }
    }
}