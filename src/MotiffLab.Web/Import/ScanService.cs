using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

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
public sealed class ScanService(IJSRuntime js, CloudAccount account, UiText text)
{
    /// <summary>The address the service's README starts it on.</summary>
    public const string SuggestedAddress = "http://localhost:8765";

    /// <summary>The largest upload the server accepts.</summary>
    public const long MaxUploadBytes = 50 * 1024 * 1024;

    public const string AcceptedExtensions = ".pdf,.png,.jpg,.jpeg";

    /// <summary>What the transcription drop zone offers: recordings a phone, a camera or an app writes.</summary>
    public const string AcceptedRecordings = "audio/*,video/*,.m4a,.mp3,.wav,.aac,.flac,.ogg,.mp4,.mov,.webm,.mkv";

    /// <summary>
    /// How much of a recording this browser takes the sound out of, from its start (0014). The server hears
    /// up to 2½ minutes a request, as much as Transkun hears in the four minutes the hosted server may take,
    /// so a longer sound is sent in parts (<see cref="RecordingParts"/>).
    /// </summary>
    public const int MaxRecordingSeconds = 420;

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
            return ScanResult.Failed("Scanning is off. Give the scan server's address in Settings.");
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
                return ScanResult.Failed(text["scan.signIn"]);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        try
        {
            using HttpResponseMessage response = await s_http.SendAsync(request, cancellation);
            if (response.IsSuccessStatusCode)
            {
                byte[] musicXml = await response.Content.ReadAsByteArrayAsync(cancellation);
                string engine = response.Headers.TryGetValues("X-Engine", out IEnumerable<string>? values) ? values.First() : "unknown engine";
                int pages = response.Headers.TryGetValues("X-Pages", out IEnumerable<string>? counts) && int.TryParse(counts.First(), out int count) ? count : 1;
                return new ScanResult(musicXml, engine, pages, null);
            }

            string? problem = TextOf(await response.Content.ReadAsByteArrayAsync(cancellation), "message");
            return ScanResult.Failed(problem is { Length: > 0 } message
                ? message
                : response.StatusCode == HttpStatusCode.NotFound ? NotAScanServer : "The scan server could not read this file.");
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return ScanResult.Failed("The scan server took too long to answer.");
        }
        catch (HttpRequestException)
        {
            return ScanResult.Failed(Unreachable(address));
        }
    }

    /// <summary>
    /// The first minutes of the sound of the recording picked in the input, decoded and mixed down on this
    /// device; null when this browser cannot decode it.
    /// </summary>
    public async Task<RecordingSound?> SoundOfAsync(ElementReference input)
    {
        try
        {
            _recording ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/import/recording.js");
            return await _recording.InvokeAsync<RecordingSound?>("soundOf", input, MaxRecordingSeconds);
        }
        catch (JSException)
        {
            return null;
        }
    }

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
    private static string? TextOf(byte[] json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The sound the browser took out of a recording: a mono WAV file, its length, and whether the recording was longer.</summary>
public sealed record RecordingSound(byte[] Wav, double Seconds, bool Cut)
{
    private const int HeaderBytes = 44;

    /// <summary>The length in milliseconds.</summary>
    public long Milliseconds => (long)Math.Round(Seconds * 1000);

    /// <summary>The stretch of the sound from one millisecond to another, as a WAV file of its own.</summary>
    public byte[] Part(long fromMilliseconds, long toMilliseconds)
    {
        int rate = BitConverter.ToInt32(Wav, 24);
        int samples = (Wav.Length - HeaderBytes) / 2;
        int from = (int)Math.Clamp(fromMilliseconds * rate / 1000, 0, samples);
        int to = (int)Math.Clamp(toMilliseconds * rate / 1000, from, samples);
        byte[] part = new byte[HeaderBytes + ((to - from) * 2)];
        Wav.AsSpan(0, HeaderBytes).CopyTo(part);
        Wav.AsSpan(HeaderBytes + (from * 2), (to - from) * 2).CopyTo(part.AsSpan(HeaderBytes));
        BitConverter.TryWriteBytes(part.AsSpan(4), part.Length - 8);
        BitConverter.TryWriteBytes(part.AsSpan(40), part.Length - HeaderBytes);
        return part;
    }
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

/// <summary>What the scan server sent back: the recognised MusicXML, or why there is none.</summary>
/// <param name="Engine">The engine and its version, for example "homr 0.7.0".</param>
public sealed record ScanResult(byte[]? MusicXml, string Engine, int Pages, string? Problem)
{
    public static ScanResult Failed(string problem) => new(null, "", 0, problem);
}