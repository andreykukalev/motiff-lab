using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The JSON adapter of the performances session (<see cref="PerformancesSession"/>, audit W1.7): the other versions
/// compared with the piece (decision record 0022; contract 4), added from MIDI, a recording heard in parts or another
/// score's file, compared, bars taken into the score or the print restored, a bar marked "not a difference", and renamed.
/// Since version 1.1 (ADR 0034, section 8) a version is removed as Notes are (sources.notes.remove), and the merge draft is
/// gone. Each call's request read into typed values in the order it always was, the session asked with the printed notes
/// (<see cref="Printed"/>), the project it makes kept, the notes read again when bars were taken or restored
/// (<see cref="RefreshNotes"/>), and the version compared written as the answer. None of its calls reads the time; this file
/// holds no state.
/// </summary>
internal sealed partial class OpenPiece
{
    internal static partial class Calls
    {
        /// <summary>The performances.</summary>
        public static readonly FacadeCall<OpenPiece>[] Performances =
        [
            new("performances", static (piece, _) => Answer.Write(json => Answer.Performances(json, piece.Project)), CallFlags.OpenFirst),
            new("performance", static (piece, request) => piece.PerformanceAnswer(piece.Performance(request)), CallFlags.OpenFirst),
            new("performance.add", static (piece, request) => piece.AddPerformance(request)),
            new("performance.rename", static (piece, request) => piece.RenamePerformance(request)),
            new("performance.dismiss", static (piece, request) => piece.DismissBar(request)),
            new("performance.take", static (piece, request) => piece.TakeBars(request)),
            new("performance.takeAll", static (piece, request) => piece.TakeAll(request)),
            new("performance.restore", static (piece, request) => piece.RestoreBars(request)),
        ];
    }

    /// <summary>The version "id" (<see cref="PerformancesSession.Find"/>).</summary>
    private SavedPerformance Performance(Request request) => PerformancesSession.Find(State, request.Required("id"));

    /// <summary>The version compared with the printed notes (<see cref="PerformancesSession.Compare"/>) as the answer.</summary>
    private string PerformanceAnswer(SavedPerformance performance)
    {
        IReadOnlyList<Note> printed = Printed;
        PerformanceComparison comparison = PerformancesSession.Compare(State, printed, performance);
        return Answer.Write(json => Answer.Performance(json, performance, comparison, printed.Count, Words, Piece.Meter));
    }

    /// <summary>
    /// "name", and either "midi" (base64) with "source" ("recording" or "midi", default midi), "engine" for a recording
    /// and "recordingStart" in milliseconds where the transcribed stretch starts, or "file" (base64) with its "fileName",
    /// a MIDI or MusicXML file of another version, such as the official score over notes heard in a recording; or "parts",
    /// the notes heard in a recording heard in parts ({index, midi} by the plan of "milliseconds" and "levels" that
    /// recording.parts answers), joined into one recording. Lines the notes up with the score, keeps the anchors, raises the Check flags the version
    /// gives a scanned piece, and answers the comparison.
    /// </summary>
    private string AddPerformance(Request request)
    {
        (byte[] midi, PerformanceSource source) = request.Text("file") is { } file ? PerformancesSession.Version(request.Required("fileName"), Base64(file, "\"file\" is a MIDI or MusicXML file in base64."))
            : request.Has("parts") ? (JoinedParts(request), PerformanceSource.Recording)
            : (Base64(request.Required("midi"), "\"midi\" is a MIDI file in base64."), request.Enum<PerformanceSource>("source") ?? PerformanceSource.Midi);

        return AddVersion(State, request.Required("name"), midi, source, (request.Long("recordingStart") ?? 0) * 1000, request.Text("engine"));
    }

    /// <summary>
    /// A version added over <paramref name="piece"/> (<see cref="PerformancesSession.Add"/>), the project it makes kept, and
    /// answered compared; the piece is left as it was when it is refused.
    /// </summary>
    private string AddVersion(SessionPiece piece, string name, byte[] midi, PerformanceSource source, long start, string? engine)
    {
        AddedVersion added = PerformancesSession.Add(piece, Printed, name, midi, source, start, engine);
        Project = added.Project;
        return PerformanceAnswer(added.Performance);
    }

