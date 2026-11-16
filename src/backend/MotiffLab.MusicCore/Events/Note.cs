using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Events;

/// <summary>
/// A note in the shared musical event model: the one shape that display, playback, selection,
/// analysis, variations and export read, whatever the input format.
/// </summary>
/// <remarks>
/// <para>
/// Timing is a canonical <see cref="TickSpan"/> (decision record 0002). Bar and beat come from the
/// <see cref="MeterMap"/>, and seconds from the tempo map; neither is stored on the note.
/// </para>
/// <para>
/// <see cref="Attribution"/> covers the note itself (that it exists, its pitch, timing and velocity).
/// Fields that can be inferred separately, such as <see cref="Hand"/>, carry their own attribution,
/// so an inferred hand never makes an imported note look uncertain, or the other way round
/// (decision record 0004).
/// </para>
/// </remarks>
public sealed record Note
{
    public const int MinVelocity = 1;

    public const int MaxVelocity = 127;

    private readonly string _id = null!;
    private readonly int _velocity;
    private readonly int _track;
    private readonly int? _voice;
    private readonly Attribution _attribution = null!;

    /// <summary>Stable within a project, for example "n_0057". Motifs, diffs and edits refer to notes by it.</summary>
    public required string Id
    {
        get => _id;
        init => _id = !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("A note id is required.", nameof(Id));
    }

    public required Pitch Pitch { get; init; }

    /// <summary>Notated (or played) onset and length in canonical ticks. Sustain-pedal sounding length is not included.</summary>
    public required TickSpan Span { get; init; }

    /// <summary>MIDI velocity, 1–127.</summary>
    public required int Velocity
    {
        get => _velocity;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinVelocity, nameof(Velocity));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxVelocity, nameof(Velocity));
            _velocity = value;
        }
    }

    /// <summary>
    /// Zero-based index of the track the note belongs to: a MIDI track or a MusicXML part, as the
    /// importer lists them. The workspace shows one lane per track.
    /// </summary>
    public required int Track
    {
        get => _track;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(Track));
            _track = value;
        }
    }

    /// <summary>
    /// The notated voice within the track, numbered from 1, when the source has voices (MusicXML).
    /// Null for MIDI. This is not the melody, inner or bass role, which analysis infers separately.
    /// </summary>
    public int? Voice
    {
        get => _voice;
        init
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(Voice), value, "Voices are numbered from 1.");
            }

            _voice = value;
        }
    }

    /// <summary>The hand, when known: imported from a grand staff, inferred, or set by the player.</summary>
    public Attributed<Hand>? Hand { get; init; }

    public required Attribution Attribution
    {
        get => _attribution;
        init => _attribution = value ?? throw new ArgumentNullException(nameof(Attribution));
    }

    /// <summary>Where the note is in the source file. Null for notes the player added.</summary>
    public SourceLocation? Source { get; init; }
}