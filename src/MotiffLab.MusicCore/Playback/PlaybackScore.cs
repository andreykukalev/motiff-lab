using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
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
    private PlaybackScore(
        IReadOnlyList<PlaybackNote> notes,
        IReadOnlyList<PlaybackTempo> tempos,
        IReadOnlyList<PlaybackBeat> beats,
        IReadOnlyList<PlaybackBar> bars,
        MeterMap meter,
        Tick end,
        IReadOnlyList<PlaybackPedal> pedal)
    {
        Pedal = pedal;
        Meter = meter;
        Notes = notes;
        Tempos = tempos;
        Beats = beats;
        Bars = bars;
        End = end;
    }

    /// <summary>Notes ordered by onset, then pitch, then track, each sounding as long as its key or the sustain pedal holds it.</summary>
    public IReadOnlyList<PlaybackNote> Notes { get; }

    /// <summary>Where the sustain pedal is held down, which every source on this timeline sounds with.</summary>
    public IReadOnlyList<PlaybackPedal> Pedal { get; }

    /// <summary>Tempo changes in order; the first is at tick 0.</summary>
    public IReadOnlyList<PlaybackTempo> Tempos { get; }

    /// <summary>Every metronome beat before <see cref="End"/>, in order.</summary>
    public IReadOnlyList<PlaybackBeat> Beats { get; }

    /// <summary>The bars that start before <see cref="End"/>, for the count-in and bar snapping.</summary>
    public IReadOnlyList<PlaybackBar> Bars { get; }

    /// <summary>The bars of the piece, as the metronome and count-in follow them.</summary>
    public MeterMap Meter { get; }

    /// <summary>Where playback stops when it is not looping: the end of the piece or of its last note, whichever is later.</summary>
    public Tick End { get; }

    /// <param name="notes">Notes of the piece or motif, in any order.</param>
    /// <param name="tempos">Tempo changes. Empty means 120 BPM throughout; otherwise the first must be at tick 0 and positions must strictly increase.</param>
    /// <param name="meter">The bars of the piece, for the metronome and count-in.</param>
    /// <param name="end">The end of the piece; extended to the last note's end if that is later.</param>
    /// <param name="pedal">
    /// Where the sustain pedal is held down. A note still sounding when the pedal goes down, or let go
    /// while it is down, sounds on until the pedal comes up, or until its key is struck again on the
    /// same track. The notes themselves keep their lengths; only their sound is held.
    /// </param>
    public static PlaybackScore Create(IEnumerable<Note> notes, IEnumerable<PlaybackTempo> tempos, MeterMap meter, Tick end, IEnumerable<PlaybackPedal>? pedal = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(tempos);
        ArgumentNullException.ThrowIfNull(meter);

        PlaybackPedal[] held = [.. (pedal ?? []).OrderBy(static span => span.Span.Onset).ThenBy(static span => span.Track)];
        PlaybackNote[] ordered = Sustained([.. notes.Select(static note => new PlaybackNote(note.Span, note.Pitch.MidiNumber, note.Velocity, note.Track))], held)
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

        return Build(ordered, tempoMap, meter, end, held);
    }

    /// <summary>Each note's sound held by the pedal on its track (see <see cref="Create"/>).</summary>
    private static PlaybackNote[] Sustained(PlaybackNote[] notes, PlaybackPedal[] pedal)
    {
        if (pedal.Length == 0)
        {
            return notes;
        }

        // The next strike of each key on each track, which damps the held sound.
        var byKey = notes
            .GroupBy(static note => (note.Track, note.Pitch))
            .ToDictionary(static group => group.Key, static group => group.Select(static note => note.Span.Onset.Value).Order().ToArray());
        var spans = pedal.GroupBy(static span => span.Track).ToDictionary(static group => group.Key, static group => group.Select(static span => span.Span).ToArray());
        var sounded = new PlaybackNote[notes.Length];
        for (int i = 0; i < notes.Length; i++)
        {
            PlaybackNote note = notes[i];
            long release = note.Span.End.Value;
            if (spans.TryGetValue(note.Track, out TickSpan[]? held))
            {
                foreach (TickSpan span in held)
                {
                    // Spans are in order; one lifted and pressed again at once still damps the sound.
                    if (span.Onset.Value <= release && release < span.End.Value)
                    {
                        release = span.End.Value;
                        break;
                    }
                }
            }

            long[] strikes = byKey[(note.Track, note.Pitch)];
            int next = Array.BinarySearch(strikes, note.Span.Onset.Value + 1);
            next = next >= 0 ? next : ~next;
            if (next < strikes.Length && strikes[next] < release)
            {
                release = Math.Max(strikes[next], note.Span.End.Value);
            }

            sounded[i] = release == note.Span.End.Value ? note : note with { Span = TickSpan.FromBounds(note.Span.Onset, new Tick(release)) };
        }

        return sounded;
    }

    /// <summary>Bars, beats and the end around notes and a tempo map that are already ordered and checked.</summary>
    private static PlaybackScore Build(PlaybackNote[] ordered, PlaybackTempo[] tempoMap, MeterMap meter, Tick end, PlaybackPedal[] pedal)
    {
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

        return new PlaybackScore(ordered, tempoMap, beats, bars, meter, scoreEnd, pedal);
    }

    /// <summary>The playback score of an imported MIDI file: its shared-model notes, tempo map and meter.</summary>
    public static PlaybackScore FromMidi(MidiImport import)
    {
        ArgumentNullException.ThrowIfNull(import);

        return Create(
            import.Notes,
            import.Tempos.Select(static tempo => new PlaybackTempo(tempo.At, tempo.MicrosecondsPerQuarter)),
            import.Meter,
            import.End,
            import.SustainPedal.Select(held => new PlaybackPedal(held.Span, PedalTrack(import, held))));
    }

    /// <summary>The track whose notes a pedal holds: the shared-model track of the notes on its source track and channel, or its source track.</summary>
    private static int PedalTrack(MidiImport import, MidiPedal held)
    {
        int index = import.SourceNotes.ToList().FindIndex(note => note.Track == held.Track && note.Channel == held.Channel);
        return index >= 0 ? import.Notes[index].Track : held.Track;
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
    public PlaybackScore WithNotes(IEnumerable<Note> notes) => Create(notes, Tempos, Meter, End, Pedal);

    /// <summary>The same notes and timeline, running to <paramref name="end"/> when that is later than <see cref="End"/>.</summary>
    public PlaybackScore ExtendedTo(Tick end) => end > End ? Build([.. Notes], [.. Tempos], Meter, end, [.. Pedal]) : this;

    /// <summary>The playback score of an imported piece, whichever format it came from.</summary>
    public static PlaybackScore FromPiece(ImportedPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);

        return piece.Midi is { } midi ? FromMidi(midi) : FromMusicXml(piece.MusicXml!);
    }
}

/// <summary>A note as the playback engine plays it.</summary>
/// <param name="Pitch">MIDI note number, 0–127.</param>
/// <param name="Velocity">MIDI velocity, 1–127.</param>
/// <param name="Track">Zero-based track lane, for solo and mute.</param>
public readonly record struct PlaybackNote(TickSpan Span, int Pitch, int Velocity, int Track);

/// <summary>The sustain pedal held down over <paramref name="Span"/>, for the notes of <paramref name="Track"/>.</summary>
public readonly record struct PlaybackPedal(TickSpan Span, int Track);

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