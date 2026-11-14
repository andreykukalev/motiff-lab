using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.Web.Account;
using MotiffLab.Web.Localization;

namespace MotiffLab.Web.Import;

/// <summary>
/// Sheet music scanning (decision record 0011): sends a PDF or photo of printed music to the scan
/// server the player runs on their own computer (services/omr, homr in Docker) and gets MusicXML back.
/// In a build with sign-in, Motiff Lab's hosted server (0014) is the choice until the player picks their own server
/// or turns scanning off in Settings; it reads pages only for a signed-in player, and each send asks first. In a build
/// without sign-in, scanning is off until the player gives their own server's address.
/// </summary>
/// <remarks>
/// The page goes only to that address, and only when the player picks it for scanning; the server
/// deletes it as soon as it has been read. MIDI and MusicXML files never leave the browser.
/// </remarks>
public sealed partial class ScanService(IJSRuntime js, CloudAccount account, UiText text)
{
    /// <summary>The address the service's README starts it on.</summary>
    public const string SuggestedAddress = "http://localhost:8765";

    /// <summary>The largest upload the server accepts (services/omr <c>MAX_UPLOAD_MB</c>, 50 there and on Motiff Lab's server); a larger file is not sent.</summary>
    public const long MaxUploadBytes = 50 * 1024 * 1024;

    public const string AcceptedExtensions = ".pdf,.png,.jpg,.jpeg";

    /// <summary>What the transcription drop zone offers: recordings a phone, a camera or an app writes.</summary>
    public const string AcceptedRecordings = "audio/*,video/*,.m4a,.mp3,.wav,.aac,.flac,.ogg,.mp4,.mov,.webm,.mkv";

    /// <summary>
    /// The largest recording this browser decodes to let the player choose a stretch of it (0014): the file and
    /// its decoded sound both pass through memory, so a long video is better saved as sound only. The server
    /// hears up to 2½ minutes a request, as much as Transkun hears in the four minutes the hosted server may
    /// take, so the stretch is sent in parts (<see cref="RecordingRange"/>, <see cref="RecordingParts"/>).
    /// </summary>
    public const long MaxDecodeBytes = 1024L * 1024 * 1024;

    /// <summary>
    /// Roughly how many seconds the server takes to hear a second of sound: Transkun on the hosted server's two
    /// cores takes about one, plus sending (0014). Only for the estimate shown before sending.
    /// </summary>
    public const double SecondsPerSecondHeard = 1.1;

    /// <summary>How much of a recording sent as it is the server hears, when this browser cannot take its sound out.</summary>
    public const int ServerRecordingSeconds = 150;

    /// <summary>
    /// The largest recording sent as it is, when this browser cannot take its sound out: the whole file then
    /// passes through the app's memory, which a tablet does not have much of.
    /// </summary>
    public const long MaxRecordingUploadBytes = 50 * 1024 * 1024;

    private const string AddressKey = "motifflab.scan.address";

    /// <summary>What the address key holds once the player turned scanning off, so the hosted server is not chosen again.</summary>
    private const string OffValue = "off";

    // A page takes 20 to 60 s; a long PDF on a slow computer gets several minutes.
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private Task? _loading;
    private IJSObjectReference? _recording;

    /// <summary>The scan server's address, or null while scanning is off.</summary>
    public Uri? Address { get; private set; }

    /// <summary>True when the address is this computer, so the page does not leave it.</summary>
    public bool IsLocal => Address is { IsLoopback: true };

    /// <summary>Motiff Lab's hosted scan server, in a build with sign-in; null otherwise.</summary>
    public Uri? HostedAddress => account.IsAvailable ? Parse(account.Options.Scan) : null;

    /// <summary>True when scanning uses the hosted server, which needs the player signed in.</summary>
    public bool IsHosted => Address is not null && Address == HostedAddress;

    /// <summary>Reads the address from this browser. Safe to call more than once.</summary>
    public Task LoadAsync() => _loading ??= LoadCoreAsync();

    /// <summary>An absolute http or https address with no path, query or user name; null otherwise.</summary>
    public static Uri? Parse(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out Uri? address)
        && address.Scheme is "http" or "https"
        && string.IsNullOrEmpty(address.UserInfo)
        && address.AbsolutePath == "/"
        && string.IsNullOrEmpty(address.Query)
            ? address
            : null;

