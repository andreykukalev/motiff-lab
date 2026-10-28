using System.IO.Compression;
using System.Text.RegularExpressions;

using MotiffLab.MusicCore.Projects;

namespace MotiffLab.Api;

/// <summary>
/// The player's projects: list, read, save and delete one, and delete everything (the right to erasure).
/// A project travels as its project file (<see cref="ProjectFile"/>), the same bytes the app autosaves and exports.
/// </summary>
internal static partial class ProjectEndpoints
{
    /// <summary>A project file above this size is refused; its source is most of it, and few scores come near.</summary>
    public const int MaxFileBytes = 16 * 1024 * 1024;

    /// <summary>Packed files above this do not fit a Cosmos DB document (2 MB with the summary beside them).</summary>
    public const int MaxPackedChars = 1_900_000;

    public static void MapProjects(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder projects = app.MapGroup("/projects").RequireAuthorization(Player.Policy);

        projects.MapGet("/", async (HttpContext http, IProjectStore store, CancellationToken cancel) =>
            Results.Ok(await store.ListAsync(Player.Id(http.User), cancel)));

        projects.MapGet("/{id}", async (string id, HttpContext http, IProjectStore store, CancellationToken cancel) =>
        {
            if (!IsProjectId(id) || await store.GetAsync(Player.Id(http.User), id, cancel) is not { } stored)
            {
                return Problem(StatusCodes.Status404NotFound, "There is no such project.");
            }

            http.Response.Headers.ETag = stored.ETag;
            return Results.Bytes(Unpack(stored.PackedFile), "application/json");
        });

        projects.MapPut("/{id}", async (string id, HttpContext http, IProjectStore store, CancellationToken cancel) =>
        {
            if (!IsProjectId(id))
            {
                return Problem(StatusCodes.Status400BadRequest, "A project id has letters, digits, - and _ only.");
            }

            byte[]? file = await ReadBodyAsync(http.Request, cancel);
            if (file is null)
            {
                return Problem(StatusCodes.Status413PayloadTooLarge, $"The project is larger than {MaxFileBytes / (1024 * 1024)} MB.");
            }

            Project project;
            try
            {
                project = ProjectFile.Read(file);
            }
            catch (ProjectFileException error)
            {
                return Problem(StatusCodes.Status400BadRequest, error.Message);
            }

            if (project.Id != id)
            {
                return Problem(StatusCodes.Status400BadRequest, "The project file belongs to another project.");
            }

            string packed = Pack(file);
            if (packed.Length > MaxPackedChars)
            {
                return Problem(StatusCodes.Status413PayloadTooLarge, "The project is too large to save online yet; it stays on this device.");
            }

            string? ifMatch = http.Request.Headers.IfMatch.Count > 0 ? http.Request.Headers.IfMatch.ToString() : null;
            try
            {
                string etag = await store.PutAsync(Player.Id(http.User), project.Summary, packed, ifMatch, cancel);
                http.Response.Headers.ETag = etag;
                return Results.NoContent();
            }
            catch (ConflictException error)
            {
                return Problem(StatusCodes.Status412PreconditionFailed, error.Message);
            }
        });

        projects.MapDelete("/{id}", async (string id, HttpContext http, IProjectStore store, CancellationToken cancel) =>
            IsProjectId(id) && await store.DeleteAsync(Player.Id(http.User), id, cancel)
                ? Results.NoContent()
                : Problem(StatusCodes.Status404NotFound, "There is no such project."));

        // Everything the player keeps here goes; the account itself is the tenant's, deleted there.
        app.MapDelete("/me", async (HttpContext http, IProjectStore store, CancellationToken cancel) =>
            Results.Ok(new { deletedProjects = await store.DeleteAllAsync(Player.Id(http.User), cancel) }))
            .RequireAuthorization(Player.Policy);
    }

    /// <summary>The project file, gzipped and in base64, so a typical score fits one document.</summary>
    internal static string Pack(byte[] file)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(file);
        }

        return Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    internal static byte[] Unpack(string packed)
    {
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(packed)), CompressionMode.Decompress);
        using var file = new MemoryStream();
        gzip.CopyTo(file);
        return file.ToArray();
    }

    // Project ids are made by the app (p_…); anything else could not be a Cosmos DB id.
    internal static bool IsProjectId(string id) => ProjectId().IsMatch(id);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectId();

    /// <summary>The body, or null when it is larger than <see cref="MaxFileBytes"/>.</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancel)
    {
        if (request.ContentLength > MaxFileBytes)
        {
            return null;
        }

        using var body = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancel)) > 0)
        {
            if (body.Length + read > MaxFileBytes)
            {
                return null;
            }

            body.Write(chunk, 0, read);
        }

        return body.ToArray();
    }

    private static IResult Problem(int status, string message) =>
        Results.Json(new { message }, statusCode: status, contentType: "application/json");
}