using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Projects;

namespace MotiffLab.Web.Library;

/// <summary>
/// The pieces picked in the library to be read (version 0.5, ux-prototype/v0.5/screens/import-web-4-consent.png): while
/// picking, the list has a box on each PDF piece with no notes, and «Выбрано 8 пьес · 41 страница» above it. The pages are
/// those the library row keeps for the PDF; one when it was not counted.
/// </summary>
public sealed class RecognitionPick(ProjectStore projects)
{
    private readonly Dictionary<string, string> _picked = new(StringComparer.Ordinal);
    private Dictionary<string, int> _pages = new(StringComparer.Ordinal);

    public bool Picking { get; private set; }

    public IReadOnlyCollection<string> Picked => _picked.Keys;

    /// <summary>After each change, so the list and its bar show it.</summary>
    public event Action? Changed;

    /// <summary>Starts picking, with the pages of each PDF from the library's rows.</summary>
    public async Task StartAsync()
    {
        (IReadOnlyList<LibraryOriginal> originals, _) = await projects.LibraryOriginalsAsync();
        _pages = originals.Where(static original => original.OriginalId == PieceOriginal.DocumentId && original.Pages is > 0)
            .GroupBy(static original => original.ProjectId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().Pages!.Value, StringComparer.Ordinal);
        _picked.Clear();
        Picking = true;
        Changed?.Invoke();
    }

    public void Stop()
    {
        Picking = false;
        _picked.Clear();
        Changed?.Invoke();
    }

    /// <summary>Only a PDF read as it is, with no notes yet, can be picked.</summary>
    public static bool CanPick(LibraryEntry entry) => entry?.Summary.Origin == PieceOrigin.Pdf;

    public bool IsPicked(string projectId) => _picked.ContainsKey(projectId);

    public async Task ToggleAsync(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (CanPick(entry) && !_picked.Remove(entry.Summary.Id))
        {
            _picked[entry.Summary.Id] = entry.Title;
            await CountAsync(entry.Summary.Id);
        }

        Changed?.Invoke();
    }

    /// <summary>Picks every piece listed that can be, or none when all of them are.</summary>
    public async Task ToggleAllAsync(IEnumerable<LibraryEntry> listed)
    {
        LibraryEntry[] pickable = [.. listed.Where(CanPick)];
        bool all = pickable.All(entry => _picked.ContainsKey(entry.Summary.Id));
        foreach (LibraryEntry entry in pickable)
        {
            if (all)
            {
                _picked.Remove(entry.Summary.Id);
            }
            else
            {
                _picked[entry.Summary.Id] = entry.Title;
                await CountAsync(entry.Summary.Id);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>The pages of a PDF kept in its piece whose row does not say, counted once from the PDF.</summary>
    private async Task CountAsync(string projectId)
    {
        if (_pages.ContainsKey(projectId))
        {
            return;
        }

        try
        {
            if (await projects.LoadAsync(projectId) is { Document: { } pdf } && PdfDocument.PageCount(pdf) is > 0 and var count)
            {
                _pages[projectId] = count;
            }
        }
        catch (ProjectFileException)
        {
            // A damaged piece counts as one page; reading it says what is wrong.
        }
    }

    public int PagesOf(string projectId) => _pages.GetValueOrDefault(projectId, 1);

    public int PagesPicked => _picked.Keys.Sum(PagesOf);

    /// <summary>The picked pieces, in the order they were picked, to send.</summary>
    public IReadOnlyList<PieceToRead> ToRead => [.. _picked.Select(picked => new PieceToRead(picked.Key, picked.Value, PagesOf(picked.Key)))];
}