    /// <summary>The bytes of <paramref name="text"/> in base64; refused with <paramref name="wrong"/> when it is not.</summary>
    private static byte[] Base64(string text, string wrong)
    {
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw FacadeException.BadRequest(wrong);
        }
    }

    /// <summary>
    /// The parts a recording of <paramref name="milliseconds"/> is heard in: cut evenly, or with the seams at quiet moments
    /// when the request sends "levels", how loud the sound is in each <see cref="RecordingParts.LevelMilliseconds"/>.
    /// </summary>
    internal static IReadOnlyList<RecordingPart> PlanOf(Request request, long milliseconds)
    {
        int[] levels = [.. request.Elements("levels").Select(level => level.ValueKind == JsonValueKind.Number && level.TryGetInt32(out int loud) && loud >= 0
            ? loud
            : throw FacadeException.BadRequest("\"levels\" are whole numbers from 0, how loud the sound is in each 100 ms."))];
        return RecordingParts.Plan(milliseconds, levels);
    }

    /// <summary>The notes heard in each part of a recording, joined on the recording's own clock as the web app joins them.</summary>
    internal static byte[] JoinedParts(Request request)
    {
        long milliseconds = request.Long("milliseconds") is > 0 and var length ? length : throw FacadeException.BadRequest("Send \"milliseconds\", how long the sound heard in parts is.");
        IReadOnlyList<RecordingPart> plan = PlanOf(request, milliseconds);
        List<(RecordingPart Part, byte[] Midi)> heard = [];
        foreach (JsonElement part in request.Elements("parts"))
        {
            if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("index", out JsonElement index) || !index.TryGetInt32(out int number)
                || number < 0 || number >= plan.Count || !part.TryGetProperty("midi", out JsonElement midi) || midi.ValueKind != JsonValueKind.String)
            {
                throw FacadeException.BadRequest($"\"parts\" are {{index, midi}}, index from 0 to {plan.Count - 1}, midi a MIDI file in base64.");
            }

            try
            {
                heard.Add((plan[number], Convert.FromBase64String(midi.GetString()!)));
            }
            catch (FormatException)
            {
                throw FacadeException.BadRequest("A part's \"midi\" is a MIDI file in base64.");
            }
        }

        if (heard.Count == 0)
        {
            throw FacadeException.BadRequest("The performance has no notes.");
        }

        try
        {
            return plan.Count == 1 ? heard[0].Midi : RecordingParts.Join(heard);
        }
        catch (MidiImportException exception)
        {
            throw FacadeException.BadRequest($"The performance is not a MIDI file Motiff Lab can read: {exception.Message}");
        }
    }

    /// <summary>"id", "bars" (numbers) and "leaveOut" (ids of notes on either side the player unticked).</summary>
    private string TakeBars(Request request)
    {
        SavedPerformance performance = Performance(request);
        IReadOnlyList<string> leaveOut = request.Texts("leaveOut");
        IReadOnlyList<TakenBar> taken = PerformancesSession.Take(State, Printed, performance, BarsOf(request), leaveOut);
        return Keep(PerformancesSession.WithBarsTaken(Project, performance.Id, taken), performance.Id, notesChange: true);
    }

    /// <summary>
    /// Take everything automatically (testing item 16): "id", and "confidence" in percent (80 unless sent). Every bar the
    /// version plays otherwise that the comparison is that sure of; "performance.restore" with the bars answered undoes it.
    /// </summary>
    private string TakeAll(Request request)
    {
        SavedPerformance performance = Performance(request);
        IReadOnlyList<TakenBar> taken = PerformancesSession.TakeAll(State, Printed, performance, request.Int("confidence"));
        return taken.Count == 0 ? PerformanceAnswer(performance) : Keep(PerformancesSession.WithBarsTaken(Project, performance.Id, taken), performance.Id, notesChange: true);
    }

    /// <summary>"id" and "bars": the print restored in those bars.</summary>
    private string RestoreBars(Request request)
    {
        string id = Performance(request).Id;
        return Keep(PerformancesSession.Restored(Project, id, BarsOf(request)), id, notesChange: true);
    }

    /// <summary>"id", "bar" and "dismissed" (true unless sent): the bar marked "not a difference", or the mark taken back.</summary>
    private string DismissBar(Request request)
    {
        SavedPerformance performance = Performance(request);
        int bar = request.Int("bar") ?? throw FacadeException.BadRequest("Send \"bar\".");
        bool dismissed = request.Bool("dismissed") ?? true;
        return Keep(PerformancesSession.Dismissed(State, Printed, performance, bar, dismissed), performance.Id);
    }

    /// <summary>"id" and "name".</summary>
    private string RenamePerformance(Request request)
    {
        string id = Performance(request).Id;
        return Keep(PerformancesSession.Renamed(Project, id, request.Required("name")), id);
    }

    /// <summary>
    /// The <paramref name="project"/> a change of the version <paramref name="id"/> makes kept; when bars were taken or
    /// restored (<paramref name="notesChange"/>) the notes read again and the selection kept over them, as after a
    /// correction (<see cref="SelectionSession.FollowNotes"/>); then that version answered.
    /// </summary>
    private string Keep(Project project, string id, bool notesChange = false)
    {
        Project = project;
        if (notesChange)
        {
            RefreshNotesAndSelection();
        }

        return PerformanceAnswer(Project.FindPerformance(id)!);
    }

    private static IReadOnlyList<int> BarsOf(Request request)
    {
        IReadOnlyList<JsonElement> bars = request.Elements("bars");
        return bars.Count > 0
            ? [.. bars.Select(bar => bar.ValueKind == JsonValueKind.Number && bar.TryGetInt32(out int number) && number >= 0 ? number : throw FacadeException.BadRequest("\"bars\" are bar numbers."))]
            : throw FacadeException.BadRequest("Send \"bars\", the bar numbers.");
    }
}