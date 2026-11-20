using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// One side of a seam as the draft's score has it there (<see cref="DraftMusic"/>): the key its place's source states (null
/// when it states none, never one worked out), the meter of the bar at the seam and the tempo in force there with its source
/// as saved, the piece's default where the place states none. Where the base was copied from is its provenance.
/// </summary>
/// <param name="Index">The place's position in the draft, from 0.</param>
public sealed record SeamSide(string Place, int Index, DraftVariant Base, KeySignature? Key, TimeSignature Meter, PlaybackTempo Tempo)
{
    /// <summary>The tempo in quarter notes a minute, rounded.</summary>
    public int PerMinute => TempoSources.PerMinute(Tempo.MicrosecondsPerQuarter);

    /// <summary>Every note of the place's result, both hands, in the draft's ticks: its register and the bridge read them.</summary>
    public IReadOnlyList<Note> Notes { get; init; } = [];

    /// <summary>Whether the place was made for a seam, its variants the bridge candidates (<see cref="DraftPlace.Bridge"/>).</summary>
    public bool Bridge { get; init; }
}

/// <summary>
/// The boundary of two places of a draft (decision record 0034, section 5): what differs in key, meter, tempo and register,
/// and what music-core offers there: the place after it transposed to the key before it, moved by an octave towards it, or
/// played at the tempo before it, and a bridge (<see cref="DraftBridges"/>). Nothing is changed by itself; «Оставить» keeps
/// the seam as it is and is remembered (<see cref="Left"/>). A bridge place is offered nothing at the seam before it: it was
/// made to lead from the place before it into the key after it.
/// </summary>
/// <param name="At">The tick of the draft where the place after it starts.</param>
/// <param name="Listen">«▶ Послушать стык»: the draft's last bar before the seam and its first bar after it.</param>
/// <param name="Left">Whether the player kept the seam as it is: a <see cref="DraftLeave"/> with <see cref="DraftLeave.Seam"/> of the place after it.</param>
/// <param name="LastBar">The draft's number of the last bar before the seam.</param>
/// <param name="FirstBar">The draft's number of the first bar after it.</param>
public sealed record DraftSeam(SeamSide Before, SeamSide After, Tick At, TickSpan Listen, bool Left, int LastBar, int FirstBar)
{
    /// <summary>Whether the keys differ; false when a side states none, since then nothing tells.</summary>
    public bool KeyChanges => Before.Key is { } before && After.Key is { } after
        && (before.Fifths != after.Fifths || (before.Mode is { } one && after.Mode is { } other && one != other));

    public bool MeterChanges => Before.Meter != After.Meter;

    public bool TempoChanges => Before.PerMinute != After.PerMinute;

    /// <summary>
    /// «② в соль мажор»: the semitones that move the place after the seam to the key signature before it, the nearer way
    /// (−6 to +5, <see cref="Transposition.Semitones"/>), a tritone the way that writes it as before the seam (G major next to
    /// D♭ major down, to five flats; C major next to F♯ major up), its mode kept; null when a side states no key or the
    /// signatures are the same.
    /// </summary>
    public int? Semitones
    {
        get
        {
            if (After.Bridge || Before.Key is not { } before || After.Key is not { } after
                || Transposition.Semitones(new MusicalKey(after.Fifths, KeyMode.Major), new MusicalKey(before.Fifths, KeyMode.Major)) is not (not 0 and var semitones))
            {
                return null;
            }

            // Transposition.Semitones never answers +6 (its range is −6 to +5), so a tritone always comes as −6: up instead
            // where only up writes the place in the key signature before the seam.
            return semitones == -6 && Transposition.Moved(after.Fifths, -6) != before.Fifths ? 6 : semitones;
        }
    }

    /// <summary>The key the place after the seam would be in, moved by <see cref="Semitones"/>; null when none is offered.</summary>
    public KeySignature? Transposed => Semitones is { } semitones ? After.Key! with { Fifths = Transposition.Moved(After.Key!.Fifths, semitones) } : null;

    /// <summary>«Темп как в ①»: the tempo before the seam for the place after it, when they differ and it is one a place can be set to; else null.</summary>
    public int? Tempo => !After.Bridge && TempoChanges && PlayerTempo.IsValid(Before.PerMinute) ? Before.PerMinute : null;

    /// <summary>
    /// The register (the plan's answer 3): 1 when the mean pitch of the place after the seam, the mean MIDI number of all its
    /// notes, both hands, is an octave (12 semitones) or more above that of the place before it, −1 when it is as far below,
    /// else 0 and nothing is said of it; 0 too into a bridge place, which follows its neighbours' register (<see cref="DraftBridges"/>).
    /// Worked out in whole numbers: the sums of the pitches against 12 × both counts.
    /// </summary>
    public int Register
    {
        get
        {
            long before = Before.Notes.Sum(static note => (long)note.Pitch.MidiNumber), after = After.Notes.Sum(static note => (long)note.Pitch.MidiNumber);
            long difference = (after * Before.Notes.Count) - (before * After.Notes.Count), octave = 12L * Before.Notes.Count * After.Notes.Count;
            return octave == 0 || After.Bridge ? 0 : difference >= octave ? 1 : difference <= -octave ? -1 : 0;
        }
    }

