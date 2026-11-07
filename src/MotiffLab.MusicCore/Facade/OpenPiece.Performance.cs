using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The calls about performances compared with the piece (decision record 0022; contract 4): add one from MIDI,
/// compare, take bars into the score or restore the print, mark a bar "not a difference", rename and remove.
/// The comparison is always against the printed notes with the player's corrections, never against bars
/// already taken, so a taken bar can be restored and compared again.
/// </summary>
internal sealed partial class OpenPiece
{
    private string CallOnPerformance(string name, Request request) => name switch
    {
        "performances" => Answer.Write(json => Answer.Performances(json, Project)),
        "performance" => PerformanceAnswer(Performance(request)),
        "performance.add" => AddPerformance(request),
        "performance.rename" => ChangePerformance(request, performance => performance with { Name = request.Required("name") }),
        "performance.dismiss" => ChangePerformance(request, performance => performance.Dismiss(BarOf(request), request.Bool("dismissed") ?? true)),
        "performance.take" => TakeBars(request),
        "performance.takeAll" => TakeAll(request),
        "performance.restore" => ChangePerformance(request, performance => performance.Restore(BarsOf(request)), notesChange: true),
        "performance.remove" => RemovePerformance(request),
        _ => throw new FacadeException("unknownCall", $"There is no call \"{name}\"."),
    };

    /// <summary>The printed notes with the player's corrections: what every performance is compared with.</summary>
    private IReadOnlyList<Note> Printed => Project.Edits.Apply(Piece.Notes);

    private SavedPerformance Performance(Request request) =>
        Project.FindPerformance(request.Required("id")) ?? throw new FacadeException("notFound", $"There is no performance {request.Required("id")}.");

    private PerformanceComparison Compare(SavedPerformance performance) =>
        PerformanceComparison.Compare(Printed, Piece.Meter, performance.Notes(), performance.Alignment());

    /// <summary>
    /// The version in the score's time for the comparison layer (0022, section 4): what agrees as printed, what it adds
    /// on the grid, bars that do not line up empty. Its likely artefacts are left out unless asked for; the ids of the
    /// notes it adds never include them.
    /// </summary>
    private PerformanceLayerNotes VersionLayer(SavedPerformance performance, bool artefacts)
    {
        PerformanceLayerNotes layer = PerformanceLayer.Of(Compare(performance), performance.Notes(), Printed, Piece.Meter);
        HashSet<string> likely = [.. layer.Artefacts];
        IReadOnlyList<string> added = [.. layer.Added.Where(id => !likely.Contains(id))];
        return artefacts ? layer with { Added = added } : new([.. layer.Notes.Where(note => !likely.Contains(note.Id))], added, []);
    }

    private string PerformanceAnswer(SavedPerformance performance)
    {
        PerformanceComparison comparison = Compare(performance);
        return Answer.Write(json => Answer.Performance(json, performance, comparison, Printed.Count, Words, Piece.Meter));
    }

    /// <summary>
    /// "name", and either "midi" (base64) with "source" ("recording" or "midi", default midi), "engine" for a recording
    /// and "recordingStart" in milliseconds where the transcribed stretch starts, or "file" (base64) with its "fileName",
    /// a MIDI or MusicXML file of another version, such as the official score over notes heard in a recording; or "parts",
    /// the notes heard in a recording heard in parts ({index, midi} by <see cref="RecordingParts.Plan"/> of "milliseconds"),
    /// joined into one recording. Lines the notes up with the score, keeps the anchors, raises the Check flags the version
    /// gives a scanned piece, and answers the comparison.
    /// </summary>
    private string AddPerformance(Request request)
    {
        (byte[] midi, PerformanceSource source) = request.Text("file") is { } file ? VersionOf(file, request.Required("fileName"))
            : request.Has("parts") ? (JoinedParts(request), PerformanceSource.Recording)
            : (MidiOf(request), request.Enum<PerformanceSource>("source") ?? PerformanceSource.Midi);

        return AddVersion(request.Required("name"), midi, source, (request.Long("recordingStart") ?? 0) * 1000, request.Text("engine"));
    }

    /// <summary>A version added over the piece: lined up with the score, kept with its anchors, and for a scanned piece the Check flags it gives.</summary>
    private string AddVersion(string name, byte[] midi, PerformanceSource source, long start, string? engine)
    {
        IReadOnlyList<PlayedNote> played;
        try
        {
            played = PlayedNotes.FromMidi(midi, start);
        }
        catch (Exception exception) when (exception is MidiImportException or ArgumentException or InvalidDataException or FormatException)
        {
            throw FacadeException.BadRequest($"The performance is not a MIDI file Motiff Lab can read: {exception.Message}");
        }

        if (played.Count == 0)
        {
            throw FacadeException.BadRequest("The performance has no notes.");
        }

        PerformanceAlignment alignment = PerformanceAlignment.Align(Printed, played);
        SavedPerformance performance = new(Project.NextPerformanceId(), name, source, midi, start, alignment.Anchors)
        {
            Engine = engine,
        };

        Project = Project.AddPerformance(performance);
        if (Project.Scan is not null)
        {
            PerformanceComparison comparison = PerformanceComparison.Compare(Printed, Piece.Meter, played, alignment);
            IReadOnlyList<RecordingCorrection> corrections = RecordingCorrections.Find(Printed, Piece.Meter, ScanCheck.Of(Piece), comparison);
            Project = Project.WithPerformanceChecks(corrections.SelectMany(correction => correction.Flags()));
        }

        return PerformanceAnswer(performance);
    }

