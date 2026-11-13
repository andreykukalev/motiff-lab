using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using MotiffLab.MusicCore.Accounts;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Localization;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Account;

/// <summary>
/// Saving projects online (decision record 0014), which syncs them between the player's devices (version 0.2,
/// <see cref="CloudSync"/>): on once the player signs in, until they turn it off in Settings, and then each project the
/// device saves is sent to the Motiff Lab API as its project file, the same bytes as an export.
/// </summary>
/// <remarks>
/// The device stays the working copy: a project is saved here first and online a moment later, so a missing
/// connection loses nothing. For each project the device keeps the online copy's ETag and save time it last saw, and
/// whether it changed here since (<see cref="SyncRecord"/>), so a save goes with <c>If-Match</c> and a change made on
/// another device is never overwritten unseen. Nothing is sent while signed out or with saving online off, and nothing
/// other than the project files and the player's sign-in.
/// </remarks>
public sealed class OnlineProjects(CloudAccount account, Preferences preferences, UiText text) : IDisposable
{
    private const string OnKey = "ml-online";

    private const string SyncKey = "ml-sync";

    // Autosave runs on every change; a project is sent once it has been still for a moment.
    private static readonly TimeSpan s_settle = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http = new();
    private readonly Dictionary<string, (Project Project, CancellationTokenSource Wait)> _queued = [];
    private bool? _on;
    private Dictionary<string, SyncRecord>? _records;
    private (string Key, object[] Args)? _status;

    /// <summary>Raised when <see cref="Status"/> changes.</summary>
    public event Action? Changed;

    /// <summary>What happened to the last project sent, in plain words, or null before anything was sent.</summary>
    public string? Status => _status is { } status ? text.Format(status.Key, status.Args) : null;

    /// <summary>
    /// True unless the player turned saving online off on this device (0014 item 13 as 0.2 amends it: signing in turns
    /// it on). Sign-in is checked when sending.
    /// </summary>
    public async Task<bool> IsOnAsync() => account.IsAvailable && (_on ??= await preferences.GetAsync(OnKey) != "off");

    public async Task SetOnAsync(bool on)
    {
        _on = on;
        await preferences.SetAsync(OnKey, on ? null : "off");
    }

    /// <summary>True when saving online is on and someone is signed in, so the projects sync.</summary>
    public async Task<bool> IsSyncingAsync() => await IsOnAsync() && await account.PlayerAsync() is not null;

    /// <summary>How many projects stay on this device because a free account keeps <see cref="Plans.FreeProjectsOnline"/> online.</summary>
    public async Task<int> OverQuotaAsync() => (await RecordsAsync()).Values.Count(record => record.OverQuota);

    /// <summary>
    /// Notes that the project changed here, and sends it once it has been still for a moment, when saving online is on.
    /// The note stays when nothing can be sent, so the next sync sends it.
    /// </summary>
    public async Task QueueAsync(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        await ChangedHereAsync(project.Id);
        if (!await IsSyncingAsync())
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
        (IReadOnlyList<OnlineRow>? rows, string? problem) = await RowsAsync();
        return rows is null
            ? (null, problem)
            : ([.. rows.Select(row => new ProjectSummary(row.Id, row.FileName, row.MotifCount, row.VariationCount, row.LastOpened, row.ResumeCount, row.IdeaCount))], null);
    }

