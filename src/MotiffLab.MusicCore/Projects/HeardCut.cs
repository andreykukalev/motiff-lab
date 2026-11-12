using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A stretch of a recording heard, by time, cut into a transcription of its own (version 0.5, a piece from a concert): the
/// engine's notes, pedal and keys with their onset in the stretch, on the engine's clock, moved to start at 0; then bars
/// and a tempo worked out afresh from that stretch alone (<see cref="BarGrid.Find"/>), so a piece cut from a concert of
/// several pieces has its own tempo and bars, not the concert's. Its Check flags are worked out again, since its bars are
/// other bars. The collection is never changed.
/// </summary>
/// <remarks>
/// Integer arithmetic only, deterministic: the same collection and stretch give the same piece. A stretch with fewer onsets
/// than a grid needs (<see cref="BarGrid.MinOnsets"/>) keeps the engine's bars, as any short recording does.
/// </remarks>
public static class HeardCut
{
    /// <summary>The most silence a piece's stretch takes before its first note: a second.</summary>
    public const long MostBefore = 1_000_000;

    /// <summary>The most silence a piece's stretch takes after its last note or pedal: two seconds.</summary>
    public const long MostAfter = 2_000_000;

    /// <summary>
    /// The stretch of the recording a piece of the pieces layer takes, in milliseconds from the start of the stretch sent:
    /// from its first onset less a second or half the silence before it, whichever is shorter, to the end of its last note
    /// or pedal plus two seconds or half the silence after it. A span with no note in it takes the time of its bar lines.
    /// </summary>
    /// <param name="piece">The collection as opened, on its bars.</param>
    /// <param name="span">The piece's place in the collection's notes.</param>
    public static (long From, long To) StretchOf(ImportedPiece piece, HeardClock clock, TickSpan span)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(clock);
        Note[] inside = [.. piece.Notes.Where(note => span.Contains(note.Span.Onset))];
        if (inside.Length == 0)
        {
            return StretchOfBars(clock, span);
        }

        long first = clock.MicrosecondsAt(inside.Min(static note => note.Span.Onset));
        IEnumerable<Tick> ends = inside.Select(static note => note.Span.End)
            .Concat((piece.Midi?.SustainPedal ?? []).Where(held => span.Contains(held.Span.Onset)).Select(static held => held.Span.End));
        long last = clock.MicrosecondsAt(ends.Max());
        Tick[] before = [.. piece.Notes.Where(note => note.Span.Onset < span.Onset).Select(static note => note.Span.End)];
        long sounding = before.Length == 0 ? 0 : Math.Min(first, clock.MicrosecondsAt(before.Max()));
        Tick[] after = [.. piece.Notes.Where(note => note.Span.Onset >= span.End).Select(static note => note.Span.Onset)];
        long next = after.Length == 0 ? last + (2 * MostAfter) : Math.Max(last, clock.MicrosecondsAt(after.Min()));
        long from = Math.Max(0, first - Math.Min(MostBefore, (first - sounding) / 2));
        long to = last + Math.Min(MostAfter, (next - last) / 2);
        return (from / 1000, (to + 999) / 1000);
    }

    /// <summary>The stretch of a fragment chosen by its bars: from when its first bar line was played to when its last was, in milliseconds.</summary>
    public static (long From, long To) StretchOfBars(HeardClock clock, TickSpan span)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return (clock.MicrosecondsAt(span.Onset) / 1000, (clock.MicrosecondsAt(span.End) + 999) / 1000);
    }

    /// <summary>The stretch <paramref name="from"/>–<paramref name="to"/> of the collection's recording as a transcription of its own, on bars found afresh.</summary>
    /// <param name="engine">The engine's MIDI, the collection's source, as read: on the recording's clock.</param>
    /// <param name="linked">Whether the part keeps the collection's original, which its notes were then heard in.</param>
    /// <exception cref="ArgumentException">The stretch is empty, starts before 0, or holds no note.</exception>
    internal static PieceParts.Cut Of(Project collection, MidiImport engine, long from, long to, bool linked)
    {
        if (from < 0 || to <= from)
        {
            throw new ArgumentException($"A stretch of the recording runs forwards from 0 ms; not {from}–{to} ms.", nameof(from));
        }

        TranscriptionOrigin heard = collection.Transcription!;
        HeardClock clock = HeardClock.OfEngine(engine);
        Tick start = clock.TickAt(from * 1000), stop = clock.TickAt(to * 1000);
        Note[] notes = [.. engine.Notes.Where(note => clock.MicrosecondsAt(note.Span.Onset) >= from * 1000 && clock.MicrosecondsAt(note.Span.Onset) < to * 1000)];
        if (notes.Length == 0)
        {
            throw new ArgumentException($"No note was heard in {from}–{to} ms.", nameof(to));
        }

        byte[] midi = Midi(engine, notes, start, stop);
        ImportedPiece read = PieceImporter.Import("part.mid", midi).Piece is { Midi: not null } imported ? imported : throw new ArgumentException("The notes cut could not be read.", nameof(collection));
        BarGrid? bars = BarGrid.Find(read.Midi!);
        var origin = new TranscriptionOrigin(heard.Engine, bars)
        {
            Seams = [.. heard.Seams.Where(seam => seam > from && seam < to).Select(seam => seam - from)],
            From = linked ? PieceParts.OriginalId : null,
            At = heard.At,
        };
        ImportedPiece piece = Transcribed.Mark(bars is { } grid ? grid.Apply(read) : read);
        return new PieceParts.Cut(midi, ".mid", origin, (from, to)) { Check = CheckFlag.FromTranscription(origin, midi, piece) };
    }

    /// <summary>The notes, pedal, keys, tempo and meter of the engine's MIDI from <paramref name="start"/>, moved to tick 0, as a MIDI file.</summary>
    private static byte[] Midi(MidiImport engine, Note[] notes, Tick start, Tick stop)
    {
        var file = new RecordingCut.MidiWriter(Math.Max(1, engine.Tracks.Count), engine.Tracks);
        foreach (Note note in notes)
        {
            MidiSourceLocation source = note.Source as MidiSourceLocation ?? new MidiSourceLocation(note.Track, 0, 0);
            file.Note(note.Track, source.Channel, note.Pitch.MidiNumber, note.Velocity, note.Span.Onset.Value - start.Value, note.Span.End.Value - start.Value);
        }

        foreach (MidiPedal held in engine.SustainPedal.Where(held => held.Span.End > start && held.Span.Onset < stop))
        {
            file.Pedal(held.Track, held.Channel, Math.Max(0, held.Span.Onset.Value - start.Value), held.Span.End.Value - start.Value);
        }

        foreach (MidiKeySignature key in RecordingCut.Since(engine.KeySignatures, static key => key.At, start))
        {
            file.Key(Math.Max(0, key.At.Value - start.Value), key.Sharps, key.IsMinor);
        }

        foreach (MidiTempoChange tempo in RecordingCut.Since(engine.Tempos.Where(static tempo => !tempo.IsAssumed), static tempo => tempo.At, start))
        {
            file.Tempo(Math.Max(0, tempo.At.Value - start.Value), tempo.MicrosecondsPerQuarter);
        }

        foreach (MidiTimeSignature meter in RecordingCut.Since(engine.TimeSignatures.Where(static meter => !meter.IsAssumed), static meter => meter.At, start))
        {
            file.Meter(Math.Max(0, meter.At.Value - start.Value), meter.Numerator, meter.Denominator);
        }

        return file.Write();
    }
}