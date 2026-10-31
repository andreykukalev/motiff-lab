namespace MotiffLab.MusicCore.Import.Transcription;

/// <summary>
/// The stretch of a recording the player chose to have heard (decision record 0014), for example one piece
/// out of a half-hour video: from one millisecond of the recording to another, at most
/// <see cref="MostMilliseconds"/> long. Only this stretch's sound is sent, in the parts
/// <see cref="RecordingParts"/> plans for it, so the player sees before sending how many requests it takes.
/// Moving one end keeps the other where it is when it can. Pure and deterministic.
/// </summary>
public sealed record RecordingRange
{
    /// <summary>The longest stretch heard at once: seven minutes, in parts of up to two.</summary>
    public const long MostMilliseconds = 420_000;

    /// <summary>The shortest stretch: a second, so there is always some sound to hear.</summary>
    public const long LeastMilliseconds = 1_000;

    private RecordingRange(long recordingMilliseconds, long fromMilliseconds, long toMilliseconds) =>
        (RecordingMilliseconds, FromMilliseconds, ToMilliseconds) = (recordingMilliseconds, fromMilliseconds, toMilliseconds);

    /// <summary>The length of the whole recording.</summary>
    public long RecordingMilliseconds { get; }

    /// <summary>Where the stretch starts in the recording.</summary>
    public long FromMilliseconds { get; }

    /// <summary>Where the stretch ends in the recording.</summary>
    public long ToMilliseconds { get; }

    /// <summary>The length of the stretch.</summary>
    public long Milliseconds => ToMilliseconds - FromMilliseconds;

    /// <summary>True when the stretch is the whole recording.</summary>
    public bool IsWhole => FromMilliseconds == 0 && ToMilliseconds == RecordingMilliseconds;

    /// <summary>The parts the stretch is heard in, one request each, on the stretch's own clock.</summary>
    public IReadOnlyList<RecordingPart> Parts => RecordingParts.Plan(Milliseconds);

    /// <summary>How much sound the requests carry together, the overlaps at the seams counted twice.</summary>
    public long SentMilliseconds => Parts.Sum(part => part.SendToMilliseconds - part.SendFromMilliseconds);

    /// <summary>
    /// The first stretch of a recording of <paramref name="recordingMilliseconds"/>: all of it, or its
    /// first seven minutes when it is longer.
    /// </summary>
    public static RecordingRange Start(long recordingMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordingMilliseconds);
        return new(recordingMilliseconds, 0, Math.Min(recordingMilliseconds, MostMilliseconds));
    }

    /// <summary>
    /// The stretch starting at <paramref name="fromMilliseconds"/>. Its end stays unless the stretch would then
    /// be too short, too long or turned round; it then keeps the length it had, or seven minutes at most,
    /// up to the recording's end.
    /// </summary>
    public RecordingRange WithStart(long fromMilliseconds)
    {
        long least = Math.Min(LeastMilliseconds, RecordingMilliseconds);
        long from = Math.Clamp(fromMilliseconds, 0, RecordingMilliseconds - least);
        long to = ToMilliseconds;
        if (to - from < least || to - from > MostMilliseconds)
        {
            to = Math.Min(RecordingMilliseconds, from + Math.Clamp(Milliseconds, least, MostMilliseconds));
        }

        return new(RecordingMilliseconds, from, Math.Max(to, from + least));
    }

    /// <summary>
    /// The stretch ending at <paramref name="toMilliseconds"/>. Its start stays unless the stretch would then be
    /// too short, too long or turned round; it then keeps the length it had, or seven minutes at most, from
    /// the recording's start.
    /// </summary>
    public RecordingRange WithEnd(long toMilliseconds)
    {
        long least = Math.Min(LeastMilliseconds, RecordingMilliseconds);
        long to = Math.Clamp(toMilliseconds, least, RecordingMilliseconds);
        long from = FromMilliseconds;
        if (to - from < least || to - from > MostMilliseconds)
        {
            from = Math.Max(0, to - Math.Clamp(Milliseconds, least, MostMilliseconds));
        }

        return new(RecordingMilliseconds, Math.Min(from, to - least), to);
    }
}