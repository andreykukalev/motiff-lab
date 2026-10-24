using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Motifs;

/// <summary>What a note does in a motif's texture. A note can have more than one role.</summary>
public enum VoiceRole
{
    /// <summary>A note of the melody: the top line.</summary>
    Melody,

    /// <summary>Neither melody nor bass: a chord tone or an inner voice.</summary>
    Inner,

    /// <summary>A note of the bass: the bottom line.</summary>
    Bass,
}

/// <summary>Everything that sounds at one onset of a motif: the notes struck there and the notes still held from before.</summary>
/// <param name="Struck">Notes that start at <paramref name="At"/>, from low to high.</param>
/// <param name="Held">Notes that started earlier and still sound at <paramref name="At"/>, from low to high.</param>
public sealed record Sonority(Tick At, IReadOnlyList<Note> Struck, IReadOnlyList<Note> Held)
{
    /// <summary>Every note sounding at <see cref="At"/>, from low to high.</summary>
    public IReadOnlyList<Note> Sounding => [.. Struck.Concat(Held).OrderBy(static note => note.Pitch.MidiNumber).ThenBy(static note => note.Id, StringComparer.Ordinal)];

    /// <summary>The distinct pitch classes sounding, 0 (C) to 11 (B), in rising order.</summary>
    public IReadOnlyList<int> PitchClasses => [.. Struck.Concat(Held).Select(static note => note.Pitch.PitchClass).Distinct().Order()];
}

/// <summary>
/// One reading of a motif's texture that analysis, Keep checks and operations share: its melody,
/// its bass, and the sonority at each onset (Stage 1 spec, motif definition).
/// </summary>
/// <remarks>
/// <para>
/// When no two notes sound at once the motif is a single line: every note is the melody, there is
/// no separate bass, and the reading is imported fact. Otherwise both lines are inferred, with
/// <see cref="Confidence"/>:
/// </para>
/// <list type="bullet">
/// <item>The melody is the highest note at each onset, among right-hand notes when the motif has
/// any. An onset whose notes all lie below a melody note still sounding is accompaniment under a
/// held melody note, not a new melody note.</item>
/// <item>The bass mirrors it: the lowest note at each onset, among left-hand notes when the motif
/// has any, skipping onsets whose notes all lie above a bass note still sounding.</item>
/// </list>
/// <para>
/// A note struck alone with nothing held is both melody and bass, and a melody note struck with
/// lower notes is also the top of a chord. The reading keeps such overlaps visible instead of
/// forcing each note into one voice. Pure: the same notes give the same texture, in any order.
/// </para>
/// </remarks>
/// <param name="Notes">The motif's notes, ordered by onset, then pitch, then id.</param>
/// <param name="Melody">The melody notes in onset order.</param>
/// <param name="Bass">The bass notes in onset order; empty for a single line.</param>
/// <param name="Sonorities">One per distinct onset, in order.</param>
public sealed record MotifTexture(
    IReadOnlyList<Note> Notes,
    IReadOnlyList<Note> Melody,
    IReadOnlyList<Note> Bass,
    IReadOnlyList<Sonority> Sonorities,
    Attribution Attribution)
{
    /// <summary>The top line is usually the melody and the bottom line the bass, but not always (an inner melody, a melody in the bass).</summary>
    public const double Confidence = 0.8;

    /// <summary>True when no two notes sound at once, so the melody is every note and is read, not inferred.</summary>
    public bool IsSingleLine => Attribution.Provenance != Provenance.Inferred;

    /// <summary>Reads the texture of a motif's notes.</summary>
    public static MotifTexture Read(IEnumerable<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        Note[] sorted = [.. notes
            .DistinctBy(static note => note.Id, StringComparer.Ordinal)
            .OrderBy(static note => note.Span.Onset)
            .ThenBy(static note => note.Pitch.MidiNumber)
            .ThenBy(static note => note.Id, StringComparer.Ordinal)];
        Sonority[] sonorities = SonoritiesOf(sorted);

        if (!HasTwoAtOnce(sorted))
        {
            return new MotifTexture(sorted, sorted, [], sonorities, Attribution.Imported);
        }

        Note[] melody = Line(Prefer(sorted, Hand.Right), top: true);
        Note[] bass = Line(Prefer(sorted, Hand.Left), top: false);
        return new MotifTexture(sorted, melody, bass, sonorities, Attribution.Inferred(Confidence));
    }

    /// <summary>The roles of a note of this motif; empty when the note is not in it.</summary>
    public IReadOnlySet<VoiceRole> RolesOf(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var roles = new HashSet<VoiceRole>();
        if (Melody.Any(melody => melody.Id == note.Id))
        {
            roles.Add(VoiceRole.Melody);
        }

        if (Bass.Any(bass => bass.Id == note.Id))
        {
            roles.Add(VoiceRole.Bass);
        }

        if (roles.Count == 0 && Notes.Any(other => other.Id == note.Id))
        {
            roles.Add(VoiceRole.Inner);
        }

        return roles;
    }

    /// <summary>The other notes sounding when <paramref name="note"/> starts, from low to high: struck with it or still held.</summary>
    public IReadOnlyList<Note> SoundingWith(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        Sonority? sonority = Sonorities.FirstOrDefault(sonority => sonority.At == note.Span.Onset);
        return sonority is null ? [] : [.. sonority.Sounding.Where(other => other.Id != note.Id)];
    }

    private static Note[] Prefer(Note[] sorted, Hand hand) =>
        sorted.Any(note => note.Hand?.Value == hand) ? [.. sorted.Where(note => note.Hand?.Value == hand)] : sorted;

    private static Note[] Line(Note[] candidates, bool top)
    {
        var line = new List<Note>();
        foreach (IGrouping<Tick, Note> onset in candidates.GroupBy(static note => note.Span.Onset))
        {
            Note pick = top ? onset.MaxBy(static note => note.Pitch.MidiNumber)! : onset.MinBy(static note => note.Pitch.MidiNumber)!;
            if (line.Count > 0 && line[^1].Span.End > onset.Key && (top ? pick.Pitch < line[^1].Pitch : pick.Pitch > line[^1].Pitch))
            {
                continue;
            }

            line.Add(pick);
        }

        return [.. line];
    }

    private static Sonority[] SonoritiesOf(Note[] sorted) => [.. sorted
        .GroupBy(static note => note.Span.Onset)
        .Select(onset => new Sonority(
            onset.Key,
            [.. onset],
            [.. sorted.Where(note => note.Span.Onset < onset.Key && note.Span.End > onset.Key)
                .OrderBy(static note => note.Pitch.MidiNumber)
                .ThenBy(static note => note.Id, StringComparer.Ordinal)]))];

    private static bool HasTwoAtOnce(Note[] sorted)
    {
        Tick soundingUntil = Tick.Zero;
        foreach (Note note in sorted)
        {
            if (note.Span.Onset < soundingUntil)
            {
                return true;
            }

            soundingUntil = note.Span.End > soundingUntil ? note.Span.End : soundingUntil;
        }

        return false;
    }
}