    /// <summary>«② на октаву ниже»: the place after the seam moved an octave towards the one before it, its spelling kept (−12 or 12); null when the register does not differ.</summary>
    public int? Octave => Register == 0 ? null : -12 * Register;
}

/// <summary>
/// The seams of a draft and the player's decisions at one (decision record 0034, section 5): each place after the first
/// has the seam before it. Transposing a place and setting its tempo are journal steps (<see cref="DraftTranspose"/>,
/// <see cref="DraftTempo"/>), and so is «Оставить». Pure: each gives a new draft.
/// </summary>
public static class DraftSeams
{
    /// <summary>The draft's seams in order, worked out from its score (<see cref="DraftMusic"/>), each place's result built once.</summary>
    public static IReadOnlyList<DraftSeam> Of(Draft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        DraftMusic score = DraftMusic.Of(draft);
        List<DraftSeam> seams = [];
        for (int index = 1; index < draft.Places.Count; index++)
        {
            Tick at = score.Places[index].Span.Onset;
            TickSpan listen = TickSpan.FromBounds(score.Music.BarAt(new Tick(at.Value - 1)).Bar.Span.Onset, score.Music.BarAt(at).Bar.Span.End);
            bool left = draft.StepsOf(draft.Places[index].Id).OfType<DraftLeave>().Any(static leave => leave.Seam);
            seams.Add(new DraftSeam(Side(draft, score, index - 1, atEnd: true), Side(draft, score, index, atEnd: false), at, listen, left, score.Places[index - 1].LastBar, score.Places[index].FirstBar));
        }

        return seams;
    }

    /// <summary>«Оставить»: the seam before the place <paramref name="placeId"/> kept as it is, remembered as a step of that place.</summary>
    /// <exception cref="KeyNotFoundException">No such place.</exception>
    /// <exception cref="ArgumentException">The first place, which has no seam before it.</exception>
    /// <exception cref="InvalidOperationException">The seam is kept already.</exception>
    public static Draft Leave(Draft draft, string placeId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(draft);
        DraftSeam seam = Before(draft, placeId);
        return seam.Left
            ? throw new InvalidOperationException("The seam is kept as it is already.")
            : draft.With(next => new DraftLeave(next, at, placeId) { Seam = true });
    }

    /// <summary>«② в соль мажор»: the place <paramref name="placeId"/> moved by <paramref name="semitones"/>, its keys with it.</summary>
    /// <exception cref="KeyNotFoundException">No such place.</exception>
    /// <exception cref="ArgumentOutOfRangeException">0, or more than 24 semitones either way.</exception>
    public static Draft Transpose(Draft draft, string placeId, int semitones, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _ = Place(draft, placeId);
        return draft.With(next => new DraftTranspose(next, at, placeId, semitones));
    }

    /// <summary>«Темп как в ①»: the place <paramref name="placeId"/> played at <paramref name="perMinute"/> quarter notes a minute from its start.</summary>
    /// <exception cref="KeyNotFoundException">No such place.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A tempo outside 20 to 300.</exception>
    public static Draft Tempo(Draft draft, string placeId, int perMinute, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _ = Place(draft, placeId);
        return draft.With(next => new DraftTempo(next, at, placeId, perMinute));
    }

    /// <summary>The seam before the place <paramref name="placeId"/>.</summary>
    private static DraftSeam Before(Draft draft, string placeId)
    {
        int index = draft.Places.ToList().IndexOf(Place(draft, placeId));
        return index > 0 ? Of(draft)[index - 1] : throw new ArgumentException("The first place has no seam before it.", nameof(placeId));
    }

    private static DraftPlace Place(Draft draft, string placeId) => draft.FindPlace(placeId) ?? throw new KeyNotFoundException($"The draft has no place {placeId}.");

    /// <summary>
    /// The place at <paramref name="index"/> at its end (the seam after it) or at its start (the seam before it), as the score
    /// has it. At its end its key is the one in force there only when the place's source states it: the score cancels a key
    /// that would run on from an earlier place at the start of a place that states none.
    /// </summary>
    private static SeamSide Side(Draft draft, DraftMusic score, int index, bool atEnd)
    {
        DraftPlace place = draft.Places[index];
        DraftPlaceSpan span = score.Places[index];
        Tick tick = atEnd ? new Tick(span.Span.End.Value - 1) : span.Span.Onset;
        KeySignature? key = !atEnd ? span.Key
            : KeySignature.InForce(score.Music.Keys, tick) is { } inForce && (inForce.At > span.Span.Onset || (inForce.At == span.Span.Onset && span.Key is not null)) ? inForce
            : null;
        PlaybackTempo tempo = score.Music.Tempos.Last(each => each.At <= tick);
        return new SeamSide(place.Id, index, place.BaseVariant, key, score.Music.BarAt(tick).Bar.Signature, tempo)
        {
            Notes = score.Music.NotesIn(span.Span.Onset, span.Span.End),
            Bridge = place.Bridge,
        };
    }
}