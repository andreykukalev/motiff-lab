using System.Globalization;
using System.Text;

using Microsoft.JSInterop;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Account;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Projects;

/// <summary>
/// Projects saved on the device (S1-P1): a thin C# handle on the IndexedDB wrapper in
/// <c>Scripts/storage</c>. Each project is stored as its project file, so what autosave keeps and
/// what export hands over are the same bytes. Nothing leaves the device unless the player signed in and
/// turned saving online on (0014); then each save and delete is repeated online a moment later.
/// </summary>
public sealed class ProjectStore(IJSRuntime js, OnlineProjects online) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private IJSObjectReference? _originals;

    /// <summary>
    /// Saves on this device, and online when saving online is on. A PDF (0023) stays on the device: it can run to tens of
    /// megabytes, more than an online copy holds. So the online copy of a piece with notes and a PDF holds the notes and
    /// what was made from them, without the PDF (testing item 16), and a piece that is only a PDF is not saved online.
    /// </summary>
    public async Task SaveAsync(Project project)
    {
        await SaveHereAsync(project);
        if (project.ForOnline() is { } copy)
        {
            // The PDF stays here (version 0.3): the copy says this browser holds it, so another device shows where it is.
            if (project.Document is not null && copy.FindOriginal(PieceOriginal.DocumentId) is { } pdf && !pdf.IsHeldBy(HoldingDevice.Web))
            {
                copy = copy.MarkHeld(PieceOriginal.DocumentId, HoldingDevice.Web, held: true, DateTimeOffset.Now);
            }

            await online.QueueAsync(copy);
        }
    }

    /// <summary>Saves on this device only, as for a project just fetched from its online copy.</summary>
    public async Task SaveHereAsync(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        IJSObjectReference module = await ModuleAsync();
        await module.InvokeVoidAsync("saveProject", project.Id, Encoding.UTF8.GetString(ProjectFile.Write(project)), Row(project));
    }

    /// <summary>
    /// Writes a saved project's library row again from the project, as it is: for rows saved before the row kept the title,
    /// composer and tags (version 0.2), so the library can search and filter them. The project file is not touched.
    /// </summary>
    public async Task<ProjectSummary> SaveSummaryAsync(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        IJSObjectReference module = await ModuleAsync();
        await module.InvokeVoidAsync("saveSummary", Row(project));
        return project.Summary;
    }

    private static StoredSummary Row(Project project)
    {
        ProjectSummary summary = project.Summary;
        return new(summary.Id, summary.FileName, summary.MotifCount, summary.VariationCount, Iso(summary.LastOpened), summary.ResumeCount, summary.IdeaCount)
        {
            Origin = summary.Origin.ToString(),
            CheckBars = summary.CheckBars,
            ReadFrom = summary.ReadFrom,
            Latest = summary.Latest is { } latest ? new StoredResume(latest.Id, latest.Name, latest.At, latest.From, latest.To, Iso(latest.LastActive)) : null,
            Title = summary.Details?.Title.Value,
            TitleFrom = summary.Details?.Title.Attribution.Provenance.ToString(),
            Composer = summary.Details?.Composer?.Value,
            ComposerFrom = summary.Details?.Composer?.Attribution.Provenance.ToString(),
            Tags = summary.Details?.Tags,
            Playlists = summary.Details?.Playlists,
            Originals = [.. summary.Originals.Select(original => StoredOriginalOf(original, project.FindOriginal(original.Id)))],
            Part = StoredPartOf(summary.Part),
        };
    }

    /// <summary>An original as its row keeps it: where its bytes are, and (version 0.5) its name, folder, fingerprint, size and pages.</summary>
    private static StoredOriginal StoredOriginalOf(OriginalSummary original, PieceOriginal? described) =>
        new(original.Id, original.Kind.ToString(), original.Kept.ToString(), original.InFile, [.. original.Held.Select(static device => device.ToString())])
        {
            FileName = described?.FileName,
            Folder = described?.Folder,
            Sha256 = described?.Sha256,
            Bytes = described?.Bytes,
            Pages = described?.Pages,
        };

    /// <summary>The saved project, or null when there is none. Throws <see cref="ProjectFileException"/> when it is damaged.</summary>
    public async Task<Project?> LoadAsync(string id)
    {
        IJSObjectReference module = await ModuleAsync();
        string? file = await module.InvokeAsync<string?>("loadProject", id);
        return file is null ? null : ProjectFile.Read(Encoding.UTF8.GetBytes(file));
    }

    /// <summary>Saved projects, most recently opened first.</summary>
    public async Task<IReadOnlyList<ProjectSummary>> ListAsync()
    {
        IJSObjectReference module = await ModuleAsync();
        StoredSummary[] rows = await module.InvokeAsync<StoredSummary[]>("listProjects");
        return [.. rows.Select(row => new ProjectSummary(
            row.Id,
            row.FileName,
            row.MotifCount,
            row.VariationCount,
            Time(row.LastOpened),
            row.ResumeCount,
            row.IdeaCount)
        {
            Origin = Enum.TryParse(row.Origin, out PieceOrigin origin) ? origin : PieceOrigin.File,
            CheckBars = row.CheckBars,
            ReadFrom = row.ReadFrom,
            Latest = row.Latest is { } latest ? new ResumeSummary(latest.Id, latest.Name, latest.At, latest.From, latest.To, Time(latest.LastActive)) : null,
            Details = DetailsOf(row),
            Originals = OriginalsOf(row),
            Part = PartSummaryOf(row.Part),
        })];
    }

    /// <summary>
    /// The library's originals as the rows keep them (version 0.5, «Импорт папки»), each with its piece's title and composer,
    /// for the import plan to know the files the library has and for «Проверить папку» to find them again; and the pieces
    /// whose rows were saved before they kept them, to read once.
    /// </summary>
    public async Task<(IReadOnlyList<LibraryOriginal> Originals, IReadOnlyList<string> Unknown)> LibraryOriginalsAsync()
    {
        IJSObjectReference module = await ModuleAsync();
        StoredSummary[] rows = await module.InvokeAsync<StoredSummary[]>("listProjects");
        List<LibraryOriginal> originals = [];
        List<string> unknown = [];
        foreach (StoredSummary row in rows)
        {
            if ((row.Originals ?? []).Any(static original => original.FileName is null))
            {
                unknown.Add(row.Id);
            }

            originals.AddRange((row.Originals ?? []).Where(static original => original.FileName is not null).Select(original => new LibraryOriginal(row.Id, original.Id, original.Sha256)
            {
                Bytes = original.Bytes,
                Pages = original.Pages,
                FileName = original.FileName,
                Folder = original.Folder,
                Title = row.Title ?? ScoreTitle.Of(row.FileName),
                Composer = row.Composer,
            }));
        }

        return (originals, unknown);
    }

    /// <summary>
    /// The recordings this browser holds, as "&lt;project&gt;/&lt;original&gt;" (version 0.3), and the PDFs it reads where the
    /// player keeps them (version 0.5), for the library's badges; none when the browser does not say.
    /// </summary>
    public async Task<IReadOnlySet<string>> HeldHereAsync()
    {
        try
        {
            _originals ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/storage/originals.js");
            return new HashSet<string>(await _originals.InvokeAsync<string[]>("heldHereWithLinks"), StringComparer.Ordinal);
        }
        catch (JSException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>The notes read from a PDF piece (0023), the ones opened last when it was read more than once; null when there are none.</summary>
    public async Task<ProjectSummary?> NotesReadFromAsync(string pdfId) =>
        (await ListAsync()).Where(summary => summary.ReadFrom == pdfId).OrderByDescending(summary => summary.LastOpened).FirstOrDefault();

    /// <summary>The covers saved, by project; each page is an image address that holds until the next listing.</summary>
    public async Task<IReadOnlyDictionary<string, PieceCover>> CoversAsync()
    {
        IJSObjectReference module = await ModuleAsync();
        PieceCover[] rows = await module.InvokeAsync<PieceCover[]>("listCovers");
        return rows.ToDictionary(row => row.Id);
    }

    /// <summary>Lets go of the covers' image addresses, when the library closes.</summary>
    public async Task ReleaseCoversAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("releaseCovers");
        }
    }

    public async Task DeleteAsync(string id)
    {
        IJSObjectReference module = await ModuleAsync();
        await module.InvokeVoidAsync("deleteProject", id);
        // The pencil marks on its score go with it (0015).
        await module.InvokeVoidAsync("deleteMarks", PencilMarks.ProjectKey(id));
        // So do the recordings kept on this device for its versions (0024, scenario 2).
        await module.InvokeVoidAsync("deleteRecordings", id);
        await online.DeleteAsync(id);
    }

    /// <summary>True when the browser says this site's storage is nearly full; false when it does not say.</summary>
    public async Task<bool> IsNearlyFullAsync()
    {
        IJSObjectReference module = await ModuleAsync();
        StorageUse? use = await module.InvokeAsync<StorageUse?>("storageUse");
        return use?.NearlyFull ?? false;
    }

    /// <summary>Hands the player the project file to save as a backup (AC-S1-20).</summary>
    public async Task ExportAsync(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        IJSObjectReference module = await ModuleAsync();
        string name = Path.GetFileNameWithoutExtension(project.FileName);
        await module.InvokeVoidAsync("downloadText", name + ProjectFile.Extension, Encoding.UTF8.GetString(ProjectFile.Write(project)));
    }

    /// <summary>Hands the player a file made on the device, such as a MIDI export (S1-X1).</summary>
    public async Task DownloadAsync(string fileName, byte[] data, string contentType)
    {
        IJSObjectReference module = await ModuleAsync();
        await module.InvokeVoidAsync("downloadBytes", fileName, data, contentType);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    /// <summary>The title, composer, tags and playlists a row keeps; null for a row saved before it kept them, or one this build cannot read.</summary>
    private static PieceDetails? DetailsOf(StoredSummary row) =>
        row is { Title: { } title, TitleFrom: { } titleFrom } && Attributed(title, titleFrom) is { } shown
            ? new PieceDetails(shown, row is { Composer: { } composer, ComposerFrom: { } composerFrom } ? Attributed(composer, composerFrom) : null, row.Tags ?? [], row.Playlists ?? [])
            : null;

    /// <summary>The originals a row keeps (version 0.3); none for a row saved before, or a value this build does not know.</summary>
    private static IReadOnlyList<OriginalSummary> OriginalsOf(StoredSummary row) =>
        [.. (row.Originals ?? []).Where(static original => Enum.TryParse<OriginalKind>(original.Kind, out _) && Enum.TryParse<OriginalKept>(original.Kept, out _))
            .Select(static original => new OriginalSummary(
                original.Id,
                Enum.Parse<OriginalKind>(original.Kind),
                Enum.Parse<OriginalKept>(original.Kept),
                original.InFile,
                [.. (original.Held ?? []).Select(static device => Enum.TryParse(device, out HoldingDevice known) ? known : (HoldingDevice?)null).OfType<HoldingDevice>()]))];

    private static Attributed<string>? Attributed(string value, string provenance) =>
        Enum.TryParse(provenance, out Provenance kind) && kind is Provenance.UserEdited or Provenance.Imported or Provenance.Inferred
            ? new(value, kind == Provenance.Inferred ? Attribution.Inferred(1) : kind == Provenance.Imported ? Attribution.Imported : Attribution.UserEdited)
            : null;

    /// <summary>UTC, so the script can order rows by comparing the strings.</summary>
    private static StoredPart? StoredPartOf(PartSummary? part) =>
        part is { Original: var book, Place: var place }
            ? new StoredPart(book.Kind.ToString(), book.FileName, book.Name, Iso(book.AddedAt), book.Sha256, place?.Pages?.First, place?.Pages?.Last, place?.Milliseconds?.From, place?.Milliseconds?.To)
            : null;

    /// <summary>
    /// A piece made from a collection as its row keeps it (version 0.4): the collection's original and its place there;
    /// null for a piece of its own, and for a row this build cannot read, which then reads as one.
    /// </summary>
    private static PartSummary? PartSummaryOf(StoredPart? part)
    {
        if (part is null || !Enum.TryParse(part.Kind, out OriginalKind kind) || string.IsNullOrWhiteSpace(part.FileName)
            || !DateTimeOffset.TryParse(part.AddedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset added))
        {
            return null;
        }

        try
        {
            var original = new PieceOriginal(PieceParts.OriginalId, kind, part.FileName, OriginalKept.Device, added) { Name = part.Name, Sha256 = part.Sha256 };
            PartOf? place = part is { FirstPage: { } first, LastPage: { } last } ? PartOf.OnPages(PieceParts.OriginalId, first, last)
                : part is { From: { } from, To: { } to } ? PartOf.InRecording(PieceParts.OriginalId, from, to)
                : null;
            return new PartSummary(original, place);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string Iso(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/storage/project-store.js");

    private static DateTimeOffset Time(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>A summary row as stored; rows saved before resume points existed have no counts of them, so they read as 0.</summary>
    private sealed record StoredSummary(string Id, string FileName, int MotifCount, int VariationCount, string LastOpened, int ResumeCount = 0, int IdeaCount = 0)
    {
        public string? Origin { get; init; }

        public int CheckBars { get; init; }

        public StoredResume? Latest { get; init; }

        public string? ReadFrom { get; init; }

        /// <summary>The title, composer, tags and playlists the card shows, with where each comes from (a provenance name); rows saved before version 0.2 have none.</summary>
        public string? Title { get; init; }

        public string? TitleFrom { get; init; }

        public string? Composer { get; init; }

        public string? ComposerFrom { get; init; }

        public IReadOnlyList<string>? Tags { get; init; }

        public IReadOnlyList<string>? Playlists { get; init; }

        /// <summary>The piece's originals and where their bytes are (version 0.3); rows saved before have none.</summary>
        public IReadOnlyList<StoredOriginal>? Originals { get; init; }

        /// <summary>For a piece made from a collection, the collection's original and the piece's place there (version 0.4); rows saved before have none.</summary>
        public StoredPart? Part { get; init; }
    }

    private sealed record StoredPart(string Kind, string FileName, string? Name, string AddedAt, string? Sha256, int? FirstPage, int? LastPage, long? From, long? To);

    private sealed record StoredOriginal(string Id, string Kind, string Kept, bool InFile, IReadOnlyList<string>? Held)
    {
        /// <summary>The original's name, folder, fingerprint, size and pages (version 0.5, «Импорт папки»); rows saved before have none.</summary>
        public string? FileName { get; init; }

        public string? Folder { get; init; }

        public string? Sha256 { get; init; }

        public long? Bytes { get; init; }

        public int? Pages { get; init; }
    }

    private sealed record StoredResume(string Id, string? Name, long At, long? From, long? To, string LastActive);

    private sealed record StorageUse(double Usage, double Quota, bool NearlyFull);
}