using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Facade.Sessions;

/// <summary>
/// The piece's other versions compared with it (audit W1.7, the fourth typed session; decision record 0022, contract 4): a
/// version added from MIDI, a recording heard or another score and lined up with the piece, its comparison and its layer in
/// the score's time, bars taken into the score or restored, a bar marked "not a difference", and its name. Every version is
/// compared with the <c>printed</c> notes, the imported notes with the player's corrections as the corrections session lays
/// them, never with bars already taken, so a taken bar can be restored and compared again. The versions and the bars taken
/// are the project's: each change here answers the project it makes, and the open piece keeps it and reads its notes again
/// (<see cref="NotesWithTakes"/>). The session keeps no state of its own; it reads the piece through
/// <see cref="SessionPiece"/>, takes typed values, never a request, and returns typed results; it writes no JSON and reads
/// no clock. OpenPiece.Performance.cs is its JSON adapter.
/// </summary>
internal static class PerformancesSession
{
    /// <summary>The version <paramref name="id"/>; refused as not found when the piece has none so.</summary>
    public static SavedPerformance Find(SessionPiece piece, string id) =>
        piece.Project.FindPerformance(id) ?? throw new FacadeException("notFound", $"There is no performance {id}.");

    /// <summary>The version compared with the <paramref name="printed"/> notes, bar by bar.</summary>
    public static PerformanceComparison Compare(SessionPiece piece, IReadOnlyList<Note> printed, SavedPerformance performance) =>
        PerformanceComparison.Compare(printed, piece.Piece.Meter, performance.Notes(), performance.Alignment());

    /// <summary>
    /// The version in the score's time for the comparison layer (0022, section 4): what agrees as printed, what it adds
    /// on the grid, bars that do not line up empty. Its likely artefacts are left out unless asked for; the ids of the
    /// notes it adds never include them.
    /// </summary>
    public static PerformanceLayerNotes Layer(SessionPiece piece, IReadOnlyList<Note> printed, SavedPerformance performance, bool artefacts)
    {
        PerformanceLayerNotes layer = PerformanceLayer.Of(Compare(piece, printed, performance), performance.Notes(), printed, piece.Piece.Meter);
        HashSet<string> likely = [.. layer.Artefacts];
        IReadOnlyList<string> added = [.. layer.Added.Where(id => !likely.Contains(id))];
        return artefacts ? layer with { Added = added } : new([.. layer.Notes.Where(note => !likely.Contains(note.Id))], added, []);
    }

    /// <summary>
    /// The version <paramref name="midi"/> added over the piece as <paramref name="name"/>, its notes from
    /// <paramref name="start"/> microseconds into the recording: lined up with the <paramref name="printed"/> notes, kept with
    /// its anchors, and for a scanned piece with the Check flags it gives. Refused when the MIDI cannot be read or has no notes.
    /// </summary>
    public static AddedVersion Add(SessionPiece piece, IReadOnlyList<Note> printed, string name, byte[] midi, PerformanceSource source, long start, string? engine)
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

        PerformanceAlignment alignment = PerformanceAlignment.Align(printed, played);
        SavedPerformance performance = new(piece.Project.NextPerformanceId(), name, source, midi, start, alignment.Anchors)
        {
            Engine = engine,
        };

        Project project = piece.Project.AddPerformance(performance);
        if (project.Scan is not null)
        {
            PerformanceComparison comparison = PerformanceComparison.Compare(printed, piece.Piece.Meter, played, alignment);
            IReadOnlyList<RecordingCorrection> corrections = RecordingCorrections.Find(printed, piece.Piece.Meter, ScanCheck.Of(piece.Piece), comparison);
            project = project.WithPerformanceChecks(corrections.SelectMany(correction => correction.Flags()));
        }

