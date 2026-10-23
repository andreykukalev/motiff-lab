using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>The V1 rhythm reshapes this slice offers. Syncopate and dotted follow in #35.</summary>
public enum RhythmReshapeKind
{
    /// <summary>Every onset twice as far from the motif's start, every note twice as long.</summary>
    HalfTime,

    /// <summary>Every onset half as far from the motif's start, every note half as long.</summary>
    DoubleTime,

    /// <summary>Every note starts 1–3 eighths later, with its length unchanged.</summary>
    Displace,
}

/// <summary>The parameters of V1 Rhythm reshape (S1-E3).</summary>
/// <param name="Eighths">How far <see cref="RhythmReshapeKind.Displace"/> moves the notes, 1–3 eighths; ignored otherwise.</param>
public sealed record RhythmReshapeParameters(RhythmReshapeKind Kind, int Eighths = 1)
{
    public const int MinEighths = 1;

    public const int MaxEighths = 3;
}

/// <summary>
/// V1 Rhythm reshape: keeps the pitches and their order, changes onsets and durations. Pure and
/// deterministic: the same notes, selection, Keep choices and parameters give the same variation.
/// </summary>
public static class RhythmReshape
{
    public const string OperationId = "V1";

    private const long EighthTicks = CanonicalTime.TicksPerQuarter / 2;

    /// <summary>Why the Keep choices rule V1 out (the Keep table's V1 row), or null when it can run.</summary>
    public static string? DisabledReason(IReadOnlySet<KeepProperty> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        return keep.Contains(KeepProperty.Rhythm)
            ? "Rhythm reshape changes when notes start and how long they last, so it is off while Keep rhythm is on."
            : null;
    }

    /// <summary>Why these parameters cannot apply to the motif without leaving the tick grid, or null when they can.</summary>
    public static string? UnavailableReason(IReadOnlyList<Note> motif, TickSpan span, RhythmReshapeParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(motif);
        ArgumentNullException.ThrowIfNull(parameters);
        if (motif.Count == 0)
        {
            return "The selection has no notes to reshape.";
        }

        if (parameters.Kind == RhythmReshapeKind.DoubleTime
            && motif.Any(note => (note.Span.Onset.Value - span.Onset.Value) % 2 != 0 || note.Span.Duration % 2 != 0))
        {
            return "Double-time would put a note between ticks, so it is not available for this selection.";
        }

        return null;
    }

    /// <summary>Applies V1 to the selected notes of <paramref name="notes"/>, then checks the kept properties.</summary>
    /// <exception cref="InvalidOperationException">The Keep choices rule V1 out, or the parameters are unavailable.</exception>
    public static Variation Apply(IEnumerable<Note> notes, MotifSelection selection, IReadOnlySet<KeepProperty> keep, RhythmReshapeParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(keep);
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Kind == RhythmReshapeKind.Displace
            && parameters.Eighths is < RhythmReshapeParameters.MinEighths or > RhythmReshapeParameters.MaxEighths)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Eighths, "Displace moves notes by 1–3 eighths.");
        }

        IReadOnlyList<Note> motif = VariationFacts.MotifNotes(notes, selection);
        string? reason = DisabledReason(keep) ?? UnavailableReason(motif, selection.Span, parameters);
        if (reason is not null)
        {
            throw new InvalidOperationException(reason);
        }

        long start = selection.Span.Onset.Value;
        NoteChange[] changes = motif.Select(note => new NoteChange(note, note with
        {
            Span = Reshape(note.Span, start, parameters),
            Attribution = Attribution.UserEdited,
        })).ToArray();

        return VariationFacts.Build(OperationId, selection.Span, changes, keep);
    }

    private static TickSpan Reshape(TickSpan span, long start, RhythmReshapeParameters parameters)
    {
        long offset = span.Onset.Value - start;
        return parameters.Kind switch
        {
            RhythmReshapeKind.HalfTime => new TickSpan(new Tick(start + (offset * 2)), span.Duration * 2),
            RhythmReshapeKind.DoubleTime => new TickSpan(new Tick(start + (offset / 2)), span.Duration / 2),
            RhythmReshapeKind.Displace => new TickSpan(span.Onset.Offset(parameters.Eighths * EighthTicks), span.Duration),
            _ => throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Kind, "Unknown rhythm reshape."),
        };
    }
}