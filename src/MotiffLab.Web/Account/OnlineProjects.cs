using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Localization;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Account;

/// <summary>
/// Saving projects online (decision record 0014): off until the player signs in and turns it on, and then each
/// project the device saves is sent to the Motiff Lab API as its project file, the same bytes as an export.
/// </summary>
/// <remarks>
/// The device stays the working copy: a project is saved here first and online a moment later, so a missing
/// connection loses nothing. The last save wins when two devices change one project. Nothing is sent while
/// saving online is off, and nothing other than the project files and the player's sign-in.
/// </remarks>
public sealed class OnlineProjects(CloudAccount account, Preferences preferences, UiText text) : IDisposable
{
    private const string OnKey = "ml-online";

    // Autosave runs on every change; a project is sent once it has been still for a moment.
    private static readonly TimeSpan s_settle = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http = new();
    private readonly Dictionary<string, (Project Project, CancellationTokenSource Wait)> _queued = [];
    private bool? _on;
    private (string Key, object[] Args)? _status;

    /// <summary>Raised when <see cref="Status"/> changes.</summary>
    public event Action? Changed;

    /// <summary>What happened to the last project sent, in plain words, or null before anything was sent.</summary>
    public string? Status => _status is { } status ? text.Format(status.Key, status.Args) : null;

    /// <summary>True when the player turned saving online on, on this device. Sign-in is checked when sending.</summary>
    public async Task<bool> IsOnAsync() => account.IsAvailable && (_on ??= await preferences.GetAsync(OnKey) == "on");

    public async Task SetOnAsync(bool on)
    {
        _on = on;
        await preferences.SetAsync(OnKey, on ? "on" : null);
    }

    /// <summary>Sends the project once it has been still for a moment, when saving online is on.</summary>
    public async Task QueueAsync(Project project)
    {
        if (!await IsOnAsync())
        {
            return;
        }

        if (_queued.Remove(project.Id, out var earlier))
        {
            await earlier.Wait.CancelAsync();
            earlier.Wait.Dispose();
        }

        var wait = new CancellationTokenSource();
        _queued[project.Id] = (project, wait);
        _ = SendLaterAsync(project, wait.Token);
    }

    /// <summary>The player's projects saved online, or null with the reason they could not be listed.</summary>
    public async Task<(IReadOnlyList<ProjectSummary>? Projects, string? Problem)> ListAsync()
    {
        using HttpRequestMessage request = await RequestAsync(HttpMethod.Get, "projects");
        if (request.Headers.Authorization is null)
        {
            return (null, text[SignInFirst]);
        }

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (null, text[Problem(response.StatusCode)]);
            }

            OnlineSummary[] rows = await response.Content.ReadFromJsonAsync<OnlineSummary[]>() ?? [];
            return ([.. rows.Select(row => new ProjectSummary(row.Id, row.FileName, row.MotifCount, row.VariationCount, row.LastOpened, row.SessionCount, row.IdeaCount))], null);
        }
        catch (HttpRequestException)
        {
            return (null, text[Offline]);
        }
    }

    /// <summary>A project saved online, or null with the reason it could not be fetched.</summary>
    public async Task<(Project? Project, string? Problem)> DownloadAsync(string id)
    {
        using HttpRequestMessage request = await RequestAsync(HttpMethod.Get, $"projects/{Uri.EscapeDataString(id)}");
        if (request.Headers.Authorization is null)
        {
            return (null, text[SignInFirst]);
        }

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode
                ? (ProjectFile.Read(await response.Content.ReadAsByteArrayAsync()), null)
                : (null, text[Problem(response.StatusCode)]);
        }
        catch (HttpRequestException)
        {
            return (null, text[Offline]);
        }
        catch (ProjectFileException error)
        {
            return (null, error.Message);
        }
    }

    /// <summary>Deletes the project's online copy, when saving online is on; the device's copy is the caller's.</summary>
    public async Task DeleteAsync(string id)
    {
        if (!await IsOnAsync())
        {
            return;
        }

        if (_queued.Remove(id, out var queued))
        {
            await queued.Wait.CancelAsync();
            queued.Wait.Dispose();
        }

        using HttpRequestMessage request = await RequestAsync(HttpMethod.Delete, $"projects/{Uri.EscapeDataString(id)}");
        await SendQuietlyAsync(request, "online.deleted");
    }

    /// <summary>Deletes everything saved online for the player and turns saving online off; the device keeps its projects.</summary>
    public async Task<string> DeleteEverythingAsync()
    {
        using HttpRequestMessage request = await RequestAsync(HttpMethod.Delete, "me");
        if (request.Headers.Authorization is null)
        {
            return text[SignInFirst];
        }

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return text[Problem(response.StatusCode)];
            }

            await SetOnAsync(false);
            return text["online.deletedEverything"];
        }
        catch (HttpRequestException)
        {
            return text[Offline];
        }
    }

    public void Dispose()
    {
        foreach ((_, CancellationTokenSource wait) in _queued.Values)
        {
            wait.Cancel();
            wait.Dispose();
        }

        _http.Dispose();
    }

    private async Task SendLaterAsync(Project project, CancellationToken cancel)
    {
        try
        {
            await Task.Delay(s_settle, cancel);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        _queued.Remove(project.Id);
        using HttpRequestMessage request = await RequestAsync(HttpMethod.Put, $"projects/{Uri.EscapeDataString(project.Id)}");
        request.Content = new ByteArrayContent(ProjectFile.Write(project));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        await SendQuietlyAsync(request, "online.saved", DateTime.Now.ToString("t", text.Culture));
    }

    // Saving online never interrupts the player: what went wrong is said on Settings, and the device keeps the project.
    private async Task SendQuietlyAsync(HttpRequestMessage request, string done, params object[] args)
    {
        if (request.Headers.Authorization is null)
        {
            SetStatus(SignInFirst);
            return;
        }

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode || (request.Method == HttpMethod.Delete && response.StatusCode == HttpStatusCode.NotFound))
            {
                SetStatus(done, args);
            }
            else
            {
                SetStatus(Problem(response.StatusCode));
            }
        }
        catch (HttpRequestException)
        {
            SetStatus(Offline);
        }
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(account.Options.Api.TrimEnd('/') + "/"), path));
        if (await account.TokenAsync(account.Options.ApiScope) is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private void SetStatus(string key, params object[] args)
    {
        _status = (key, args);
        Changed?.Invoke();
    }

    // Keys of wwwroot/i18n, so the status follows the interface's language.
    private const string SignInFirst = "online.signInFirst";

    private const string Offline = "online.offline";

    private static string Problem(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => SignInFirst,
        HttpStatusCode.RequestEntityTooLarge => "online.tooLarge",
        HttpStatusCode.NotFound => "online.notFound",
        _ => "online.problem",
    };

    private sealed record OnlineSummary(string Id, string FileName, int MotifCount, int VariationCount, DateTimeOffset LastOpened, int SessionCount, int IdeaCount);
}