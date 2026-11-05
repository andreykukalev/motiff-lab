namespace MotiffLab.MusicCore.Import;

/// <summary>
/// A PDF of printed music the player adds to read as it is (0023; Andrei, 2026-11-04): kept exactly as picked and shown page
/// by page, with no notes until it is recognised. music-core only recognises one; showing it is the apps' job.
/// </summary>
public static class PdfDocument
{
    /// <summary>
    /// The largest PDF the library keeps on the device: printed scores run to a few megabytes. The PDF is kept inside the
    /// project file, so a scanned book of hundreds of megabytes is better split.
    /// </summary>
    public const int MaxFileBytes = 50 * 1024 * 1024;

    /// <summary>The extension the file picker offers for it.</summary>
    public const string Extension = ".pdf";

    /// <summary>Whether the bytes are a PDF: they start with the "%PDF-" header, as every PDF does.</summary>
    public static bool IsPdf(ReadOnlySpan<byte> data) => data.StartsWith("%PDF-"u8);

    /// <summary>A rejection for a PDF too large or empty, so the caller need not read it first; null when its size is fine.</summary>
    public static ImportRejection? CheckSize(long bytes) => bytes switch
    {
        0 => PieceImporter.CheckSize(0),
        > MaxFileBytes => new ImportRejection(ImportRejectionReason.TooLarge, "This PDF is larger than 50 MB.", "Split it into one file per piece, or save it at a lower resolution.")
        {
            RussianMessage = "Этот PDF больше 50 МБ.",
            RussianTip = "Разделите его на файлы по одной пьесе или сохраните с меньшим разрешением.",
        },
        _ => null,
    };
}