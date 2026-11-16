using System.Text.Json;

using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Library;

/// <summary>Where one file of an import is: waiting, added as a piece, or not added and why (a key of the words).</summary>
public enum JobFileState
{
    Waiting,
    Added,
    Failed,
}

/// <summary>One file of the import, with what the player added it with: the title, composer, tags and playlists.</summary>
public sealed record JobFile(string Path, long Bytes, string? Sha256, int? Pages, string? Title, string? Composer, IReadOnlyList<string> Tags, IReadOnlyList<string> Playlists)
{
    public JobFileState State { get; init; }

    /// <summary>Why it was not added: a key of the words.</summary>
    public string? Reason { get; init; }

    /// <summary>The piece it was added as, its id chosen before it is saved, so a stop and a resume never add it twice.</summary>
    public string? Project { get; init; }
}

/// <summary>A file the library has already, linked on this device where it is, and given the folder's playlists and tags.</summary>
public sealed record JobKnown(string Project, string Path, IReadOnlyList<string> Tags, IReadOnlyList<string> Playlists);

/// <summary>The import under way, kept on this device so it goes on after the page is closed: «Импорт остановлен на 214 из 628 — продолжить?».</summary>
public sealed record JobState(string Folder, string FolderName, bool Linked, IReadOnlyList<JobFile> Files, IReadOnlyList<JobKnown> Known)
{
    public bool Paused { get; init; }

    /// <summary>«Распознать ноты: сразу для всех»: each PDF added goes to the batch the player agreed to (version 0.5).</summary>
    public bool Recognise { get; init; }

    public int Done => Files.Count(static file => file.State != JobFileState.Waiting);

    public int Added => Files.Count(static file => file.State == JobFileState.Added);

    public int Failed => Files.Count(static file => file.State == JobFileState.Failed);

    public bool Finished => Done == Files.Count;
}

/// <summary>
/// The folder import's job (version 0.5, «Импорт папки»): adds the files the player confirmed one at a time, by the same way
/// as Open a file (a PDF as it is, linked where it is or copied; MIDI and MusicXML read into notes), with the title,
/// composer, tags and playlists confirmed in the row, never over what the player wrote. It can be paused and stopped,
/// and goes on after a reload from the next file, since its state is kept on the device after each one. The files are
/// only read.
/// </summary>
public sealed class ImportJob(PlayerFolders folders, ProjectStore projects, RecognitionQueue recognition)
{
    /// <summary>The experiment's switch in Settings · Experimental («Импорт папки», version 0.5), off by default.</summary>
    public const string Setting = "experimental.libraryImport";

    private const string Key = "import.job";

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private bool _running;
    private bool _stop;

    public JobState? State { get; private set; }

    /// <summary>Whether a file is being added now; else the job, if any, waits for the player.</summary>
    public bool Running => _running;

    /// <summary>After each file and each change of state.</summary>
    public event Action? Changed;

    /// <summary>The job kept on this device, as the page was left; it does not start by itself.</summary>
    public async Task LoadAsync()
    {
        if (State is not null)
        {
            return;
        }

        try
        {
            State = await folders.LoadStateAsync(Key) is { } text ? JsonSerializer.Deserialize<JobState>(text, s_json) : null;
        }
        catch (JsonException)
        {
            State = null;
        }
    }

    /// <summary>Starts an import of the files the player confirmed, the files the library has linked first.</summary>
    public async Task StartAsync(JobState state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        await SaveAsync();
        await RunAsync();
    }

    /// <summary>Goes on from the next file; the folder must be readable (the caller asks the browser first).</summary>
    public async Task ResumeAsync()
    {
        if (State is { } state)
        {
            State = state with { Paused = false };
            await SaveAsync();
            await RunAsync();
        }
    }

    /// <summary>Stops after the file being added; what was added stays.</summary>
    public async Task PauseAsync()
    {
        _stop = true;
        if (State is { } state)
        {
            State = state with { Paused = true };
            await SaveAsync();
        }

        Changed?.Invoke();
    }

