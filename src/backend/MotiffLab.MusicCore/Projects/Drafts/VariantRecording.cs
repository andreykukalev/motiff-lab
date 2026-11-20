using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// Where a variant of notes heard in a recording sounds in that recording (decision record 0034, section 4: a приём heard
/// «как на записи», from the recording's own sound): the original the Notes the fragment was copied from were heard in,
/// the piece whose original it is, and, on those Notes' clock (<see cref="DraftFragment.Heard"/>), the stretch of the
/// recording a span of the variant was played in. Whether the sound is on the device is not known here.
/// </summary>
public sealed class VariantRecording
{
    private readonly Project _notes;
    private readonly HeardClock _clock;
    private readonly long _from;

    private VariantRecording(string project, string original, Project notes, HeardClock clock, long from)
    {
        Project = project;
        Original = original;
        _notes = notes;
        _clock = clock;
        _from = from;
    }

    /// <summary>The piece whose original the recording is, by id.</summary>
    public string Project { get; }

    /// <summary>The recording's original, by id, in that piece's originals: the one the Notes' transcription names.</summary>
    public string Original { get; }

    /// <summary>
    /// The recording of <paramref name="variant"/>, whose Notes are in <paramref name="holder"/> (the piece itself, or the
    /// other piece the variant names), its clock built once: null for notes not heard, heard in an original not kept, or
    /// once the Notes are no longer there.
    /// </summary>
    public static VariantRecording? Of(Project holder, DraftVariant variant)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(variant);
        if (variant.From.Milliseconds is null)
        {
            return null;
        }

        try
        {
            Project notes = holder.ViewOf(variant.From.Notes);
            return notes.Transcription?.From is { } original && HeardClock.Of(notes, OpenedProject.Open(notes, EditsLayer.Lenient).Piece) is { } clock
                ? new VariantRecording(holder.Id, original, notes, clock, variant.From.Span.Onset.Value)
                : null;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or ProjectFileException)
        {
            return null;
        }
    }

    /// <summary>The stretch of the recording <paramref name="span"/> of the variant's ticks was played in, in milliseconds; null when it has none.</summary>
    public (long From, long To)? Stretch(TickSpan span) =>
        DraftFragment.Heard(_notes, _clock, new TickSpan(span.Onset.Offset(_from), span.Duration));
}