using MotiffLab.MusicCore.Import.Scan;

namespace MotiffLab.MusicCore.Projects;

/// <summary>Which import raised a Check flag.</summary>
public enum CheckSource
{
    Scan,
    Transcription,
}

/// <summary>
/// Why a bar is worth checking (0018, section 9): the scan checks of 0011 and the doubts of a
/// transcription (preparation item 12), one list so the Check layer reads them the same way.
/// </summary>
public enum CheckReason
{
    /// <summary>The notes add up to more than the time signature allows (scan).</summary>
    BarTooLong,

    /// <summary>The notes add up to less than the time signature (scan).</summary>
    BarTooShort,

    /// <summary>The two hands do not add up to the same length (scan).</summary>
    StavesDiffer,

    /// <summary>A note is outside the piano's range (scan).</summary>
    OutOfPianoRange,

    /// <summary>The bar has grace notes, which are often misread (scan).</summary>
    GraceNotes,

    /// <summary>The bar lies where two parts of a long recording were joined (transcription).</summary>
    PartSeam,

    /// <summary>The tempo worked out from the playing fits the bar poorly (transcription).</summary>
    TempoUncertain,

    /// <summary>Many notes close together, where a transcription often hears extra ones (transcription).</summary>
    DenseCluster,

    /// <summary>The hands were guessed from the pitches alone (transcription).</summary>
    HandsGuessed,
}

/// <summary>
/// One Check flag (0018, section 9; preparation item 6): a bar, why it is doubtful, which import said so, and
/// whether the player has resolved it. One shape for scans and transcriptions; the Check layer shows the open
/// flags and disappears when every flag is resolved.
/// </summary>
/// <param name="Bar">The bar number, as the workspace shows it.</param>
public sealed record CheckFlag(int Bar, CheckSource Source, CheckReason Reason, bool Resolved)
{
    public int Bar { get; } = Bar >= 0 ? Bar : throw new ArgumentOutOfRangeException(nameof(Bar), Bar, "A bar number is not negative.");

    /// <summary>The flags of a scan's check, one per flagged bar and reason, resolved where the player confirmed the bar.</summary>
    public static IReadOnlyList<CheckFlag> FromScan(ScanCheck check, IReadOnlyCollection<int> confirmedBars)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(confirmedBars);
        return [.. check.Flagged.SelectMany(bar => bar.Flags.Select(flag => new CheckFlag(bar.Bar, CheckSource.Scan, Of(flag), confirmedBars.Contains(bar.Bar))))];
    }

    /// <summary>The reason as the Check layer says it, in the language.</summary>
    public string Describe(Words words) => Describe(Reason, words);

    public static string Describe(CheckReason reason, Words words) => reason switch
    {
        CheckReason.BarTooLong => ScanBar.Describe(ScanFlag.BarTooLong, words),
        CheckReason.BarTooShort => ScanBar.Describe(ScanFlag.BarTooShort, words),
        CheckReason.StavesDiffer => ScanBar.Describe(ScanFlag.StavesDiffer, words),
        CheckReason.OutOfPianoRange => ScanBar.Describe(ScanFlag.OutOfPianoRange, words),
        CheckReason.GraceNotes => ScanBar.Describe(ScanFlag.GraceNotes, words),
        CheckReason.PartSeam => words.T(
            "Two parts of the recording were joined here, so a note may be missing or doubled.",
            "Здесь склеены две части записи: нота могла потеряться или удвоиться."),
        CheckReason.TempoUncertain => words.T(
            "The tempo worked out from the playing fits this bar poorly, so its rhythm may be written wrong.",
            "Темп, снятый с исполнения, плохо ложится на этот такт: его ритм мог записаться неверно."),
        CheckReason.DenseCluster => words.T(
            "Many notes sound close together here, where a transcription often hears extra ones.",
            "Здесь много нот подряд: транскрипция в таких местах часто слышит лишние."),
        CheckReason.HandsGuessed => words.T(
            "The hands in this bar were guessed from the pitches alone.",
            "Руки в этом такте угаданы только по высоте нот."),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown check reason."),
    };

    private static CheckReason Of(ScanFlag flag) => flag switch
    {
        ScanFlag.BarTooLong => CheckReason.BarTooLong,
        ScanFlag.BarTooShort => CheckReason.BarTooShort,
        ScanFlag.StavesDiffer => CheckReason.StavesDiffer,
        ScanFlag.OutOfPianoRange => CheckReason.OutOfPianoRange,
        ScanFlag.GraceNotes => CheckReason.GraceNotes,
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "Unknown scan flag."),
    };
}