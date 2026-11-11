using System.Net.Http.Headers;

using Microsoft.JSInterop;

using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Transcription;

namespace MotiffLab.Web.Import;

/// <summary>
/// Hearing a stretch of a recording of any length (0014, version 0.3): music-core plans its parts with the seams at quiet
/// moments, the browser sends their sound to the scan server, several at once on Motiff Lab's server, keeps each part
/// heard on the device until all are and asks again when the server is busy (Scripts/import/hearing.ts), and music-core
/// joins the notes. A hearing cut off goes on from the parts already heard when the same stretch is heard again.
/// </summary>
public sealed partial class ScanService
{
    /// <summary>
    /// The most parts sent at once to Motiff Lab's server, which runs up to four copies, each hearing one part at a time;
    /// a player's own server hears one at a time, so it gets one.
    /// </summary>
    public const int HostedAtOnce = 4;

    private IJSObjectReference? _hearing;

    /// <summary>How many parts go to the scan server at once, at most.</summary>
    public int MostAtOnce => IsHosted ? HostedAtOnce : 1;

    /// <summary>
    /// Roughly how long hearing these parts takes, in seconds, for the estimate shown before sending: the parts go a few
    /// at a time, two at first and one more as each round comes back, up to <see cref="MostAtOnce"/>, and each round takes
    /// as long as its longest part.
    /// </summary>
    public double EstimateSeconds(IReadOnlyList<RecordingPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        double seconds = 0;
        int atOnce = Math.Min(2, MostAtOnce);
        for (int first = 0; first < parts.Count; first += atOnce, atOnce = Math.Min(MostAtOnce, atOnce + 1))
        {
            long longest = parts.Skip(first).Take(atOnce).Max(part => part.SendToMilliseconds - part.SendFromMilliseconds);
            seconds += longest / 1000.0 * SecondsPerSecondHeard;
        }

        return seconds;
    }

    /// <summary>
    /// Hears a stretch of the recording <see cref="LoadRecordingAsync"/> holds: the joined notes, or none and why, with the
    /// parts planned. <paramref name="progress"/> is told how many parts are heard of how many, and whether the server was
    /// busy and is being asked again.
    /// </summary>
    public async Task<HeardSound> HearRangeAsync(LoadedRecording recording, RecordingRange range, Action<HearingProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(range);
        if (Address is not { } address)
        {
            return new HeardSound(null, TranscriptionResult.Failed(text["transcribe.off"]), []);
        }

        int[] levels = await (await RecordingModuleAsync()).InvokeAsync<int[]>("levelsBetween", range.FromMilliseconds, range.ToMilliseconds);
        IReadOnlyList<RecordingPart> parts = RecordingParts.Plan(range.Milliseconds, levels);
        if (IsHosted && await account.TokenAsync(account.Options.ScanScope) is null)
        {
            return new HeardSound(null, TranscriptionResult.Failed(text["transcribe.signIn"]), parts);
        }

        var request = new HearRequest(address.ToString(), range.FromMilliseconds, range.ToMilliseconds, [.. parts.Select(PartToHear.Of)], MostAtOnce, recording.Fingerprint);
        IJSObjectReference hearing = _hearing ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/import/hearing.js");
        using var page = DotNetObjectReference.Create(new HearingPage(HeadersAsync, progress));
        Hearing heard;
        try
        {
            heard = await hearing.InvokeAsync<Hearing>("hear", request, page);
        }
        catch (JSException)
        {
            return new HeardSound(null, TranscriptionResult.Failed(text["transcribe.failed"]), parts);
        }

        HeardSound sound = Joined(heard, parts, range, address);
        if (sound.Midi is not null)
        {
            await hearing.InvokeVoidAsync("forgetHeard", request);
        }

        return sound;
    }