    /// <summary>The rows the API lists, with when each was saved online, or null with the reason they could not be listed.</summary>
    public async Task<(IReadOnlyList<OnlineRow>? Rows, string? Problem)> RowsAsync()
    {
        using HttpRequestMessage request = await RequestAsync(HttpMethod.Get, "projects");
        if (request.Headers.Authorization is null)
        {
            return (null, text[SignInFirst]);
        }

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode
                ? (await response.Content.ReadFromJsonAsync<OnlineRow[]>() ?? [], null)
                : (null, text[Problem(response.StatusCode)]);
        }
        catch (HttpRequestException)
        {
            return (null, text[Offline]);
        }
    }

    /// <summary>A project saved online, or null with the reason it could not be fetched.</summary>
    public async Task<(Project? Project, string? Problem)> DownloadAsync(string id)
    {
        (Project? project, _, string? problem) = await FetchAsync(id);
        return (project, problem);
    }

    /// <summary>A project saved online with its ETag, or null with the reason it could not be fetched.</summary>
    public async Task<(Project? Project, string? ETag, string? Problem)> FetchAsync(string id)
    {
        using HttpRequestMessage request = await RequestAsync(HttpMethod.Get, $"projects/{Uri.EscapeDataString(id)}");
        if (request.Headers.Authorization is null)
        {
            return (null, null, text[SignInFirst]);
        }

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode
                ? (ProjectFile.Read(await response.Content.ReadAsByteArrayAsync()), response.Headers.ETag?.ToString(), null)
                : (null, null, text[Problem(response.StatusCode)]);
        }
        catch (HttpRequestException)
        {
            return (null, null, text[Offline]);
        }
        catch (ProjectFileException error)
        {
            return (null, null, error.Message);
        }
    }

    /// <summary>
    /// Sends the online copy now, over the online copy with <paramref name="ifMatch"/> when given, and notes what the API
    /// answered: its new ETag, or that a free account has no room for it. False when it was not saved online.
    /// </summary>
    public async Task<bool> UploadAsync(Project copy, string? ifMatch)
    {
        ArgumentNullException.ThrowIfNull(copy);
        using HttpRequestMessage request = await RequestAsync(HttpMethod.Put, $"projects/{Uri.EscapeDataString(copy.Id)}");
        request.Content = new ByteArrayContent(ProjectFile.Write(copy));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (request.Headers.Authorization is null)
        {
            SetStatus(SignInFirst);
            return false;
        }

        DateTimeOffset sent = DateTimeOffset.UtcNow;
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                // A save made here while this one travelled is still to send.
                bool savedSince = (await RecordAsync(copy.Id)).ChangedAt > sent;
                await NoteAsync(copy.Id, new SyncRecord(response.Headers.ETag?.ToString(), null, savedSince, savedSince ? DateTimeOffset.UtcNow : null));
                SetStatus("online.saved", DateTime.Now.ToString("t", text.Culture));
                return true;
            }

            string? code = response.StatusCode == HttpStatusCode.Forbidden ? await CodeAsync(response) : null;
            if (code == "quota")
            {
                // Tried again when it is saved here next, or when the account has room again (CloudSync).
                await NoteAsync(copy.Id, await RecordAsync(copy.Id) with { OverQuota = true, Dirty = false });
                SetStatus("online.quota", Plans.FreeProjectsOnline);
            }
            else if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // Changed on another device since this one saw it: the next sync fetches it and keeps both.
                await NoteAsync(copy.Id, await RecordAsync(copy.Id) with { SavedAt = null });
                SetStatus("online.changedElsewhere");
            }
            else
            {
                SetStatus(Problem(response.StatusCode));
            }

            return false;
        }
        catch (HttpRequestException)
        {
            SetStatus(Offline);
            return false;
        }
    }

    /// <summary>What this device knows of the project's online copy; an empty record when it has never synced it.</summary>
    public async Task<SyncRecord> RecordAsync(string id) =>
        (await RecordsAsync()).TryGetValue(id, out SyncRecord? record) ? record : SyncRecord.None;

    /// <summary>Every project this device keeps a sync record for.</summary>
    public async Task<IReadOnlyDictionary<string, SyncRecord>> AllRecordsAsync() => await RecordsAsync();

    /// <summary>Keeps what this device knows of the project's online copy; null forgets it.</summary>
    public async Task NoteAsync(string id, SyncRecord? record)
    {
        Dictionary<string, SyncRecord> records = await RecordsAsync();
        if (record is null || record == SyncRecord.None)
        {
            records.Remove(id);
        }
        else
        {
            records[id] = record;
        }

        await preferences.SetAsync(SyncKey, JsonSerializer.Serialize(records));
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
        if (await SendQuietlyAsync(request, "online.deleted"))
        {
            // Deleted on both sides; a piece deleted only here is left online and not fetched again (ProjectSync).
            await NoteAsync(id, null);
        }
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

    /// <summary>
    /// Deletes the signed-in player's copies in the test database (the API's DELETE /me, their profile there too), for
    /// «Очистить тестовую библиотеку» on the test site. Only on develop's own API (<see cref="IsDevelopApi"/>): a test
    /// site still on the main servers deletes nothing online. Saving online stays as it was.
    /// </summary>
    public async Task<TestCopies> DeleteTestCopiesAsync()
    {
        if (await account.PlayerAsync() is null)
        {
            return TestCopies.SignedOut;
        }

        if (!IsDevelopApi(account.Options.Api))
        {
            return TestCopies.NotDevelop;
        }

        using HttpRequestMessage request = await RequestAsync(HttpMethod.Delete, "me");
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode ? TestCopies.Deleted : TestCopies.Failed;
        }
        catch (HttpRequestException)
        {
            return TestCopies.Failed;
        }
    }

    /// <summary>Whether the address is the develop environment's API (<c>ca-motifflab-api-dev.…</c>, infra/dev-env.bicep).</summary>
    public static bool IsDevelopApi(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.StartsWith("ca-motifflab-api-dev.", StringComparison.OrdinalIgnoreCase);

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
        SyncRecord record = await RecordAsync(project.Id);
        // A project known online goes over the copy this device saw; one never synced, or seen deleted online, goes as new.
        await UploadAsync(project, record.DeletedOnline ? null : record.ETag);
    }

    private async Task ChangedHereAsync(string id)
    {
        SyncRecord record = await RecordAsync(id);
        await NoteAsync(id, record with { Dirty = true, ChangedAt = DateTimeOffset.UtcNow });
    }

    private async Task<Dictionary<string, SyncRecord>> RecordsAsync()
    {
        if (_records is null)
        {
            string? kept = await preferences.GetAsync(SyncKey);
            try
            {
                _records = kept is null ? [] : JsonSerializer.Deserialize<Dictionary<string, SyncRecord>>(kept) ?? [];
            }
            catch (JsonException)
            {
                // A record that cannot be read is no record: the next sync compares the two copies and keeps both.
                _records = [];
            }
        }

        return _records;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        try
        {
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Saving online never interrupts the player: what went wrong is said on Settings, and the device keeps the project.
    private async Task<bool> SendQuietlyAsync(HttpRequestMessage request, string done, params object[] args)
    {
        if (request.Headers.Authorization is null)
        {
            SetStatus(SignInFirst);
            return false;
        }

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode || (request.Method == HttpMethod.Delete && response.StatusCode == HttpStatusCode.NotFound))
            {
                SetStatus(done, args);
                return true;
            }

            SetStatus(Problem(response.StatusCode));
            return false;
        }
        catch (HttpRequestException)
        {
            SetStatus(Offline);
            return false;
        }
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(account.Options.Api.TrimEnd('/') + "/"), path));
        if (await account.TokenAsync(account.Options.ApiScope) is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        await account.AddPlanChoiceAsync(request);
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
        HttpStatusCode.Unauthorized => SignInFirst,
        HttpStatusCode.Forbidden => "online.needsPro",
        HttpStatusCode.RequestEntityTooLarge => "online.tooLarge",
        HttpStatusCode.NotFound => "online.notFound",
        _ => "online.problem",
    };

}

/// <summary>What became of the player's copies in the test database (<see cref="OnlineProjects.DeleteTestCopiesAsync"/>).</summary>
public enum TestCopies
{
    SignedOut,
    Deleted,
    Failed,
    NotDevelop,
}

/// <summary>A row of the API's list of the player's projects, with when it was saved online (the server's clock).</summary>
public sealed record OnlineRow(string Id, string FileName, int MotifCount, int VariationCount, DateTimeOffset LastOpened, int ResumeCount, int IdeaCount, DateTimeOffset SavedAt);

/// <summary>
/// What this device knows of one project's online copy (version 0.2 sync), kept on the device beside the projects and never
/// in the project file: the ETag and save time of the online copy it last saw, whether it changed here since and when, and
/// whether the online copy was seen deleted or found no room in a free account.
/// </summary>
public sealed record SyncRecord(string? ETag, DateTimeOffset? SavedAt, bool Dirty, DateTimeOffset? ChangedAt, bool DeletedOnline = false, bool OverQuota = false)
{
    /// <summary>Never synced.</summary>
    public static SyncRecord None { get; } = new(null, null, false, null);

    /// <summary>The device synced it before: it saw the online copy, or saw it deleted.</summary>
    public bool Known => ETag is not null || DeletedOnline;
}