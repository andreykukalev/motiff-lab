using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Settings;
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
/// What the player kept about a phrase is stored as observations (#78), each variation as its steps.
/// Version 12 (0018) adds what the redesign keeps with the piece: the marks, the bookmarks and their
/// categories, the resume points that replace the sessions of 0012, the Check flags and the piece's
/// settings overrides.
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

    /// <summary>Set when the source was heard in an audio or video recording by transcription (0014); null otherwise.</summary>
    public TranscriptionOrigin? Transcription { get; init; }

    /// <summary>A new project for a file the player just imported, with nothing made from it yet.</summary>
    public static Project Create(string id, string fileName, byte[] source, DateTimeOffset now) =>
        new(id, fileName, source, now, now, [], []);

    /// <summary>The row on the projects screen (N1).</summary>
    public ProjectSummary Summary => new(Id, FileName, Motifs.Count, Variations.Count, LastOpened, ResumePoints.Count, Observations.Count);

    /// <summary>The marks the player made on the piece (0018, section 5): ink, margin notes, highlights and fingering.</summary>
    public Marks Marks { get; init; } = Marks.Empty;

    /// <summary>The project with its marks changed by <paramref name="change"/>.</summary>
    public Project ChangeMarks(Func<Marks, Marks> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return this with { Marks = change(Marks) };
    }

    /// <summary>The bookmark categories (0018, section 5): the four built-in ones, as named here, then the player's own.</summary>
    public IReadOnlyList<BookmarkCategory> Categories { get; init; } = BookmarkCategory.BuiltIn;

    /// <summary>The player's bookmarks, in the player's order; <see cref="BookmarksInScoreOrder"/> sorts them by place.</summary>
    public IReadOnlyList<Bookmark> Bookmarks { get; init; } = [];

    public IReadOnlyList<Bookmark> BookmarksInScoreOrder => [.. Bookmarks.OrderBy(bookmark => bookmark.Span.Onset).ThenBy(bookmark => bookmark.Span.End)];

    public BookmarkCategory? FindCategory(string id) => Categories.FirstOrDefault(category => category.Id == id);

    public Bookmark? FindBookmark(string id) => Bookmarks.FirstOrDefault(bookmark => bookmark.Id == id);

    /// <summary>The project with the bookmark at the end of the player's order.</summary>
    /// <exception cref="ArgumentException">A bookmark with the same id is already there, or its category is not.</exception>
    public Project AddBookmark(Bookmark bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        if (Bookmarks.Any(kept => kept.Id == bookmark.Id))
        {
            throw new ArgumentException($"There is already a bookmark {bookmark.Id}.", nameof(bookmark));
        }

        return FindCategory(bookmark.CategoryId) is null
            ? throw new ArgumentException($"There is no bookmark category {bookmark.CategoryId}.", nameof(bookmark))
            : this with { Bookmarks = [.. Bookmarks, bookmark] };
    }

    /// <summary>The project with one bookmark changed by <paramref name="change"/>, keeping its id and place in the order.</summary>
    /// <exception cref="KeyNotFoundException">No bookmark has the id.</exception>
    /// <exception cref="ArgumentException">The change moved it to a category that is not there, or changed its id.</exception>
    public Project ChangeBookmark(string id, Func<Bookmark, Bookmark> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Bookmark changed = change(FindBookmark(id) ?? throw new KeyNotFoundException($"There is no bookmark {id}."));
        if (changed.Id != id)
        {
            throw new ArgumentException("A bookmark keeps its id.", nameof(change));
        }

        return FindCategory(changed.CategoryId) is null
            ? throw new ArgumentException($"There is no bookmark category {changed.CategoryId}.", nameof(change))
            : this with { Bookmarks = [.. Bookmarks.Select(bookmark => bookmark.Id == id ? changed : bookmark)] };
    }

    /// <summary>The project with the bookmark moved to <paramref name="index"/> in the player's order.</summary>
    /// <exception cref="KeyNotFoundException">No bookmark has the id.</exception>
    public Project MoveBookmark(string id, int index)
    {
        Bookmark moved = FindBookmark(id) ?? throw new KeyNotFoundException($"There is no bookmark {id}.");
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Bookmarks.Count);
        var order = Bookmarks.Where(bookmark => bookmark.Id != id).ToList();
        order.Insert(index, moved);
        return this with { Bookmarks = order };
    }

    /// <exception cref="KeyNotFoundException">No bookmark has the id.</exception>
    public Project RemoveBookmark(string id)
    {
        _ = FindBookmark(id) ?? throw new KeyNotFoundException($"There is no bookmark {id}.");
        return this with { Bookmarks = [.. Bookmarks.Where(bookmark => bookmark.Id != id)] };
    }

    /// <summary>
    /// The project with its bookmarks where they belong after the phrases changed (<see cref="Bookmark.Follow"/>):
    /// called with the phrases of the level a bookmark was made from, after a merge or a split.
    /// </summary>
    public Project FollowPhrases(IEnumerable<TickSpan> phrases)
    {
        TickSpan[] spans = [.. phrases ?? throw new ArgumentNullException(nameof(phrases))];
        return this with { Bookmarks = [.. Bookmarks.Select(bookmark => bookmark.Follow(spans))] };
    }

    /// <summary>The project with a category of the player's own after the others.</summary>
    /// <exception cref="ArgumentException">A category with the same id is already there.</exception>
    public Project AddCategory(BookmarkCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return FindCategory(category.Id) is not null
            ? throw new ArgumentException($"There is already a category {category.Id}.", nameof(category))
            : this with { Categories = [.. Categories, category] };
    }

    /// <summary>The project with the category renamed; blank gives a built-in one its own name back.</summary>
    /// <exception cref="KeyNotFoundException">No category has the id.</exception>
    public Project RenameCategory(string id, string? name)
    {
        BookmarkCategory renamed = (FindCategory(id) ?? throw new KeyNotFoundException($"There is no category {id}.")).Rename(name);
        return this with { Categories = [.. Categories.Select(category => category.Id == id ? renamed : category)] };
    }

    /// <summary>
    /// The project without a category of the player's own; its bookmarks move to Practise. The built-in
    /// categories stay.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No category has the id.</exception>
    /// <exception cref="ArgumentException">The category is built in.</exception>
    public Project RemoveCategory(string id)
    {
        BookmarkCategory category = FindCategory(id) ?? throw new KeyNotFoundException($"There is no category {id}.");
        if (category.IsBuiltIn)
        {
            throw new ArgumentException("The four built-in categories stay.", nameof(id));
        }

        return this with
        {
            Categories = [.. Categories.Where(kept => kept.Id != id)],
            Bookmarks = [.. Bookmarks.Select(bookmark => bookmark.CategoryId == id ? bookmark with { CategoryId = BookmarkCategory.PractiseId } : bookmark)],
        };
    }

    /// <summary>"b_0004": one more than the highest bookmark number.</summary>
    public string NextBookmarkId() => NextId("b_", Bookmarks.Select(bookmark => bookmark.Id));

    /// <summary>"c_0004": one more than the highest number among the player's own categories.</summary>
    public string NextCategoryId() => NextId("c_", Categories.Select(category => category.Id));

    /// <summary>Where the player left off (0018, section 9), in the order the points were made.</summary>
    public IReadOnlyList<ResumePoint> ResumePoints { get; init; } = [];

    /// <summary>The resume points most recently active first; Resume opens the first.</summary>
    public IReadOnlyList<ResumePoint> RecentResumePoints => [.. ResumePoints.OrderByDescending(point => point.LastActive).ThenByDescending(point => point.CreatedAt)];

    public ResumePoint? FindResumePoint(string id) => ResumePoints.FirstOrDefault(point => point.Id == id);

    /// <summary>The project with the resume point added after the others.</summary>
    /// <exception cref="ArgumentException">A resume point with the same id is already there.</exception>
    public Project AddResumePoint(ResumePoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return ResumePoints.Any(kept => kept.Id == point.Id)
            ? throw new ArgumentException($"The project already has a resume point {point.Id}.", nameof(point))
            : this with { ResumePoints = [.. ResumePoints, point] };
    }

    /// <summary>The project with one resume point changed by <paramref name="change"/>; the others stay as they are.</summary>
    /// <exception cref="KeyNotFoundException">No resume point has the id.</exception>
    public Project ChangeResumePoint(string id, Func<ResumePoint, ResumePoint> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ResumePoint changed = change(FindResumePoint(id) ?? throw new KeyNotFoundException($"The project has no resume point {id}."));
        return this with { ResumePoints = [.. ResumePoints.Select(point => point.Id == id ? changed : point)] };
    }

    /// <exception cref="KeyNotFoundException">No resume point has the id.</exception>
    public Project RemoveResumePoint(string id)
    {
        _ = FindResumePoint(id) ?? throw new KeyNotFoundException($"The project has no resume point {id}.");
        return this with { ResumePoints = [.. ResumePoints.Where(point => point.Id != id)] };
    }

    /// <summary>"r_0004": one more than the highest resume point number, so ids stay stable for the life of the project.</summary>
    public string NextResumePointId() => NextId("r_", ResumePoints.Select(point => point.Id));

    /// <summary>"o_0004": one more than the highest observation number.</summary>
    public string NextObservationId() => NextId("o_", Observations.Select(observation => observation.Id));

    /// <summary>
    /// The Check flags (0018, section 9): the doubtful bars of a scan or a transcription, with whether the player
    /// resolved each. Empty for a file the player imported. Null only for a project read from a file before
    /// version 12, until it is opened: <see cref="OpenedProject.Open"/> works the flags out from the piece then.
    /// </summary>
    public IReadOnlyList<CheckFlag>? Check { get; init; } = [];

    /// <summary>The flags the player has not resolved, in bar order.</summary>
    public IReadOnlyList<CheckFlag> OpenChecks => [.. (Check ?? []).Where(flag => !flag.Resolved).OrderBy(flag => flag.Bar)];

    /// <summary>
    /// The project with every flag on the bar resolved, or open again. For a scan the bar is confirmed with
    /// it (<see cref="ScanOrigin.ConfirmedBars"/>), so its notes read as confirmed by the player.
    /// </summary>
    public Project ResolveCheck(int bar, bool resolved = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bar);
        if (!(Check ?? []).Any(flag => flag.Bar == bar))
        {
            throw new KeyNotFoundException($"Check has nothing about bar {bar}.");
        }

        return this with
        {
            Check = [.. (Check ?? []).Select(flag => flag.Bar == bar ? flag with { Resolved = resolved } : flag)],
            Scan = Scan is { } scan ? resolved ? scan.Confirm([bar]) : scan.Unconfirm(bar) : null,
        };
    }

    /// <summary>
    /// The piece's overrides of the settings (0018, section 10): only settings of <see cref="SettingScope.Piece"/>
    /// scope, by id, each with a value the setting allows. They travel with the project.
    /// </summary>
    public IReadOnlyDictionary<string, string> SettingOverrides { get; init; } = s_noSettings;

    private static readonly IReadOnlyDictionary<string, string> s_noSettings = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The project with the setting overridden for this piece; null clears the override.</summary>
    /// <exception cref="KeyNotFoundException">No setting has the id.</exception>
    /// <exception cref="ArgumentException">The setting cannot be overridden for a piece, or does not allow the value.</exception>
    public Project OverrideSetting(string id, string? value)
    {
        Setting setting = SettingsCatalogue.Get(id);
        if (setting.Scope != SettingScope.Piece)
        {
            throw new ArgumentException($"\"{id}\" is the same for every piece.", nameof(id));
        }

        if (value is not null && !setting.Allows(value))
        {
            throw new ArgumentException($"\"{id}\" cannot be \"{value}\"; it is one of {string.Join(", ", setting.Choices)}.", nameof(value));
        }

        var overrides = new Dictionary<string, string>(SettingOverrides, StringComparer.Ordinal);
        if (value is null)
        {
            overrides.Remove(id);
        }
        else
        {
            overrides[id] = value;
        }

        return this with { SettingOverrides = overrides };
    }

    internal static string NextId(string prefix, IEnumerable<string> ids)
    {
        int highest = ids
            .Select(id => id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{prefix}{highest + 1:0000}");
    }

    /// <summary>What the player kept about the piece's phrases (#78), the ideas, in the order it was kept.</summary>
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

/// <summary>A project's row on the projects screen (N1): the piece, its resume points and ideas, and when it was last opened.</summary>
public sealed record ProjectSummary(string Id, string FileName, int MotifCount, int VariationCount, DateTimeOffset LastOpened, int ResumeCount = 0, int IdeaCount = 0);

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
/// <param name="Project">The project, with its Check flags worked out when the file it came from had none (before version 12).</param>
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

        if (project.Transcription is not null)
        {
            if (piece.Midi is null)
            {
                throw new ProjectFileException("This project says it was transcribed from a recording, but its piece is not MIDI.");
            }

            if (project.Transcription.Bars is { } bars)
            {
                try
                {
                    piece = bars.Apply(piece);
                }
                catch (ArgumentException exception)
                {
                    throw new ProjectFileException($"The bars worked out for this transcription no longer fit its notes: {exception.Message}");
                }
            }

            piece = Transcribed.Mark(piece);
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
                : MotifSelector.Select(notes, piece.Meter, saved.Span, saved.Filter, MotifSelection.MaxStructureBars);
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

        if (project.Check is null)
        {
            // A file from before version 12: the Check flags are worked out from the piece, resolved where the player confirmed the bar.
            project = project with { Check = project.Scan is { } scanned ? CheckFlag.FromScan(ScanCheck.Of(piece), scanned.ConfirmedBars) : [] };
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