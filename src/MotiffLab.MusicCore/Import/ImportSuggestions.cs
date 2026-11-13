using MotiffLab.MusicCore.Projects;

namespace MotiffLab.MusicCore.Import;

/// <summary>
/// The title, composer, playlists and tags suggested for each file of a folder import (version 0.5), in the order of
/// trust: the folder mapping the player chose, the MusicXML header, the file's name, then the folder's name or the other
/// pieces in it. Each is a guess (provenance inferred, <see cref="Suggestion"/>). The PDF's own Title and Author are not
/// used (Andrei, version 0.5): files cut from one book carry the book's, so many files looked like one piece.
/// </summary>
internal sealed class ImportSuggestions
{
    private const double Chosen = 0.8;
    private const double Header = 0.9;
    private const double Named = 0.6;
    private const double Unsure = 0.4;
    private const double NameOnly = 0.3;

    private readonly FolderMapping _folders;
    private readonly Dictionary<string, string> _playlists;
    private readonly Dictionary<string, string> _agreed;
    private readonly HashSet<string> _alone;

    public ImportSuggestions(IReadOnlyList<FoundFile> files, FolderMapping folders)
    {
        _folders = folders;
        _playlists = PlaylistNames(files);
        _agreed = AgreedComposers(files);
        _alone = [.. files.Where(static file => file.Kind is not null).GroupBy(static file => file.Folder, StringComparer.Ordinal)
            .Where(static folder => folder.Count() == 1).Select(static folder => folder.Single().Path)];
    }

    /// <summary>The file as a new piece with its suggestions.</summary>
    public PlannedFile For(FoundFile file)
    {
        string[] segments = file.Folder.Length > 0 ? file.Folder.Split('/') : [];
        return new PlannedFile(file, ImportStatus.New)
        {
            Title = TitleOf(file),
            Composer = ComposerOf(file, segments),
            Playlists = _folders == FolderMapping.Playlists && segments.Length > 0 ? [_playlists[file.Folder]] : [],
            Tags = _folders == FolderMapping.Tags ? [.. segments.Distinct(StringComparer.OrdinalIgnoreCase)] : [],
            PossibleCollection = file.Kind == OriginalKind.Pdf && IsPossibleCollection(file, segments),
        };
    }

    /// <summary>
    /// A PDF that is perhaps several pieces, a guess: more than <see cref="LibraryImport.CollectionPages"/> pages, a name
    /// that says so (<see cref="FileNameDetails.NamesCollection"/>), or alone in a folder whose name says so.
    /// </summary>
    private bool IsPossibleCollection(FoundFile file, string[] segments) =>
        file.Pages > LibraryImport.CollectionPages
        || FileNameDetails.NamesCollection(PieceDetails.TitleOf(file.Name))
        || (segments.Length > 0 && _alone.Contains(file.Path) && FileNameDetails.NamesCollection(segments[^1]));

    private static Suggestion TitleOf(FoundFile file)
    {
        if (Clean(file.Header.Title) is { } written)
        {
            return new(written, SuggestionSource.ScoreHeader, Header);
        }

        return FileNameDetails.Of(file.Name).Title is { } named
            ? new(named, SuggestionSource.FileName, Named)
            : new(PieceDetails.TitleOf(file.Name), SuggestionSource.FileName, NameOnly);
    }

    private Suggestion? ComposerOf(FoundFile file, string[] segments)
    {
        if (_folders == FolderMapping.Composers && segments.Length > 0)
        {
            return new(segments[0], SuggestionSource.FolderChoice, Chosen);
        }

        if (OwnComposer(file) is { } own)
        {
            return own;
        }

        for (int index = segments.Length - 1; index >= 0; index--)
        {
            if (FileNameDetails.ComposerOfFolder(segments[index]) is { } folder)
            {
                return new(folder, SuggestionSource.Folder, Named);
            }
        }

        return _agreed.TryGetValue(file.Folder, out string? agreed) ? new(agreed, SuggestionSource.Folder, Unsure) : null;
    }

    /// <summary>The composer the file itself suggests: its header, then its name.</summary>
    private static Suggestion? OwnComposer(FoundFile file)
    {
        if (Clean(file.Header.Composer) is { } written)
        {
            return new(written, SuggestionSource.ScoreHeader, Header);
        }

        return FileNameDetails.Of(file.Name).Composer is { } named
            ? new(named, SuggestionSource.FileName, ComposerNames.IsKnown(named) ? Named : Unsure)
            : null;
    }

    /// <summary>
    /// The playlist each folder becomes: its own name, or its path ("Бах / Прелюдии") when another folder has the same
    /// name, so two folders never fall into one playlist.
    /// </summary>
    private static Dictionary<string, string> PlaylistNames(IReadOnlyList<FoundFile> files)
    {
        string[] folders = [.. files.Select(static file => file.Folder).Where(static folder => folder.Length > 0).Distinct(StringComparer.Ordinal)];
        ILookup<string, string> byName = folders.ToLookup(static folder => NameText.Folded(LastOf(folder)), StringComparer.Ordinal);
        return folders.ToDictionary(
            static folder => folder,
            folder => byName[NameText.Folded(LastOf(folder))].Count() > 1 ? folder.Replace("/", " / ", StringComparison.Ordinal) : LastOf(folder),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// For each folder whose pieces name one composer and no other (two of them at least), that composer: the others in
    /// the folder are suggested it.
    /// </summary>
    private static Dictionary<string, string> AgreedComposers(IReadOnlyList<FoundFile> files)
    {
        var agreed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (IGrouping<string, FoundFile> folder in files.Where(static file => file.Kind is not null).GroupBy(static file => file.Folder, StringComparer.Ordinal))
        {
            string[] named = [.. folder.Select(OwnComposer).OfType<Suggestion>().Select(static composer => composer.Value)];
            if (named.Length >= 2 && named.Select(NameText.Folded).Distinct(StringComparer.Ordinal).Count() == 1)
            {
                agreed[folder.Key] = named[0];
            }
        }

        return agreed;
    }

    private static string LastOf(string folder) => folder[(folder.LastIndexOf('/') + 1)..];

    private static string? Clean(string? text) => text is null ? null : PieceMetadata.Clean(NameText.Composed(text));
}