    /// <summary>Sets the address, or turns scanning off with null; it stays off until the player picks a server.</summary>
    public async Task SetAddressAsync(Uri? address)
    {
        await LoadAsync();
        Address = address;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", AddressKey, address?.ToString() ?? OffValue);
        }
        catch (JSException)
        {
            // Storage can be blocked; the address then lasts until the page is closed.
        }
    }

    /// <summary>Asks the server which engine it runs: "homr 0.7.0", or null with the reason it cannot be reached.</summary>
    public async Task<(string? Engine, string? Problem)> CheckAsync(CancellationToken cancellation = default)
    {
        if (Address is not { } address)
        {
            return (null, "No scan server is set.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using HttpResponseMessage response = await s_http.GetAsync(new Uri(address, "health"), timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return (null, NotAScanServer);
            }

            byte[] body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            string? engine = TextOf(body, "engine");
            string? version = TextOf(body, "version");
            return engine is { Length: > 0 } ? ($"{engine} {version}".Trim(), null) : (null, NotAScanServer);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return (null, Unreachable(address));
        }
    }

    /// <summary>Sends the file to be read; the MusicXML and engine, or a plain reason it could not be read.</summary>
    public async Task<ScanResult> RecogniseAsync(string fileName, byte[] data, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (Address is not { } address)
        {
            return ScanResult.Failed("Scanning is off. Give the scan server's address in Settings.") with { Stop = ScanStop.Off };
        }

        // Said before sending, so the whole file is not uploaded only to be refused.
        if (data.Length > MaxUploadBytes)
        {
            return ScanResult.Failed($"{text["import.scanTooLarge"]} {text["import.scanTooLargeTip"]}");
        }

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", Path.GetFileName(fileName));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(address, "recognise")) { Content = content };
        // The sign-in goes to Motiff Lab's own server only, never to an address typed in Settings.
        if (IsHosted)
        {
            if (await account.TokenAsync(account.Options.ScanScope) is not { } token)
            {
                return ScanResult.Failed(text["scan.signIn"]) with { Stop = ScanStop.SignIn };
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await account.AddPlanChoiceAsync(request);
        }

        try
        {
            using HttpResponseMessage response = await s_http.SendAsync(request, cancellation);
            if (response.IsSuccessStatusCode)
            {
                byte[] musicXml = await response.Content.ReadAsByteArrayAsync(cancellation);
                string engine = response.Headers.TryGetValues("X-Engine", out IEnumerable<string>? values) ? values.First() : "unknown engine";
                int pages = response.Headers.TryGetValues("X-Pages", out IEnumerable<string>? counts) && int.TryParse(counts.First(), out int count) ? count : 1;
                return new ScanResult(musicXml, engine, pages, null) { Reports = ReportsOf(response) };
            }

            if (IsHosted && response.StatusCode == HttpStatusCode.Forbidden)
            {
                return ScanResult.Failed(text["scan.needsPro"]) with { Stop = ScanStop.NeedsPro };
            }

            string? problem = TextOf(await response.Content.ReadAsByteArrayAsync(cancellation), "message");
            return problem is { Length: > 0 } message ? ScanResult.Failed(message)
                : response.StatusCode == HttpStatusCode.NotFound ? ScanResult.Failed(NotAScanServer) with { Stop = ScanStop.Unreachable }
                : ScanResult.Failed("The scan server could not read this file.");
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return ScanResult.Failed("The scan server took too long to answer.");
        }
        catch (HttpRequestException)
        {
            return ScanResult.Failed(Unreachable(address)) with { Stop = ScanStop.Unreachable };
        }
    }

    /// <summary>
    /// Decodes the recording picked in the input on this device and holds its sound in the browser for
    /// <see cref="HearRangeAsync"/>; null when this browser cannot decode it. A WAV file, and an MP4, M4A or MOV file in
    /// a browser with WebCodecs, are decoded a block at a time, of any size; another file only when it is at most
    /// <see cref="MaxDecodeBytes"/>, and when it is too long to decode whole on this device the answer says so
    /// (<see cref="LoadedRecording.TooLong"/>). Nothing is sent.
    /// </summary>
    public async Task<LoadedRecording?> LoadRecordingAsync(ElementReference input)
    {
        try
        {
            return await (await RecordingModuleAsync()).InvokeAsync<LoadedRecording?>("load", input, MaxDecodeBytes);
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>Lets the browser drop the held recording.</summary>
    public async Task ForgetRecordingAsync()
    {
        try
        {
            if (_recording is not null)
            {
                await _recording.InvokeVoidAsync("forget");
            }
        }
        catch (JSException)
        {
            // Nothing is held then.
        }
    }

    /// <summary>Where the page's player is in the recording, in milliseconds.</summary>
    public async Task<long> PositionOfAsync(ElementReference player) =>
        await (await RecordingModuleAsync()).InvokeAsync<long>("positionOf", player);

    /// <summary>Plays the recording in the page's player from one millisecond, pausing at another.</summary>
    public async Task PlayBetweenAsync(ElementReference player, long fromMilliseconds, long toMilliseconds) =>
        await (await RecordingModuleAsync()).InvokeVoidAsync("playBetween", player, fromMilliseconds, toMilliseconds);

    /// <summary>
    /// Wires up the selection editor over the held recording: dragging its edges or the whole selection, dragging the
    /// play position, the space bar and arrow keys. The page hears of changes through <paramref name="page"/>'s
    /// <c>DragRange</c> and <c>PlayingChanged</c>.
    /// </summary>
    public async Task AttachEditorAsync<T>(ElementReference editor, ElementReference player, DotNetObjectReference<T> page)
        where T : class =>
        await (await RecordingModuleAsync()).InvokeVoidAsync("attach", editor, player, page);

    /// <summary>Plays or pauses the page's player.</summary>
    public async Task TogglePlayAsync(ElementReference player) =>
        await (await RecordingModuleAsync()).InvokeVoidAsync("toggle", player);

    /// <summary>Moves the page's player by some milliseconds, back when negative.</summary>
    public async Task SeekByAsync(ElementReference player, long milliseconds) =>
        await (await RecordingModuleAsync()).InvokeVoidAsync("seekBy", player, milliseconds);

    private async Task<IJSObjectReference> RecordingModuleAsync() =>
        _recording ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/import/recording.js");

    /// <summary>Sends a recording, or the sound taken out of it, to be transcribed; the MIDI and engine, or a plain reason there is none.</summary>
    public async Task<TranscriptionResult> TranscribeAsync(string fileName, byte[] data, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (Address is not { } address)
        {
            return TranscriptionResult.Failed(text["transcribe.off"]);
        }

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", Path.GetFileName(fileName));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(address, "transcribe")) { Content = content };
        if (IsHosted)
        {
            if (await account.TokenAsync(account.Options.ScanScope) is not { } token)
            {
                return TranscriptionResult.Failed(text["transcribe.signIn"]);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await account.AddPlanChoiceAsync(request);
        }

        try
        {
            using HttpResponseMessage response = await s_http.SendAsync(request, cancellation);
            if (response.IsSuccessStatusCode)
            {
                byte[] midi = await response.Content.ReadAsByteArrayAsync(cancellation);
                string engine = response.Headers.TryGetValues("X-Engine", out IEnumerable<string>? values) ? values.First() : "unknown engine";
                double seconds = response.Headers.TryGetValues("X-Seconds", out IEnumerable<string>? lengths)
                    && double.TryParse(lengths.First(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double heard) ? heard : 0;
                bool cut = response.Headers.TryGetValues("X-Cut", out IEnumerable<string>? cuts) && cuts.First() == "true";
                return new TranscriptionResult(midi, engine, seconds, cut, null);
            }

            if (IsHosted && response.StatusCode == HttpStatusCode.Forbidden)
            {
                return TranscriptionResult.Failed(text["transcribe.needsPro"]);
            }

            // A scan server from before transcription has no such address.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                return TranscriptionResult.Failed(text["transcribe.oldServer"]);
            }

            string? problem = TextOf(await response.Content.ReadAsByteArrayAsync(cancellation), "message");
            return TranscriptionResult.Failed(problem is { Length: > 0 } message ? message : text["transcribe.failed"]) with
            {
                NothingHeard = response.StatusCode == HttpStatusCode.UnprocessableEntity,
            };
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return TranscriptionResult.Failed(text["transcribe.tooLong"]);
        }
        catch (HttpRequestException)
        {
            return TranscriptionResult.Failed(Unreachable(address));
        }
    }

    /// <summary>
    /// What the server saw of each page (its <c>X-Page-Report</c>, version 0.4): numbers only, never the music. None from a
    /// server before it, or when the header cannot be read: the notes are kept all the same, without the summary by page.
    /// </summary>
    private static IReadOnlyList<PageReport> ReportsOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Page-Report", out IEnumerable<string>? values))
        {
            return [];
        }

        try
        {
            return PageReport.Parse(values.First());
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private const string NotAScanServer = "Something answers at this address, but it is not a Motiff Lab scan server.";

    private static string Unreachable(Uri address) =>
        $"The scan server at {address} does not answer. Check that it is running (see Settings), and that the browser may reach it.";

    private async Task LoadCoreAsync()
    {
        try
        {
            // Motiff Lab's server, where the build has one, is the only one: there is nothing for the player to set.
            if (HostedAddress is { } hosted)
            {
                Address = hosted;
                return;
            }

            string? stored = await js.InvokeAsync<string?>("localStorage.getItem", AddressKey);
            Address = stored == OffValue ? null : Parse(stored);
        }
        catch (JSException)
        {
            Address = HostedAddress;
        }
    }

    /// <summary>A string property of a JSON object, or null when the body is not one or has no such string.</summary>
    private static string? TextOf(byte[] json, string name) => ValueOf(json, name, JsonValueKind.String)?.GetString();

    /// <summary>A property of a JSON object of the kind asked, or null when the body is not one or has no such property.</summary>
    private static JsonElement? ValueOf(byte[] json, string name, JsonValueKind kind)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == kind
                    ? value.Clone()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// A recording the browser decoded and holds: its length, its loudness in equal stretches between 0 and 1, an address its
/// page can play it from, and a fingerprint of its sound by which the parts already heard are found again. With
/// <see cref="Problem"/> "tooLong" nothing is held: the recording, of the length told, is too long to decode whole here,
/// where <see cref="MostMinutes"/> is the most.
/// </summary>
public sealed record LoadedRecording(long Milliseconds, double[] Peaks, string Url, string Fingerprint = "", string? Problem = null, int MostMinutes = 0)
{
    /// <summary>True when the recording is too long for this browser to decode whole on this device, and it cannot decode it in blocks.</summary>
    public bool TooLong => Problem == "tooLong";
}

/// <summary>What the scan server heard in a recording: the notes as MIDI, or why there are none.</summary>
/// <param name="Engine">The engine and its version, for example "transkun 2.0.1".</param>
/// <param name="Seconds">How much of the recording was heard.</param>
/// <param name="Cut">True when the recording was longer than what was heard.</param>
public sealed record TranscriptionResult(byte[]? Midi, string Engine, double Seconds, bool Cut, string? Problem)
{
    public static TranscriptionResult Failed(string problem) => new(null, "", 0, false, problem);

    /// <summary>The server read the sound but heard no notes in it, as in a quiet stretch of a recording heard in parts.</summary>
    public bool NothingHeard { get; init; }
}

/// <summary>What the scan server heard in a stretch's sound: the joined MIDI, or none and why; the last answer with notes; the parts sent.</summary>
public sealed record HeardSound(byte[]? Midi, TranscriptionResult Result, IReadOnlyList<RecordingPart> Parts)
{
    /// <summary>Why there are no notes, when there are none.</summary>
    public string? Problem => Midi is null ? Result.Problem : null;
}

/// <summary>What the scan server sent back: the recognised MusicXML, or why there is none.</summary>
/// <param name="Engine">The engine and its version, for example "homr 0.7.0".</param>
public sealed record ScanResult(byte[]? MusicXml, string Engine, int Pages, string? Problem)
{
    /// <summary>The server's report on each page it read, in order (version 0.4); empty when it sent none.</summary>
    public IReadOnlyList<PageReport> Reports { get; init; } = [];

    public static ScanResult Failed(string problem) => new(null, "", 0, problem);

    /// <summary>Why nothing more can be read until the player acts, when that is why this was not: not the file's fault.</summary>
    public ScanStop? Stop { get; init; }
}

/// <summary>What stops every read after it, not only this one (a batch waits for the player then).</summary>
public enum ScanStop
{
    /// <summary>Scanning is off: no server is set.</summary>
    Off,

    /// <summary>Motiff Lab's server needs the player signed in.</summary>
    SignIn,

    /// <summary>Motiff Lab's server reads for Pro.</summary>
    NeedsPro,

    /// <summary>The server cannot be reached, or is not a scan server.</summary>
    Unreachable,
}