    /// <summary>Stops for good, or puts away a finished import's line: what was added stays.</summary>
    public async Task CloseAsync()
    {
        _stop = true;
        State = null;
        await folders.SaveStateAsync(Key, null);
        Changed?.Invoke();
    }

    private async Task RunAsync()
    {
        if (_running || State is null)
        {
            return;
        }

        _running = true;
        _stop = false;
        Changed?.Invoke();
        try
        {
            await LinkKnownAsync();
            while (!_stop && State is { } state && state.Files.FirstOrDefault(static file => file.State == JobFileState.Waiting) is { } waiting)
            {
                // The piece's id is kept before it is saved, so a page closed in between never adds the file twice.
                JobFile next = waiting.Project is null ? waiting with { Project = MusicCoreFacade.NewProjectId() } : waiting;
                State = state = state with { Files = [.. state.Files.Select(file => file.Path == next.Path ? next : file)] };
                await SaveAsync();
                JobFile done = await AddAsync(state, next);
                State = state with { Files = [.. state.Files.Select(file => file.Path == next.Path ? done : file)] };
                await SaveAsync();
                Changed?.Invoke();
            }
        }
        finally
        {
            _running = false;
            Changed?.Invoke();
        }
    }

    /// <summary>The files the library has: linked here where they are, given the folder's playlists and tags; once.</summary>
    private async Task LinkKnownAsync()
    {
        if (State is not { Known.Count: > 0 } state)
        {
            return;
        }

        foreach (JobKnown known in state.Known)
        {
            try
            {
                if (await projects.LoadAsync(known.Project) is { } project)
                {
                    Project given = project with { Metadata = LibraryImport.Confirmed(project.Metadata, null, null, known.Tags, known.Playlists) };
                    if (state.Linked && given.LinksDocument)
                    {
                        given = given.MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Web, held: true, DateTimeOffset.Now);
                        await folders.LinkAsync(known.Project, new PieceLink(state.Folder, known.Path));
                    }

                    if (given != project)
                    {
                        await projects.SaveAsync(given);
                    }
                }
            }
            catch (ProjectFileException)
            {
                // A damaged piece stays as it is; opening it says what is wrong.
            }
        }

        State = state with { Known = [] };
        await SaveAsync();
    }

    /// <summary>Adds one file as a piece; the file as it is then, with why when it could not be added.</summary>
    private async Task<JobFile> AddAsync(JobState state, JobFile file)
    {
        string id = file.Project!;
        if (await Exists(id))
        {
            return file with { State = JobFileState.Added };
        }

        byte[]? data = await folders.ReadAsync(state.Folder, file.Path);
        if (data is null || data.Length == 0)
        {
            return file with { State = JobFileState.Failed, Reason = "folder.problemGone" };
        }

        (Project? project, string? problem) = FolderPieces.Make(id, file, data, state.Linked, DateTimeOffset.Now);
        if (project is null)
        {
            return file with { State = JobFileState.Failed, Reason = problem };
        }

        await projects.SaveAsync(project);
        if (state.Linked && project.LinksDocument)
        {
            await folders.LinkAsync(id, new PieceLink(state.Folder, file.Path));
        }

        if (state.Recognise && !project.HasNotes)
        {
            await recognition.SendAsync([new PieceToRead(id, file.Title ?? Path.GetFileNameWithoutExtension(file.Path), file.Pages ?? 1)]);
        }

        return file with { State = JobFileState.Added };
    }

    private async Task<bool> Exists(string id)
    {
        try
        {
            return await projects.LoadAsync(id) is not null;
        }
        catch (ProjectFileException)
        {
            return true;
        }
    }

    private async Task SaveAsync() => await folders.SaveStateAsync(Key, State is null ? null : JsonSerializer.Serialize(State, s_json));
}