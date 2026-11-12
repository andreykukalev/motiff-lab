using System.Text.Json;

using Microsoft.JSInterop;

using MotiffLab.MusicCore.Import;

namespace MotiffLab.Web.Library;

/// <summary>A folder the player chose (version 0.5, «Импорт папки»): its id on this device, its name, and whether the browser keeps access to it.</summary>
/// <param name="Kept">Chrome and Edge keep read access, so the pieces link the files in place; else the files are copied.</param>
public sealed record ChosenFolder(string Id, string Name, bool Kept, string? CheckedAt);

/// <summary>A notes file found in the folder: its path inside it (composed) and its size.</summary>
public sealed record ListedFile(string Path, long Bytes);

/// <summary>The notes files found, and how many other files were passed over unread.</summary>
public sealed record FolderListing(IReadOnlyList<ListedFile> Files, int Others);

/// <summary>What the worker read of a file: its fingerprint and, for a PDF, its pages, Title, Author, and whether it is locked or not a PDF.</summary>
public sealed record FileDetails(string Path, string? Sha256, PdfDetails? Info, bool Unreadable);

public sealed record PdfDetails(int? Pages, string? Title, string? Author, bool Locked, bool Damaged);

/// <summary>Where a linked piece's file is on this device: the folder and the path inside it.</summary>
public sealed record PieceLink(string Folder, string Path);

/// <summary>
/// The player's folders (version 0.5, «Импорт папки»): a thin handle on <c>Scripts/import/folder.ts</c>. Choosing a folder,
/// listing its notes files, reading their fingerprints and PDF details in a worker, and reading one file where it is. The
/// files are only read: nothing is written, renamed, moved or deleted, and nothing leaves the device.
/// </summary>
public sealed class PlayerFolders(IJSRuntime js) : IAsyncDisposable
{
    /// <summary>What the folder import reads: the notes files the app adds, by extension.</summary>
    public static readonly IReadOnlyList<string> NotesExtensions = [PdfDocument.Extension, .. PieceImporter.AcceptedExtensions.Split(',')];

    private IJSObjectReference? _module;

    /// <summary>Whether this browser keeps access to a folder (Chrome, Edge), so its files are linked and read in place.</summary>
    public async Task<bool> CanKeepAsync() => await (await ModuleAsync()).InvokeAsync<bool>("canKeepFolders");

    /// <summary>The folders chosen on this device, the last first.</summary>
    public async Task<IReadOnlyList<ChosenFolder>> KeptAsync() => await (await ModuleAsync()).InvokeAsync<ChosenFolder[]>("keptFolders");

    /// <summary>Chrome and Edge: the browser's folder picker, read only; null when the player closed it.</summary>
    public async Task<ChosenFolder?> ChooseAsync() => await (await ModuleAsync()).InvokeAsync<ChosenFolder?>("chooseFolder");

    /// <summary>Other browsers: the folder picked in a webkitdirectory input, its files kept for this visit; null when none.</summary>
    public async Task<ChosenFolder?> TakePickedAsync(Microsoft.AspNetCore.Components.ElementReference input) =>
        await (await ModuleAsync()).InvokeAsync<ChosenFolder?>("takePicked", input);

    /// <summary>"granted", "prompt" (a tap allows it again), "denied", or "none" (pick the folder again).</summary>
    public async Task<string> PermissionAsync(string folder) => await (await ModuleAsync()).InvokeAsync<string>("permission", folder);

    /// <summary>Asks the browser again for read access to the whole folder; only from the player's tap.</summary>
    public async Task<bool> AskPermissionAsync(string folder) => await (await ModuleAsync()).InvokeAsync<bool>("askPermission", folder);

    public async Task MarkCheckedAsync(string folder) => await (await ModuleAsync()).InvokeVoidAsync("markChecked", folder);

    /// <summary>The notes files in the folder with their sizes; <paramref name="listed"/> hears how many so far. Null without access.</summary>
    public async Task<FolderListing?> ListAsync(string folder, Action<int> listed)
    {
        using var callback = DotNetObjectReference.Create(new Progress(listed, null));
        try
        {
            return await (await ModuleAsync()).InvokeAsync<FolderListing>("listFolder", TimeSpan.FromHours(1), folder, NotesExtensions, callback);
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>Reads the fingerprints and PDF details of the files, a few at a time into <paramref name="read"/>.</summary>
    public async Task DetailsAsync(string folder, IEnumerable<(string Path, bool Pdf)> files, Action<IReadOnlyList<FileDetails>> read)
    {
        using var callback = DotNetObjectReference.Create(new Progress(null, read));
        object[] asked = [.. files.Select(static file => new { path = file.Path, pdf = file.Pdf })];
        await (await ModuleAsync()).InvokeVoidAsync("readDetails", TimeSpan.FromHours(6), folder, asked, callback);
    }

    public async Task StopDetailsAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("stopDetails");
        }
    }

    /// <summary>A file's bytes, read where it is; null when it is not there or cannot be read now.</summary>
    public async Task<byte[]?> ReadAsync(string folder, string path)
    {
        try
        {
            return await (await ModuleAsync()).InvokeAsync<byte[]?>("readFile", TimeSpan.FromMinutes(5), folder, path);
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>Roughly how many bytes this site may still keep; null when the browser does not say.</summary>
    public async Task<long?> FreeSpaceAsync() => await (await ModuleAsync()).InvokeAsync<long?>("freeSpace");

    /// <summary>Remembers where a piece's file is on this device, or forgets it with null.</summary>
    public async Task LinkAsync(string projectId, PieceLink? link) =>
        await (await ModuleAsync()).InvokeVoidAsync("linkPiece", projectId, link is null ? null : JsonSerializer.Serialize(link));

    public async Task<PieceLink?> LinkOfAsync(string projectId) => Link(await (await ModuleAsync()).InvokeAsync<string?>("linkedFolder", projectId));

    /// <summary>Every piece linked on this device.</summary>
    public async Task<IReadOnlyDictionary<string, PieceLink>> LinksAsync()
    {
        string[][] pairs = await (await ModuleAsync()).InvokeAsync<string[][]>("links");
        return pairs.Select(static pair => (pair[0], Link(pair[1]))).Where(static pair => pair.Item2 is not null).ToDictionary(static pair => pair.Item1, static pair => pair.Item2!);
    }

    /// <summary>A piece of state kept as text on this device (the import under way, the recognition queue).</summary>
    public async Task<string?> LoadStateAsync(string key) => await (await ModuleAsync()).InvokeAsync<string?>("loadState", key);

    public async Task SaveStateAsync(string key, string? value) => await (await ModuleAsync()).InvokeVoidAsync("saveState", key, value);

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

    private static PieceLink? Link(string? text)
    {
        try
        {
            return text is null ? null : JsonSerializer.Deserialize<PieceLink>(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/import/folder.js");

    /// <summary>The script's calls back while it lists a folder or reads its files.</summary>
    private sealed class Progress(Action<int>? listed, Action<IReadOnlyList<FileDetails>>? read)
    {
        [JSInvokable]
        public void OnListed(int count) => listed?.Invoke(count);

        [JSInvokable]
        public void OnDetails(FileDetails[] details) => read?.Invoke(details);
    }
}