using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A project as it is saved on the device and in an exported file (S1-P1): one source file and
/// what the player made from it. Notes are not stored; they come from importing the source again,
/// which is deterministic, so a saved project reopens exactly as it was (AC-S1-20).
/// </summary>
/// <remarks>
/// Motifs and variations are stored as the choices that made them: a motif's range, filter and note
/// ids, and a variation's operation, parameters and Keep choices. Corrections of imported notes are
/// stored as the edit log (S1-I7), applied to the notes on every open. Timing layers join the format with
/// rhythm restoration (#27). Times are passed in, never read from a clock, so building a project is pure.
/// What the player kept about a phrase is stored as observations (#78), each variation as its steps,
/// and the sessions the player worked in (0012) as the task, intent and passage of each.
/// </remarks>
/// <param name="Id">Stable for the life of the project, for example "p_3f9c…".</param>
/// <param name="Source">The imported file's bytes, exactly as picked.</param>
public sealed record Project(
    string Id,
    string FileName,
    byte[] Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastOpened,
    IReadOnlyList<SavedMotif> Motifs,
    IReadOnlyList<SavedVariation> Variations)
{
    /// <summary>The player's corrections of the imported notes, oldest first (S1-I7).</summary>
    public NoteEditLog Edits { get; init; } = NoteEditLog.Empty;

    /// <summary>The player's changes to the suggested phrase boundaries (S1-I5): boundaries they set and ones they took away.</summary>
    public PhraseEdits Phrases { get; init; } = PhraseEdits.None;

    /// <summary>Set when the source was read from a photo or PDF by sheet music recognition (0011); null for a file the player imported.</summary>
    public ScanOrigin? Scan { get; init; }

    /// <summary>A new project for a file the player just imported, with nothing made from it yet.</summary>
    public static Project Create(string id, string fileName, byte[] source, DateTimeOffset now) =>
        new(id, fileName, source, now, now, [], []);

    /// <summary>The row on the projects screen (N1).</summary>
    public ProjectSummary Summary => new(Id, FileName, Motifs.Count, Variations.Count, LastOpened, Sessions.Count, Observations.Count);

    /// <summary>The player's work sessions on the piece (0012), in the order they were started.</summary>
    public IReadOnlyList<Session> Sessions { get; init; } = [];

    /// <summary>The sessions most recently active first.</summary>
    public IReadOnlyList<Session> RecentSessions => [.. Sessions.OrderByDescending(session => session.LastActive).ThenByDescending(session => session.StartedAt)];

    /// <summary>The session with the id, or null.</summary>
    public Session? FindSession(string id) => Sessions.FirstOrDefault(session => session.Id == id);

    /// <summary>The project with the session started after the others.</summary>
    /// <exception cref="ArgumentException">A session with the same id is already there.</exception>
    public Project StartSession(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Sessions.Any(started => started.Id == session.Id)
            ? throw new ArgumentException($"The project already has a session {session.Id}.", nameof(session))
            : this with { Sessions = [.. Sessions, session] };
    }

    /// <summary>The project with one session changed by <paramref name="change"/>; the others stay as they are.</summary>
    /// <exception cref="KeyNotFoundException">No session has the id.</exception>
    public Project ChangeSession(string id, Func<Session, Session> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Session changed = change(FindSession(id) ?? throw new KeyNotFoundException($"The project has no session {id}."));
        return this with { Sessions = [.. Sessions.Select(session => session.Id == id ? changed : session)] };
    }

    /// <summary>
    /// The project without the session. What was kept during it stays, as ideas of the project, no
    /// longer marked with the session.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No session has the id.</exception>
    public Project EndSession(string id)
    {
        _ = FindSession(id) ?? throw new KeyNotFoundException($"The project has no session {id}.");
        return this with
        {
            Sessions = [.. Sessions.Where(session => session.Id != id)],
            Observations = [.. Observations.Select(kept => kept.SessionId == id ? kept with { SessionId = null } : kept)],
        };
    }

    /// <summary>"s_0004": one more than the highest session number, so ids stay stable for the life of the project.</summary>
    public string NextSessionId() => NextId("s_", Sessions.Select(session => session.Id));

    /// <summary>"o_0004": one more than the highest observation number.</summary>
    public string NextObservationId() => NextId("o_", Observations.Select(observation => observation.Id));

    private static string NextId(string prefix, IEnumerable<string> ids)
    {
        int highest = ids
            .Select(id => id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{prefix}{highest + 1:0000}");
    }

    /// <summary>What the player kept about the piece's phrases (#78), in the order it was kept.</summary>
    public IReadOnlyList<Observation> Observations { get; init; } = [];

    /// <summary>The observations about the phrase, in the order they were kept (<see cref="Observation.IsAbout"/>).</summary>
    public IReadOnlyList<Observation> ObservationsAbout(TickSpan phrase) => [.. Observations.Where(observation => observation.IsAbout(phrase))];

    /// <summary>The project with the observation kept after the others.</summary>
    /// <exception cref="ArgumentException">An observation with the same id is already kept.</exception>
    public Project Keep(Observation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return Observations.Any(kept => kept.Id == observation.Id)
            ? throw new ArgumentException($"The project already keeps an observation {observation.Id}.", nameof(observation))
            : this with { Observations = [.. Observations, observation] };
    }

    /// <summary>The project with one observation renamed; the others stay as they are.</summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    /// <exception cref="KeyNotFoundException">No observation has the id.</exception>
    public Project RenameObservation(string id, string name)
    {
        Observation renamed = FindObservation(id).Rename(name);
        return this with { Observations = [.. Observations.Select(kept => kept.Id == id ? renamed : kept)] };
    }

    /// <summary>The project without the observation; the others keep their order.</summary>
    /// <exception cref="KeyNotFoundException">No observation has the id.</exception>
    public Project DeleteObservation(string id)
    {
        _ = FindObservation(id);
        return this with { Observations = [.. Observations.Where(kept => kept.Id != id)] };
    }

    /// <summary>The project with the observation's note changed to <paramref name="note"/>; blank takes the note away.</summary>
    /// <exception cref="KeyNotFoundException">No observation has the id.</exception>
    /// <exception cref="ArgumentException">The note is blank and the observation keeps no variation.</exception>
    public Project ChangeObservationNote(string id, string? note)
    {
        Observation changed = FindObservation(id).WithNote(note);
        return this with { Observations = [.. Observations.Select(kept => kept.Id == id ? changed : kept)] };
    }

    private Observation FindObservation(string id) =>
        Observations.FirstOrDefault(observation => observation.Id == id) ?? throw new KeyNotFoundException($"The project keeps no observation {id}.");
}

/// <summary>A project's row on the projects screen (N1): the piece, its sessions and ideas, and when it was last opened.</summary>
public sealed record ProjectSummary(string Id, string FileName, int MotifCount, int VariationCount, DateTimeOffset LastOpened, int SessionCount = 0, int IdeaCount = 0);

/// <summary>A motif as saved: what the player picked, from which the motif is rebuilt on open.</summary>
/// <param name="NoteIds">The selected notes, checked against the source when the project opens.</param>
public sealed record SavedMotif(
    string Id,
    string Name,
    MotifSelectionKind Kind,
    MotifMode Mode,
    TickSpan Span,
    SelectionFilter Filter,
    IReadOnlyList<string> NoteIds)
{
    public static SavedMotif From(Motif motif)
    {
        ArgumentNullException.ThrowIfNull(motif);
        return new(motif.Id, motif.Name, motif.Kind, motif.Mode, motif.Span, motif.Filter, motif.NoteIds);
    }
}

/// <summary>One step of a saved variation: the operation's catalogue id and its parameters.</summary>
public sealed record SavedStep(string Operation, OperationParameters Parameters)
{
    public static SavedStep From(VariationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new(step.Operation.Id, step.Parameters);
    }
}

/// <summary>
/// A variation as saved: the chain of steps that made it from a motif (S1-E5). Steps are pure, so
/// they are run again on open. Undone steps are not saved.
/// </summary>
/// <param name="Steps">The accepted steps, oldest first.</param>
/// <param name="Pending">The step that was being heard and not yet accepted or rejected, or null.</param>
/// <param name="Keep">The properties the player asked to keep, in Keep table order.</param>
public sealed record SavedVariation(
    string Id,
    string MotifId,
    IReadOnlyList<SavedStep> Steps,
    SavedStep? Pending,
    IReadOnlyList<KeepProperty> Keep)
{
    public static SavedVariation Create(string id, string motifId, VariationChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return new(id, motifId, [.. chain.Steps.Select(SavedStep.From)], chain.Pending is { } pending ? SavedStep.From(pending) : null, [.. chain.Context.Keep.Distinct().Order()]);
    }
}

/// <summary>A project opened for work: the source imported again, the edit log applied, and its motifs and variations rebuilt.</summary>
/// <param name="Notes">The piece's notes with the project's corrections applied; <see cref="ImportedPiece.Notes"/> stays as imported.</param>
public sealed record OpenedProject(
    Project Project,
    ImportedPiece Piece,
    IReadOnlyList<Note> Notes,
    IReadOnlyList<Motif> Motifs,
    IReadOnlyList<(SavedVariation Saved, VariationChain Chain)> Variations)
{
    /// <summary>
    /// Imports the project's source and rebuilds what was made from it. Throws
    /// <see cref="ProjectFileException"/> when the source no longer imports or no longer matches.
    /// </summary>
    public static OpenedProject Open(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        PieceImportResult imported = PieceImporter.Import(project.FileName, project.Source);
        if (imported.Piece is not { } piece)
        {
            throw new ProjectFileException($"The piece in this project cannot be read: {imported.Rejection?.Message}");
        }

        if (project.Scan is { } scan)
        {
            if (piece.MusicXml is null)
            {
                throw new ProjectFileException("This project says it was scanned, but its piece is not MusicXML.");
            }

            piece = ScanCheck.Of(piece).Mark(piece, scan.ConfirmedBars);
        }

        IReadOnlyList<Note> notes;
        try
        {
            notes = project.Edits.Apply(piece.Notes);
        }
        catch (InvalidOperationException exception)
        {
            throw new ProjectFileException($"The note corrections in this project no longer fit the piece: {exception.Message}");
        }

        var motifs = new List<Motif>();
        foreach (SavedMotif saved in project.Motifs)
        {
            MotifSelection selection = saved.Kind == MotifSelectionKind.Notes
                ? MotifSelector.SelectNotes(notes, piece.Meter, saved.NoteIds)
                : MotifSelector.Select(notes, piece.Meter, saved.Span, saved.Filter);
            if (!selection.NoteIds.SequenceEqual(saved.NoteIds, StringComparer.Ordinal))
            {
                throw new ProjectFileException($"\"{saved.Name}\" no longer matches the notes of the piece.");
            }

            motifs.Add(Motif.Create(saved.Id, motifs.Count + 1, selection, saved.Kind, saved.Mode, notes) with { Name = saved.Name });
        }

        var variations = new List<(SavedVariation, VariationChain)>();
        foreach (SavedVariation saved in project.Variations)
        {
            Motif motif = motifs.FirstOrDefault(motif => motif.Id == saved.MotifId)
                ?? throw new ProjectFileException($"A variation refers to a motif ({saved.MotifId}) that is not in the project.");
            VariationChain chain = VariationChain.Start(
                VariationFacts.MotifNotes(notes, motif.Selection),
                VariationContext.For(motif.Selection, saved.Keep.ToHashSet()));
            foreach (SavedStep step in saved.Steps)
            {
                chain = Propose(chain, step).Accept();
            }

            if (saved.Pending is { } pending)
            {
                chain = Propose(chain, pending);
            }

            variations.Add((saved, chain));
        }

        var observations = new List<(Observation, VariationChain?)>();
        foreach (Observation observation in project.Observations)
        {
            observations.Add((observation, observation.Restore(notes, piece.Meter)));
        }

        return new OpenedProject(project, piece, notes, motifs, variations) { Observations = observations };
    }

    /// <summary>The observations, in the project's order, each with its variation's chain rebuilt; null for a note alone.</summary>
    public IReadOnlyList<(Observation Saved, VariationChain? Chain)> Observations { get; init; } = [];

    /// <summary>A saved step proposed on the chain again; shared with <see cref="Observation.Restore"/>.</summary>
    internal static VariationChain Propose(VariationChain chain, SavedStep step)
    {
        IVariationOperation operation = VariationOperations.Find(step.Operation)
            ?? throw new ProjectFileException($"A variation uses an operation ({step.Operation}) this version of Motiff Lab does not have.");
        if (chain.UnavailableReason(operation, step.Parameters) is { } reason)
        {
            throw new ProjectFileException($"A variation can no longer be made: {reason}");
        }

        return chain.Propose(operation, step.Parameters);
    }
}

/// <summary>A project file or saved project that cannot be opened. The message is a plain reason for the player.</summary>
public sealed class ProjectFileException(string message) : Exception(message);