using MotiffLab.MusicCore.Import;

namespace MotiffLab.MusicCore.Playback;

/// <summary>
/// A second imported file played as the variation of the original, on the original's timeline,
/// so A/B switching keeps the musical position (S1-W5).
/// </summary>
/// <remarks>
/// The second file's notes keep their canonical ticks. Its own tempo map and bars are not used:
/// one shared timeline is what keeps the two in step. <see cref="Differences"/> says in plain words
/// where the files disagree, so the player knows what they hear. Aligning is pure and deterministic.
/// </remarks>
/// <param name="Timeline">The original, extended to the second file's end when that is later.</param>
/// <param name="Notes">The second file's notes on <paramref name="Timeline"/>.</param>
/// <param name="Differences">Where the second file's timing differs from the original's; empty when it matches.</param>
public sealed record SecondFileSource(PlaybackScore Timeline, PlaybackScore Notes, IReadOnlyList<string> Differences)
{
    public static SecondFileSource Align(PlaybackScore original, ImportedPiece second)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(second);

        PlaybackScore own = PlaybackScore.FromPiece(second);
        PlaybackScore timeline = original.ExtendedTo(own.End);
        var differences = new List<string>();

        if (!own.Tempos.SequenceEqual(original.Tempos))
        {
            differences.Add("It sets a different tempo; it plays at the original's tempo so the two stay in step.");
        }

        if (second.Meter.PickupDuration != original.Meter.PickupDuration || !second.Meter.Changes.SequenceEqual(original.Meter.Changes))
        {
            differences.Add("Its time signature or pickup differs; bar numbers and the metronome follow the original.");
        }

        if (timeline.End > original.End)
        {
            differences.Add("It runs past the end of the original, so playback now runs to its end.");
        }

        return new SecondFileSource(timeline, timeline.WithNotes(second.Notes), differences);
    }
}