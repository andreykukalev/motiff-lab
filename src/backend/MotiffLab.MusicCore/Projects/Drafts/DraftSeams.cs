using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// One side of a seam as its place's result has it there: the key its source states (null when it states none, never one
/// worked out), the meter of the bar at the seam and the tempo in force there. The base gives the facts; whether they were
/// heard in a recording, and so inferred, is its provenance (<see cref="DraftLabels.IsHeard"/>).
/// </summary>
/// <param name="Index">The place's position in the draft, from 0.</param>
/// <param name="Tempo">Where the tempo comes from: a tempo step of the draft (<see cref="TempoSource.Player"/>), the piece's
/// default where nothing states one, worked out from the playing, or as its source states it (<see cref="TempoSource.Notated"/>).</param>
public sealed record SeamSide(string Place, int Index, DraftVariant Base, KeySignature? Key, TimeSignature Meter, int PerMinute, TempoSource Tempo)
{
    /// <summary>Whether the place's notes were heard in a recording: its meter and tempo were worked out from the playing.</summary>
    public bool Heard => DraftLabels.IsHeard(Base);
}

/// <summary>
/// The boundary of two places of a draft (decision record 0034, section 5): what differs in key, meter and tempo, and what
/// music-core offers there: the place after it transposed to the key before it, or played at the tempo before it. Nothing
/// is changed by itself; «Оставить» keeps the seam as it is and is remembered (<see cref="Left"/>).
/// </summary>
/// <param name="At">The tick of the draft where the place after it starts.</param>
/// <param name="Listen">«▶ Послушать стык»: the draft's last bar before the seam and its first bar after it.</param>
/// <param name="Left">Whether the player kept the seam as it is: a <see cref="DraftLeave"/> with <see cref="DraftLeave.Seam"/> of the place after it.</param>
public sealed record DraftSeam(SeamSide Before, SeamSide After, Tick At, TickSpan Listen, bool Left)
{
    /// <summary>Whether the keys differ; false when a side states none, since then nothing tells.</summary>
    public bool KeyChanges => Before.Key is { } before && After.Key is { } after
        && (before.Fifths != after.Fifths || (before.Mode is { } one && after.Mode is { } other && one != other));

    public bool MeterChanges => Before.Meter != After.Meter;

    public bool TempoChanges => Before.PerMinute != After.PerMinute;

    /// <summary>
    /// «② в соль мажор»: the semitones that move the place after the seam to the key signature before it, the nearer way
    /// (−6 to +5, <see cref="Transposition.Semitones"/>), its mode kept; null when a side states no key or the signatures are the same.
    /// </summary>
    public int? Semitones => Before.Key is { } before && After.Key is { } after
        && Transposition.Semitones(new MusicalKey(after.Fifths, KeyMode.Major), new MusicalKey(before.Fifths, KeyMode.Major)) is var semitones and not 0
            ? semitones
            : null;

    /// <summary>The key the place after the seam would be in, moved by <see cref="Semitones"/>; null when none is offered.</summary>
    public KeySignature? Transposed => Semitones is { } semitones ? After.Key! with { Fifths = Transposition.Moved(After.Key!.Fifths, semitones) } : null;

    /// <summary>«Темп как в ①»: the tempo before the seam for the place after it, when they differ and it is one a place can be set to; else null.</summary>
    public int? Tempo => TempoChanges && PlayerTempo.IsValid(Before.PerMinute) ? Before.PerMinute : null;
}

/// <summary>
/// The seams of a draft and the player's decisions at one (decision record 0034, section 5): each place after the first
/// has the seam before it. Transposing a place and setting its tempo are journal steps (<see cref="DraftTranspose"/>,
/// <see cref="DraftTempo"/>), and so is «Оставить». Pure: each gives a new draft.
/// </summary>
public static class DraftSeams
{
    /// <summary>The draft's seams in order, worked out from its places' results as its steps left them.</summary>
    public static IReadOnlyList<DraftSeam> Of(Draft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        DraftMusic score = DraftMusic.Of(draft);
        List<DraftSeam> seams = [];
        for (int index = 1; index < draft.Places.Count; index++)
        {
            Tick at = score.Places[index].Span.Onset;
            TickSpan listen = TickSpan.FromBounds(score.Music.BarAt(new Tick(at.Value - 1)).Bar.Span.Onset, score.Music.BarAt(at).Bar.Span.End);
            DraftPlace after = draft.Places[index];
            bool left = draft.StepsOf(after.Id).OfType<DraftLeave>().Any(static leave => leave.Seam);
            seams.Add(new DraftSeam(Side(draft, index - 1, atEnd: true), Side(draft, index, atEnd: false), at, listen, left));
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

    /// <summary>The place at <paramref name="index"/> at its end (the seam after it) or at its start (the seam before it).</summary>
    private static SeamSide Side(Draft draft, int index, bool atEnd)
    {
        DraftPlace place = draft.Places[index];
        SourceMusic music = draft.ResultOf(place.Id).Music;
        Tick tick = atEnd ? new Tick(Math.Max(0, music.End.Value - 1)) : Tick.Zero;
        PlaybackTempo[] before = [.. music.Tempos.Where(each => each.At <= tick)];
        PlaybackTempo tempo = before.Length > 0 ? before.MaxBy(static each => each.At) : new PlaybackTempo(Tick.Zero, PlaybackTempo.PieceDefaultMicrosecondsPerQuarter, TempoSource.Default);
        bool stepped = draft.StepsOf(place.Id).OfType<DraftTempo>().Any();
        TempoSource source = tempo.Source == TempoSource.Default ? TempoSource.Default
            : tempo.Source == TempoSource.Player && stepped ? TempoSource.Player
            : tempo.Source == TempoSource.Inferred || DraftLabels.IsHeard(place.BaseVariant) ? TempoSource.Inferred
            : TempoSource.Notated;
        int perMinute = TempoSources.PerMinute(tempo.MicrosecondsPerQuarter);
        return new SeamSide(place.Id, index, place.BaseVariant, KeySignature.InForce(music.Keys, tick), music.BarAt(tick).Bar.Signature, perMinute, source);
    }
}