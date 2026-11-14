namespace MotiffLab.MusicCore.Projects;

/// <summary>A kind of source a piece really has, in the order the library shows them.</summary>
public enum LibrarySource
{
    /// <summary>A PDF of the score, kept in the piece or read where it lies.</summary>
    Pdf,

    /// <summary>Notes: imported, recognised from the PDF or heard in a recording.</summary>
    Notes,

    /// <summary>A recording's sound kept as a version of the piece.</summary>
    Recording,

    /// <summary>Photos of the pages, kept as the piece's pages.</summary>
    Photos,

    /// <summary>A MIDI file kept as a version of the piece.</summary>
    Midi,
}

/// <summary>
/// The sources a piece has, for the library's row of small icons (v0.5.6, #533 section 10): read from what the piece's
/// summary already holds (no change to the project file), only what is really there, never what the app could add, in a
/// fixed order: PDF, notes, recording, then any other. A new kind of source is one more value here and one more icon.
/// </summary>
/// <remarks>
/// A recording heard into notes keeps no sound in the piece, so its notes show and no recording does. Notes read from a PDF
/// piece that the library hides behind them (0023) show the PDF too, since PDF | Notes opens it.
/// </remarks>
public static class LibrarySources
{
    /// <summary>The piece's sources, each once, in the order of <see cref="LibrarySource"/>.</summary>
    public static IReadOnlyList<LibrarySource> Of(ProjectSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        HashSet<LibrarySource> has = [];
        if (summary.ReadFrom is not null)
        {
            has.Add(LibrarySource.Pdf);
        }

        if (summary.Origin != PieceOrigin.Pdf)
        {
            has.Add(LibrarySource.Notes);
        }

        foreach (OriginalSummary original in summary.Originals)
        {
            LibrarySource? source = original.Kind switch
            {
                OriginalKind.Pdf => LibrarySource.Pdf,
                OriginalKind.Images => LibrarySource.Photos,
                OriginalKind.Audio => LibrarySource.Recording,
                // The file the notes were imported from is the notes; another MIDI file is a version of its own.
                OriginalKind.Midi when original.Id != PieceOriginal.SourceFileId => LibrarySource.Midi,
                _ => null,
            };
            if (source is { } found)
            {
                has.Add(found);
            }
        }

        return [.. Enum.GetValues<LibrarySource>().Where(has.Contains)];
    }
}