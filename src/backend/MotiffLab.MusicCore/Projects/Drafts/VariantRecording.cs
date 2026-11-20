using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// Where a variant of notes heard in a recording sounds in that recording (decision record 0034, section 4: a приём heard
/// «как на записи», from the recording's own sound): the original that is the recording, the piece whose original it is,
/// and, on the clock of the Notes the fragment was copied from (<see cref="DraftFragment.Heard"/>), the stretch of the
/// recording a span of the variant was played in.
/// </summary>
public sealed class VariantRecording
{
    private readonly Project _notes;
    private readonly ImportedPiece _piece;
    private readonly long _from;

    private VariantRecording(string project, string original, Project notes, ImportedPiece piece, long from)
    {
        Project = project;
        Original = original;
        _notes = notes;
        _piece = piece;
        _from = from;
    }

    /// <summary>The piece whose original the recording is, by id.</summary>
    public string Project { get; }

    /// <summary>The recording's original, by id, in that piece's originals.</summary>
    public string Original { get; }

    /// <summary>
    /// The recording of <paramref name="variant"/>, whose Notes are in <paramref name="holder"/> (the piece itself, or the
    /// other piece the variant names): null for notes not heard, or once the Notes are no longer there or no longer open.
    /// </summary>
    public static VariantRecording? Of(Project holder, DraftVariant variant)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(variant);
        if (variant.From.Milliseconds is null || variant.Provenance.From.FirstOrDefault() is not { } recording)
        {
            return null;
        }

        try
        {
            Project notes = holder.ViewOf(variant.From.Notes);
            return new VariantRecording(recording.Project ?? holder.Id, recording.Original, notes, OpenedProject.Open(notes, EditsLayer.Lenient).Piece, variant.From.Span.Onset.Value);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or ProjectFileException)
        {
            return null;
        }
    }

    /// <summary>The stretch of the recording <paramref name="span"/> of the variant's ticks was played in, in milliseconds; null when it has none.</summary>
    public (long From, long To)? Stretch(TickSpan span) =>
        DraftFragment.Heard(_notes, _piece, new TickSpan(span.Onset.Offset(_from), span.Duration));
}