    /// <summary>The notes of the parts heard joined, or why there are none: the hearing's failure, or no notes in any part.</summary>
    private HeardSound Joined(Hearing heard, IReadOnlyList<RecordingPart> parts, RecordingRange range, Uri address)
    {
        if (heard.Failure is { } failure)
        {
            string problem = ProblemOf(failure, address);
            if (parts.Count > 1 && heard.Parts.Count > 0)
            {
                problem = $"{problem} {text.Format("transcribe.resume", heard.Parts.Count, parts.Count)}";
            }

            return new HeardSound(null, TranscriptionResult.Failed(problem), parts);
        }

        List<(RecordingPart Part, byte[] Midi, string Engine)> notes = [.. heard.Parts
            .Where(part => part.Midi is not null && part.Index >= 0 && part.Index < parts.Count)
            .Select(part => (parts[part.Index], Convert.FromBase64String(part.Midi!), part.Engine))];
        if (notes.Count == 0)
        {
            string quiet = heard.Parts.Select(part => part.Message).FirstOrDefault(message => message is { Length: > 0 }) ?? text["transcribe.failed"];
            return new HeardSound(null, TranscriptionResult.Failed(quiet) with { NothingHeard = true }, parts);
        }

        try
        {
            byte[] midi = parts.Count == 1 ? notes[0].Midi : RecordingParts.Join([.. notes.Select(note => (note.Part, note.Midi))]);
            return new HeardSound(midi, new TranscriptionResult(midi, notes[^1].Engine, range.Milliseconds / 1000.0, false, null), parts);
        }
        catch (MidiImportException)
        {
            return new HeardSound(null, TranscriptionResult.Failed(text["transcribe.failed"]), parts);
        }
    }

    /// <summary>Why a hearing stopped, in the player's words.</summary>
    private string ProblemOf(HearingFailure failure, Uri address) => failure switch
    {
        { Kind: "network" } => Unreachable(address),
        { Kind: "timeout" } => text["transcribe.tooLong"],
        { Status: 401 } when IsHosted => text["transcribe.signIn"],
        { Status: 403 } when IsHosted => text["transcribe.needsPro"],
        { Status: 404 or 405 } => text["transcribe.oldServer"],
        { Status: 429 or 502 or 503 or 504 } => text["transcribe.busyServer"],
        { Message: { Length: > 0 } message } => message,
        _ => text["transcribe.failed"],
    };

    /// <summary>The headers a request to the scan server carries: the sign-in and plan choice for Motiff Lab's server; null when signed out.</summary>
    private async Task<Dictionary<string, string>?> HeadersAsync()
    {
        var headers = new Dictionary<string, string>();
        if (!IsHosted)
        {
            return headers;
        }

        if (await account.TokenAsync(account.Options.ScanScope) is not { } token)
        {
            return null;
        }

        headers["Authorization"] = new AuthenticationHeaderValue("Bearer", token).ToString();
        using var choice = new HttpRequestMessage();
        await account.AddPlanChoiceAsync(choice);
        foreach (KeyValuePair<string, IEnumerable<string>> header in choice.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        return headers;
    }
}

/// <summary>How far a hearing in parts has got: parts heard of how many, and whether the server was busy and is asked again.</summary>
public sealed record HearingProgress(int Done, int Total, bool Retrying);

/// <summary>The page's side of a hearing, which Scripts/import/hearing.ts calls: the request headers, and progress.</summary>
public sealed class HearingPage(Func<Task<Dictionary<string, string>?>> headers, Action<HearingProgress>? progress)
{
    [JSInvokable]
    public Task<Dictionary<string, string>?> HeadersAsync() => headers();

    [JSInvokable]
    public void Progress(int done, int total, bool retrying) => progress?.Invoke(new HearingProgress(done, total, retrying));
}

/// <summary>A part as hearing.ts takes it, in milliseconds on the stretch's own clock.</summary>
public sealed record PartToHear(int Index, long SendFrom, long SendTo, long KeepFrom, long KeepTo)
{
    public static PartToHear Of(RecordingPart part) =>
        new(part.Index, part.SendFromMilliseconds, part.SendToMilliseconds, part.KeepFromMilliseconds, part.KeepToMilliseconds);
}

/// <summary>What hearing.ts hears: the scan server, the stretch of the held recording, its parts, how many at once, the sound's fingerprint.</summary>
public sealed record HearRequest(string Address, long Start, long End, IReadOnlyList<PartToHear> Parts, int Most, string Fingerprint);

/// <summary>A part heard: its notes as MIDI in base64, or none with the server's words.</summary>
public sealed record HeardPart(int Index, string? Midi, string Engine, string? Message);

/// <summary>Why a hearing stopped: the HTTP status, or 0 with "network" or "timeout" when no answer came, and the server's words.</summary>
public sealed record HearingFailure(int Status, string Kind, string? Message);

/// <summary>The parts heard, and why the hearing stopped short when it did.</summary>
public sealed record Hearing(IReadOnlyList<HeardPart> Parts, HearingFailure? Failure);
