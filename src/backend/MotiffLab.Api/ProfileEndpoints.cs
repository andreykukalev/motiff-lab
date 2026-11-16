using System.Text.Json;

namespace MotiffLab.Api;

/// <summary>
/// The player's profile (Andrei, 2026-11-06): the name and photo they chose to be shown in the apps, kept with the account
/// on any plan, beside their projects and erased with them. Without one the apps show the name the sign-in gives.
/// </summary>
internal static class ProfileEndpoints
{
    /// <summary>The longest name kept, in characters.</summary>
    public const int MaxName = 60;

    /// <summary>The largest photo kept, in bytes: the apps send a 256-pixel square, a few tens of kilobytes.</summary>
    public const int MaxPhotoBytes = 200 * 1024;

    private static readonly (string Prefix, byte[] Magic)[] s_images =
    [
        ("data:image/jpeg;base64,", [0xFF, 0xD8, 0xFF]),
        ("data:image/png;base64,", [0x89, 0x50, 0x4E, 0x47]),
        ("data:image/webp;base64,", [0x52, 0x49, 0x46, 0x46]),
    ];

    public static void MapProfile(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder profile = app.MapGroup("/profile").RequireAuthorization(Player.Policy);

        // {"name": "…" | null, "photo": "data:image/jpeg;base64,…" | null}
        profile.MapGet("/", async (HttpContext http, IProjectStore store, CancellationToken cancel) =>
            Results.Ok(await store.GetProfileAsync(Player.Id(http.User), cancel) ?? new StoredProfile(null, null)));

        profile.MapPut("/", async (HttpContext http, IProjectStore store, CancellationToken cancel) =>
        {
            ProfileBody? body;
            try
            {
                body = await http.Request.ReadFromJsonAsync<ProfileBody>(cancel);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            {
                return ProjectEndpoints.Problem(StatusCodes.Status400BadRequest, "The profile is JSON with a name and a photo.");
            }

            if (Check(body?.Name, body?.Photo) is { } problem)
            {
                return ProjectEndpoints.Problem(problem.Status, problem.Detail);
            }

            var kept = new StoredProfile(Name(body?.Name), string.IsNullOrEmpty(body?.Photo) ? null : body.Photo);
            await store.PutProfileAsync(Player.Id(http.User), kept, cancel);
            return Results.Ok(kept);
        });
    }

    /// <summary>The name as kept: trimmed, null when nothing is left.</summary>
    internal static string? Name(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    /// <summary>Why the profile cannot be kept, or null when it can.</summary>
    internal static (int Status, string Detail)? Check(string? name, string? photo)
    {
        string? kept = Name(name);
        if (kept is not null && (kept.Length > MaxName || kept.Any(char.IsControl)))
        {
            return (StatusCodes.Status400BadRequest, $"A name is up to {MaxName} characters on one line.");
        }

        if (string.IsNullOrEmpty(photo))
        {
            return null;
        }

        foreach ((string prefix, byte[] magic) in s_images)
        {
            if (!photo.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (photo.Length - prefix.Length > ((MaxPhotoBytes + 2) / 3 * 4))
            {
                return (StatusCodes.Status413PayloadTooLarge, $"A photo is up to {MaxPhotoBytes / 1024} KB.");
            }

            byte[] bytes = new byte[(photo.Length - prefix.Length) / 4 * 3 + 3];
            return Convert.TryFromBase64Chars(photo.AsSpan(prefix.Length), bytes, out int length) && length >= magic.Length && bytes.AsSpan(0, magic.Length).SequenceEqual(magic)
                ? null
                : (StatusCodes.Status400BadRequest, "The photo is not the picture it says it is.");
        }

        return (StatusCodes.Status400BadRequest, "A photo is a JPEG, PNG or WebP picture.");
    }

    private sealed record ProfileBody(string? Name, string? Photo);
}