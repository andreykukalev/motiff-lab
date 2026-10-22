using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Playback;

/// <summary>
/// What the browser playback engine needs to play a piece: its notes, tempo map, metronome beats
/// and bars, on the canonical timeline (S1-W4, decision record 0006).
/// </summary>
/// <remarks>
/// <para>
/// The score is a flat, ordered view of the shared model, built once and handed to the engine as a
/// whole, so timing-critical scheduling happens in the audio thread (decision record 0001). Every
/// position stays a canonical tick; the engine converts ticks to audio frames itself.
/// </para>
/// <para>
/// The timeline (tempo map, beats, bars, end) is shared by every source the engine plays, such as
/// the original motif and a variation of it (S1-W5), so switching between them keeps the musical
/// position. Building a score is pure and deterministic.
/// </para>
/// </remarks>
public sealed class PlaybackScore
{
    private readonly MeterMap _meter;

    private PlaybackScore(
        IReadOnlyList<PlaybackNote> notes,
        IReadOnlyList<PlaybackTempo> tempos,
        IReadOnlyList<PlaybackBeat> beats,
        IReadOnlyList<PlaybackBar> bars,
        MeterMap meter,
        Tick end)
    {
        _meter = meter;
        Notes = notes;
        Tempos = tempos;
        Beats = beats;
        Bars = bars;
        End = end;
    }

    /// <summary>Notes ordered by onset, then pitch, then track.</summary>
    public IReadOnlyList<PlaybackNote> Notes { get; }

    /// <summary>Tempo changes in order; the first is at tick 0.</summary>
    public IReadOnlyList<PlaybackTempo> Tempos { get; }

    /// <summary>Every metronome beat before <see cref="End"/>, in order.</summary>
    public IReadOnlyList<PlaybackBeat> Beats { get; }

    /// <summary>The bars that start before <see cref="End"/>, for the count-in and bar snapping.</summary>
    public IReadOnlyList<PlaybackBar> Bars { get; }

    /// <summary>Where playback stops when it is not looping: the end of the piece or of its last note, whichever is later.</summary>
    public Tick End { get; }

    /// <param name="notes">Notes of the piece or motif, in any order.</param>
    /// <param name="tempos">Tempo changes. Empty means 120 BPM throughout; otherwise the first must be at tick 0 and positions must strictly increase.</param>
    /// <param name="meter">The bars of the piece, for the metronome and count-in.</param>
    /// <param name="end">The end of the piece; extended to the last note's end if that is later.</param>
    public static PlaybackScore Create(IEnumerable<Note> notes, IEnumerable<PlaybackTempo> tempos, MeterMap meter, Tick end)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(tempos);
        ArgumentNullException.ThrowIfNull(meter);

        PlaybackNote[] ordered = notes
            .Select(static note => new PlaybackNote(note.Span, note.Pitch.MidiNumber, note.Velocity, note.Track))
            .OrderBy(static note => note.Span.Onset)
            .ThenBy(static note => note.Pitch)
            .ThenBy(static note => note.Track)
            .ThenBy(static note => note.Span.Duration)
            .ThenBy(static note => note.Velocity)
            .ToArray();

        PlaybackTempo[] tempoMap = tempos.ToArray();
        if (tempoMap.Length == 0)
        {
            tempoMap = [new PlaybackTempo(Tick.Zero, PlaybackTempo.DefaultMicrosecondsPerQuarter)];
        }
        else if (tempoMap[0].At != Tick.Zero)
        {
            throw new ArgumentException("The first tempo change must be at tick 0.", nameof(tempos));
        }

        for (int i = 1; i < tempoMap.Length; i++)
        {
            if (tempoMap[i].At <= tempoMap[i - 1].At)
            {
                throw new ArgumentException("Tempo changes must be in strictly increasing order.", nameof(tempos));
            }
        }

        Tick last = ordered.Length == 0 ? end : ordered.Max(static note => note.Span.End);
        Tick scoreEnd = last > end ? last : end;