    private static byte[] MidiOf(Request request)
    {
        try
        {
            return Convert.FromBase64String(request.Required("midi"));
        }
        catch (FormatException)
        {
            throw FacadeException.BadRequest("\"midi\" is a MIDI file in base64.");
        }
    }

    /// <summary>The notes heard in each part of a recording, joined on the recording's own clock as the web app joins them.</summary>
    internal static byte[] JoinedParts(Request request)
    {
        long milliseconds = request.Long("milliseconds") is > 0 and var length ? length : throw FacadeException.BadRequest("Send \"milliseconds\", how long the sound heard in parts is.");
        IReadOnlyList<RecordingPart> plan = RecordingParts.Plan(milliseconds);
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

    private static (byte[] Midi, PerformanceSource Source) VersionOf(string file, string fileName)
    {
        byte[] data;
        try
        {
            data = Convert.FromBase64String(file);
        }
        catch (FormatException)
        {
            throw FacadeException.BadRequest("\"file\" is a MIDI or MusicXML file in base64.");
        }

        VersionFileResult read = VersionFile.Read(fileName, data);
        return read switch
        {
            { Rejection: { } rejection } => throw FacadeException.BadRequest($"{rejection.Message} {rejection.Tip}".Trim()),
            { Midi: { } midi } => (midi, read.Source),
            _ => throw FacadeException.BadRequest("The version has no notes."),
        };
    }

    /// <summary>"id", "bars" (numbers) and "leaveOut" (ids of notes on either side the player unticked).</summary>
    private string TakeBars(Request request)
    {
        SavedPerformance performance = Performance(request);
        PerformanceComparison comparison = Compare(performance);
        IReadOnlyList<PlayedNote> played = performance.Notes();
        IReadOnlyList<string> leaveOut = request.Texts("leaveOut");
        List<TakenBar> taken = [];
        foreach (int bar in BarsOf(request))
        {
            BarComparison compared = comparison.Bars.FirstOrDefault(kept => kept.Bar == bar) ?? throw FacadeException.BadRequest($"Bar {bar} is not in the piece.");
            try
            {
                taken.Add(PerformanceTake.Take(performance.Id, compared, played, comparison.Alignment, Piece.Meter, Printed, leaveOut));
            }
            catch (ArgumentException exception)
            {
                throw FacadeException.Unavailable(exception.Message);
            }
        }

        return ChangePerformance(request, kept => kept.TakeBars(taken), notesChange: true);
    }

    /// <summary>
    /// Take everything automatically (testing item 16): "id", and "confidence" in percent (80 unless sent). Every bar the
    /// version plays otherwise that the comparison is that sure of; "performance.restore" with the bars answered undoes it.
    /// </summary>
    private string TakeAll(Request request)
    {
        SavedPerformance performance = Performance(request);
        int confidence = Math.Clamp(request.Int("confidence") ?? PerformanceTake.AutomaticConfidence, 0, 100);
        IReadOnlyList<TakenBar> taken;
        try
        {
            taken = PerformanceTake.TakeAll(performance, Compare(performance), Piece.Meter, Printed, confidence);
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.Unavailable(exception.Message);
        }

        return taken.Count == 0 ? PerformanceAnswer(performance) : ChangePerformance(request, kept => kept.TakeBars(taken), notesChange: true);
    }

    private string RemovePerformance(Request request)
    {
        bool hadTaken = Performance(request).Taken.Count > 0;
        Project = Project.RemovePerformance(request.Required("id"));
        if (hadTaken)
        {
            RefreshNotes();
        }

        return Answer.Write(json => Answer.Performances(json, Project));
    }

    private string ChangePerformance(Request request, Func<SavedPerformance, SavedPerformance> change, bool notesChange = false)
    {
        string id = Performance(request).Id;
        try
        {
            Project = Project.ChangePerformance(id, change);
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        if (notesChange)
        {
            RefreshNotes();
        }

        return PerformanceAnswer(Project.FindPerformance(id)!);
    }

    /// <summary>The notes again after bars were taken or restored, and what was worked out from them.</summary>
    private void RefreshNotes()
    {
        Notes = PerformanceTake.Apply(Printed, Project.TakenBars);
        Levels = PhraseLevels.Suggest(Notes, Piece.Meter, Piece.MusicXml?.PhraseMarks ?? PhraseMarks.None, Piece.End, KeySignature.InForce(Piece.Keys, Tick.Zero), Words, Levels.Edits);
        _understood = null;
        _offered = null;
    }

    private static int BarOf(Request request) => request.Int("bar") ?? throw FacadeException.BadRequest("Send \"bar\".");

    private static IReadOnlyList<int> BarsOf(Request request)
    {
        IReadOnlyList<JsonElement> bars = request.Elements("bars");
        return bars.Count > 0
            ? [.. bars.Select(bar => bar.ValueKind == JsonValueKind.Number && bar.TryGetInt32(out int number) && number >= 0 ? number : throw FacadeException.BadRequest("\"bars\" are bar numbers."))]
            : throw FacadeException.BadRequest("Send \"bars\", the bar numbers.");
    }
}