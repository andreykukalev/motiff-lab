using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>The Check flags of a scan or a transcription (0018, section 9), and a transcription written in other bars.</summary>
public sealed partial record Project
{
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
    /// Whether a transcription can still be written in other bars or at another tempo (0018, section 9: the head
    /// of the Check drawer). Only before the player has put anything on the score: corrections, phrase changes,
    /// marks, bookmarks, ideas, observations and checked bars are kept by their place in the bars, which would move.
    /// </summary>
    public bool CanChangeBars => Transcription is not null && Edits.IsEmpty && !PlacedInBars && !(Check ?? []).Any(flag => flag.Resolved);

    /// <summary>
    /// Whether the player put on the score what is kept by its place in the bars, but corrections and checked bars: taken bars,
    /// phrase changes, marks, bookmarks, motifs, variations, observations, or what this build cannot read.
    /// </summary>
    private bool PlacedInBars => Bookmarks.Count > 0 || TakenBars.Any() || !Phrases.IsEmpty || !ShortPhrases.IsEmpty || !Sentences.IsEmpty
        || !Marks.IsEmpty || Motifs.Count > 0 || Variations.Count > 0 || Observations.Count > 0
        || Unknown.Count > 0 || Marks.Unknown.Count > 0;

    /// <summary>
    /// The transcription written in other bars, or as the engine wrote it when <paramref name="bars"/> is null. Its
    /// Check flags are worked out again for the new bars, and its resume points go back to the start of the piece,
    /// since every position moves.
    /// </summary>
    /// <exception cref="InvalidOperationException">The project is not a transcription, or <see cref="CanChangeBars"/> is false.</exception>
    public Project ChangeBars(BarGrid? bars)
    {
        if (Transcription is not { } heard)
        {
            throw new InvalidOperationException("Only a transcription's bars can be changed.");
        }

        if (!CanChangeBars)
        {
            throw new InvalidOperationException("The bars cannot change once something has been placed in them.");
        }

        Import.Transcription.TranscriptionOrigin origin = new(heard.Engine, bars) { Seams = heard.Seams, From = heard.From, At = heard.At };
        Project changed = this with
        {
            Transcription = origin,
            Check = [],
            ResumePoints = [.. ResumePoints.Select(point => point with { At = Tick.Zero, Passage = null, Focused = false, Loop = false })],
        };
        ImportedPiece piece = OpenedProject.Open(changed).Piece;
        return changed with { Check = CheckFlag.FromTranscription(origin, Source, piece) };
    }
}