        var beats = new List<PlaybackBeat>();
        var bars = new List<PlaybackBar>();
        foreach (Bar bar in meter.BarsBefore(scoreEnd))
        {
            long beatTicks = bar.Signature.BeatTicks;
            bars.Add(new PlaybackBar(bar.Number, bar.Span, beatTicks, bar.Signature.Numerator, bar.IsPickup));

            // A pickup's beats count from the end of a full bar, so its first beat can fall after its start.
            long leadIn = bar.Signature.BarTicks - bar.Span.Duration;
            long firstOffset = bar.IsPickup ? (beatTicks - (leadIn % beatTicks)) % beatTicks : 0;
            for (long offset = firstOffset; offset < bar.Span.Duration; offset += beatTicks)
            {
                Tick at = bar.Span.Onset.Offset(offset);
                if (at >= scoreEnd)
                {
                    break;
                }

                beats.Add(new PlaybackBeat(at, IsDownbeat: !bar.IsPickup && offset == 0));
            }
        }

        return new PlaybackScore(ordered, tempoMap, beats, bars, meter, scoreEnd);
    }

    /// <summary>The playback score of an imported MIDI file: its shared-model notes, tempo map and meter.</summary>
    public static PlaybackScore FromMidi(MidiImport import)
    {
        ArgumentNullException.ThrowIfNull(import);

        return Create(
            import.Notes,
            import.Tempos.Select(static tempo => new PlaybackTempo(tempo.At, tempo.MicrosecondsPerQuarter)),
            import.Meter,
            import.End);
    }

    /// <summary>The playback score of an imported MusicXML file: its shared-model notes, tempo map and meter.</summary>
    public static PlaybackScore FromMusicXml(MusicXmlImport import)
    {
        ArgumentNullException.ThrowIfNull(import);

        return Create(
            import.Notes,
            import.Tempos.Select(static tempo => new PlaybackTempo(tempo.At, tempo.MicrosecondsPerQuarter)),
            import.Meter,
            import.End);
    }

    /// <summary>
    /// The same timeline with other notes, for a second source such as a variation (S1-W5). The
    /// end is kept, or extended if the new notes run past it.
    /// </summary>
    public PlaybackScore WithNotes(IEnumerable<Note> notes) => Create(notes, Tempos, _meter, End);
}

/// <summary>A note as the playback engine plays it.</summary>
/// <param name="Pitch">MIDI note number, 0–127.</param>
/// <param name="Velocity">MIDI velocity, 1–127.</param>
/// <param name="Track">Zero-based track lane, for solo and mute.</param>
public readonly record struct PlaybackNote(TickSpan Span, int Pitch, int Velocity, int Track);

/// <summary>A tempo change: from <paramref name="At"/> on, a quarter note lasts <paramref name="MicrosecondsPerQuarter"/>.</summary>
public readonly record struct PlaybackTempo
{
    /// <summary>120 BPM, the MIDI default.</summary>
    public const int DefaultMicrosecondsPerQuarter = 500_000;

    public PlaybackTempo(Tick at, int microsecondsPerQuarter)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(microsecondsPerQuarter);
        At = at;
        MicrosecondsPerQuarter = microsecondsPerQuarter;
    }

    public Tick At { get; }

    public int MicrosecondsPerQuarter { get; }
}

/// <summary>A metronome beat; downbeats (beat 1 of a bar other than a pickup) are accented.</summary>
public readonly record struct PlaybackBeat(Tick At, bool IsDownbeat);

/// <summary>A bar, for the count-in: a count-in plays one full bar of the bar playback starts in.</summary>
/// <param name="Number">0 for a pickup, otherwise from 1.</param>
/// <param name="BeatTicks">Length of one beat (the time signature's denominator note value).</param>
/// <param name="BeatsPerBar">The time signature's numerator.</param>
public readonly record struct PlaybackBar(int Number, TickSpan Span, long BeatTicks, int BeatsPerBar, bool IsPickup);