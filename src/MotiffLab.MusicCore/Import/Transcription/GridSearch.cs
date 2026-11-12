using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Transcription;

/// <summary>
/// How a recording's beat grid is searched for when it follows a live performance (version 0.5, «Темп живого
/// исполнения»): afresh in each section, a section starting after every pause of at least
/// <see cref="SectionSilence"/>, at each of <see cref="Starts"/>, and where the beat sticks at the edge of
/// the tempo it may bend to for <see cref="JumpRun"/> beats running. A score linked to the recording may give a
/// <see cref="Hint"/>; it is never imposed. Without a search, <see cref="BarGrid.Find(Midi.MidiImport, TimeSignature?, int?, GridSearch?)"/> works as before.
/// </summary>
public sealed record GridSearch
{
    /// <summary>The shortest pause after which the beat is found again: 2.5 s, as a section boundary in a recording.</summary>
    public const long SectionSilence = 2_500_000;

    /// <summary>How many beats in a row may want to go past the clamp before the beat is found again: 8.</summary>
    public const int JumpRun = 8;

    /// <summary>The search with no starts and no hint.</summary>
    public static GridSearch Live { get; } = new();

    /// <summary>Where sections start besides the pauses, in microseconds of the recording (from its start), such as the pieces of a concert.</summary>
    public IReadOnlyList<long> Starts { get; init; } = [];

    /// <summary>The metre and tempo of a score of the piece, a hint for the recording; null without one.</summary>
    public GridHint? Hint { get; init; }
}

/// <summary>
/// What a score linked to a recording says about its bars: its signature and its tempo, a quarter a minute. The search
/// looks for the tempo within 0.6 to 1.6 of it, and takes the signature when the recording supports it at least three
/// quarters as well as the best.
/// </summary>
public sealed record GridHint(TimeSignature? Signature, int? QuarterNotesPerMinute)
{
    /// <summary>The 16th the hint's tempo gives, in microseconds; null without a tempo.</summary>
    public long? Sixteenth => QuarterNotesPerMinute is int bpm and > 0 ? 15_000_000L / bpm : null;
}

/// <summary>A grid found for a live performance, with how many sections it was found in and whether the hint's signature fit.</summary>
/// <param name="Grid">The grid, or null when the notes follow no beat.</param>
/// <param name="Sections">How many sections the beat was found in afresh: 1 when it was found once.</param>
/// <param name="HintFits">Whether the hint's signature was taken; null without a hinted signature or a grid.</param>
public sealed record GridFound(BarGrid? Grid, int Sections, bool? HintFits)
{
    public static GridFound None { get; } = new(null, 0, null);
}