        return new AddedVersion(project, performance);
    }

    /// <summary>The notes of another version's file, <paramref name="data"/> named <paramref name="fileName"/> (a MIDI or MusicXML file), as MIDI and the source it is; refused when it is neither or has no notes.</summary>
    public static (byte[] Midi, PerformanceSource Source) Version(string fileName, byte[] data)
    {
        VersionFileResult read = VersionFile.Read(fileName, data);
        return read switch
        {
            { Rejection: { } rejection } => throw FacadeException.BadRequest($"{rejection.Message} {rejection.Tip}".Trim()),
            { Midi: { } midi } => (midi, read.Source),
            _ => throw FacadeException.BadRequest("The version has no notes."),
        };
    }

    /// <summary>
    /// The <paramref name="bars"/> of the version as it plays them, to take into the score in place of the
    /// <paramref name="printed"/> notes, without the notes on either side the player unticked (<paramref name="leaveOut"/>).
    /// Refused when a bar is not in the piece, or cannot be taken.
    /// </summary>
    public static IReadOnlyList<TakenBar> Take(SessionPiece piece, IReadOnlyList<Note> printed, SavedPerformance performance, IReadOnlyList<int> bars, IReadOnlyList<string> leaveOut)
    {
        PerformanceComparison comparison = Compare(piece, printed, performance);
        IReadOnlyList<PlayedNote> played = performance.Notes();
        List<TakenBar> taken = [];
        foreach (int bar in bars)
        {
            BarComparison compared = comparison.Bars.FirstOrDefault(kept => kept.Bar == bar) ?? throw FacadeException.BadRequest($"Bar {bar} is not in the piece.");
            try
            {
                taken.Add(PerformanceTake.Take(performance.Id, compared, played, comparison.Alignment, piece.Piece.Meter, printed, leaveOut));
            }
            catch (ArgumentException exception)
            {
                throw FacadeException.Unavailable(exception.Message);
            }
        }

        return taken;
    }

    /// <summary>
    /// Take everything automatically (testing item 16): every bar the version plays otherwise than the
    /// <paramref name="printed"/> notes that the comparison is <paramref name="confidence"/> percent sure of (80 unless given).
    /// </summary>
    public static IReadOnlyList<TakenBar> TakeAll(SessionPiece piece, IReadOnlyList<Note> printed, SavedPerformance performance, int? confidence)
    {
        try
        {
            return PerformanceTake.TakeAll(performance, Compare(piece, printed, performance), piece.Piece.Meter, printed, Math.Clamp(confidence ?? PerformanceTake.AutomaticConfidence, 0, 100));
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.Unavailable(exception.Message);
        }
    }

    /// <summary><paramref name="project"/> with the bars <paramref name="taken"/> from the version <paramref name="id"/> in the score, in place of what was taken from the same bars before.</summary>
    public static Project WithBarsTaken(Project project, string id, IReadOnlyList<TakenBar> taken) => Changed(project, id, performance => performance.TakeBars(taken));

    /// <summary><paramref name="project"/> with the print restored in the <paramref name="bars"/> taken from the version <paramref name="id"/>.</summary>
    public static Project Restored(Project project, string id, IReadOnlyList<int> bars) => Changed(project, id, performance => performance.Restore(bars));

    /// <summary><paramref name="project"/> with the version <paramref name="id"/>'s <paramref name="bar"/> marked "not a difference", or the mark taken back.</summary>
    public static Project Dismissed(Project project, string id, int bar, bool dismissed) => Changed(project, id, performance => performance.Dismiss(bar, dismissed));

    /// <summary><paramref name="project"/> with the version <paramref name="id"/> named <paramref name="name"/>; refused when the name is empty.</summary>
    public static Project Renamed(Project project, string id, string name) => Changed(project, id, performance => performance with { Name = name });

    /// <summary>The <paramref name="printed"/> notes with the bars taken from the project's versions in place of theirs: the notes the piece is open on.</summary>
    public static IReadOnlyList<Note> NotesWithTakes(IReadOnlyList<Note> printed, Project project) => PerformanceTake.Apply(printed, project.TakenBars);

    private static Project Changed(Project project, string id, Func<SavedPerformance, SavedPerformance> change)
    {
        try
        {
            return project.ChangePerformance(id, change);
        }
        catch (ArgumentException exception)
        {
            throw FacadeException.BadRequest(exception.Message);
        }
    }
}