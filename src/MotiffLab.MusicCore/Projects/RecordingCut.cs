using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Export;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// A stretch of bars of a recording heard (0014, 0016) cut into a transcription of its own, for <see cref="PieceParts"/>:
/// its notes and pedal from tick 0, as a MIDI file on the bars' own clock, and the bars worked out for the collection
/// carried over as a grid for the part, so the part opens on the same bars, still inferred, and plays the stretch as it was
/// played. Also the stretch of the sound the part takes, in milliseconds, for the app to play or keep.
/// </summary>
/// <remarks>
/// <para>
/// The engine's MIDI cannot be cut exactly where a bar begins, since a bar line is a moment between its ticks. So the
/// part's MIDI is written on the bars: each note where the collection's bars put it, with a tempo on every beat of the
/// part that lasts as long as that beat was played (to the microsecond, a whole number of them for MIDI), and the part's
/// grid has its beats there. Its bars then put every note back on the same tick. A part that begins inside a beat of the
/// collection (a bar line off the beat) takes a beat of a 16th.
/// </para>
/// <para>Integer arithmetic only, deterministic. Without bars worked out, the engine's MIDI is cut at the bar line and its own tempo and meter go with it.</para>
/// </remarks>
internal static class RecordingCut
{
    private const int TicksPerQuarter = CanonicalTime.TicksPerQuarter;

    /// <summary>The part of the recording that <paramref name="span"/> of the collection's bars takes.</summary>
    /// <param name="piece">The collection as opened: on its bars, if any, with the notes marked detected.</param>
    /// <param name="engine">The engine's MIDI, the collection's source, as read: on the recording's clock.</param>
    /// <param name="linked">Whether the part keeps the collection's original, which the part's notes were then heard in.</param>
    public static PieceParts.Cut Of(Project collection, ImportedPiece piece, MidiImport engine, TickSpan span, bool linked)
    {
        TranscriptionOrigin heard = collection.Transcription!;
        MidiImport midi = piece.Midi!;
        long start = span.Onset.Value;
        Note[] notes = [.. piece.Notes.Where(note => note.Span.Onset >= span.Onset && note.Span.Onset < span.End)];
        long end = Math.Max(span.End.Value, notes.Select(static note => note.Span.End.Value).DefaultIfEmpty(0).Max()) - start;
        var file = new MidiWriter(Math.Max(1, midi.Tracks.Count), engine.Tracks);
        foreach (Note note in notes)
        {
            MidiSourceLocation source = note.Source as MidiSourceLocation ?? new MidiSourceLocation(note.Track, 0, 0);
            file.Note(note.Track, source.Channel, note.Pitch.MidiNumber, note.Velocity, note.Span.Onset.Value - start, note.Span.End.Value - start);
        }

        foreach (MidiPedal held in midi.SustainPedal.Where(held => held.Span.End > span.Onset && held.Span.Onset < span.End))
        {
            file.Pedal(held.Track, held.Channel, Math.Max(0, held.Span.Onset.Value - start), held.Span.End.Value - start);
        }

        foreach (MidiKeySignature key in Since(midi.KeySignatures, static key => key.At, span.Onset))
        {
            file.Key(Math.Max(0, key.At.Value - start), key.Sharps, key.IsMinor);
        }

        long fromMicroseconds, toMicroseconds;
        BarGrid? bars = null;
        if (piece.Bars is { } grid)
        {
            long origin = Units(engine.Tempos, grid.Origin) / TicksPerQuarter;
            (bars, IReadOnlyList<(long Tick, int PerQuarter)> tempos) = Bars(grid, start, end);
            foreach ((long tick, int perQuarter) in tempos)
            {
                file.Tempo(tick, perQuarter);
            }

            (fromMicroseconds, toMicroseconds) = (origin + Played(grid, start), origin + Played(grid, span.End.Value));
        }
        else
        {
            foreach (MidiTempoChange tempo in Since(midi.Tempos.Where(static tempo => !tempo.IsAssumed), static tempo => tempo.At, span.Onset))
            {
                file.Tempo(Math.Max(0, tempo.At.Value - start), tempo.MicrosecondsPerQuarter);
            }

            foreach (MidiTimeSignature meter in Since(midi.TimeSignatures.Where(static meter => !meter.IsAssumed), static meter => meter.At, span.Onset))
            {
                file.Meter(Math.Max(0, meter.At.Value - start), meter.Numerator, meter.Denominator);
            }

            (fromMicroseconds, toMicroseconds) = (Units(midi.Tempos, span.Onset) / TicksPerQuarter, Units(midi.Tempos, span.End) / TicksPerQuarter);
        }

        (long from, long to) = (fromMicroseconds / 1000, toMicroseconds / 1000);
        var part = new TranscriptionOrigin(heard.Engine, bars)
        {
            Seams = [.. heard.Seams.Select(seam => seam - from).Where(seam => seam > 0 && seam < to - from)],
            From = linked ? PieceParts.OriginalId : null,
            At = heard.At,
        };
        return new PieceParts.Cut(file.Write(), ".mid", part, (from, to));
    }

    /// <summary>
    /// The part's grid and the tempo of its MIDI: beats where the collection's beats were played, from the part's first bar
    /// line, through its end. A beat lasts a whole number of microseconds that MIDI can write as a tempo for it, so the grid
    /// read back from those tempos puts each note on the tick it was on.
    /// </summary>
    private static (BarGrid Grid, IReadOnlyList<(long Tick, int PerQuarter)> Tempos) Bars(BarGrid grid, long start, long end)
    {
        long beatTicks = grid.StepsPerBeat * BarGrid.StepTicks;
        int steps = start % beatTicks == 0 ? grid.StepsPerBeat : 1;
        long ticks = steps * BarGrid.StepTicks;
        int sixteenths = (int)(start / BarGrid.StepTicks);
        int[] barStarts = start == 0 ? [.. grid.BarStarts] : [0, .. grid.BarStarts.Where(bar => bar > sixteenths).Select(bar => bar - sixteenths)];
        if (steps == grid.StepsPerBeat && !grid.FollowsPlaying)
        {
            return (new BarGrid(Tick.Zero, grid.SixteenthMicroseconds, grid.Signature, barStarts, [], steps), [(0, 4 * grid.SixteenthMicroseconds)]);
        }

        // A beat of `steps` 16ths lasts `length` µs: its tempo is 4 × length / steps µs a quarter, whole when length is a multiple of this.
        long multiple = steps / (steps % 4 == 0 ? 4 : steps % 2 == 0 ? 2 : 1);
        long first = Played(grid, start);
        int count = (int)((end + ticks - 1) / ticks) + 1;
        long[] beats = new long[count];
        for (int beat = 1; beat < count; beat++)
        {
            long played = Played(grid, start + (beat * ticks)) - first;
            beats[beat] = Math.Max(beats[beat - 1] + multiple, (played + (multiple / 2)) / multiple * multiple);
        }

        var tempos = new List<(long, int)>();
        for (int beat = 0; beat + 1 < count; beat++)
        {
            tempos.Add((beat * ticks, (int)((beats[beat + 1] - beats[beat]) * 4 / steps)));
        }

        tempos.Add(((count - 1) * ticks, 4 * grid.SixteenthMicroseconds));
        return (new BarGrid(Tick.Zero, grid.SixteenthMicroseconds, grid.Signature, barStarts, beats, steps), tempos);
    }

    /// <summary>When a tick of the grid's bars was played, in microseconds after the grid's origin: between its beat's two ends.</summary>
    private static long Played(BarGrid grid, long tick)
    {
        long beatTicks = grid.StepsPerBeat * BarGrid.StepTicks;
        long beat = tick / beatTicks, within = tick % beatTicks;
        long from = grid.BeatAt(beat);
        return from + (within * (grid.BeatAt(beat + 1) - from) / beatTicks);
    }

    /// <summary>A tick on a MIDI file's clock in microseconds times <see cref="TicksPerQuarter"/>, exactly.</summary>
    private static long Units(IReadOnlyList<MidiTempoChange> tempos, Tick tick)
    {
        (long At, long PerQuarter)[] segments = [(0, MidiTempoChange.DefaultMicrosecondsPerQuarter), .. tempos.Where(tempo => tempo.At <= tick).OrderBy(static tempo => tempo.At).Select(static tempo => (tempo.At.Value, (long)tempo.MicrosecondsPerQuarter))];
        return segments.Select((segment, index) => ((index + 1 < segments.Length ? segments[index + 1].At : tick.Value) - segment.At) * segment.PerQuarter).Sum();
    }

    /// <summary>The changes in effect from <paramref name="from"/>: the last one at or before it, then every later one.</summary>
    internal static IEnumerable<T> Since<T>(IEnumerable<T> changes, Func<T, Tick> at, Tick from)
    {
        T[] ordered = [.. changes.OrderBy(at)];
        T[] before = [.. ordered.Where(change => at(change) <= from)];
        return [.. before.TakeLast(1), .. ordered.Where(change => at(change) > from)];
    }

    /// <summary>A type 1 MIDI file at 480 ticks a quarter, its tracks numbered as the engine's were, so the part reads its notes alike.</summary>
    internal sealed class MidiWriter(int tracks, IReadOnlyList<MidiTrackInfo> names)
    {
        private readonly List<(long Tick, int Order, byte[] Data)>[] _tracks = [.. Enumerable.Range(0, tracks).Select(static _ => new List<(long, int, byte[])>())];
        private readonly List<(long Tick, int Channel, int Track, int Pitch, int Velocity, long End)> _notes = [];

        public void Note(int track, int channel, int pitch, int velocity, long on, long off) => _notes.Add((on, channel, Math.Min(track, tracks - 1), pitch, velocity, off));

        public void Pedal(int track, int channel, long down, long up)
        {
            int index = Math.Min(track, tracks - 1);
            _tracks[index].Add((down, 0, [(byte)(0xB0 | channel), 64, 127]));
            _tracks[index].Add((Math.Max(up, down + 1), 0, [(byte)(0xB0 | channel), 64, 0]));
        }

        public void Tempo(long tick, int perQuarter) =>
            _tracks[0].Add((tick, -1, [0xFF, 0x51, 0x03, (byte)(perQuarter >> 16), (byte)(perQuarter >> 8), (byte)perQuarter]));

        public void Meter(long tick, int numerator, int denominator) =>
            _tracks[0].Add((tick, -1, [0xFF, 0x58, 0x04, (byte)numerator, (byte)int.Log2(denominator), 24, 8]));

        public void Key(long tick, int sharps, bool minor) =>
            _tracks[0].Add((tick, -1, [0xFF, 0x59, 0x02, (byte)(sbyte)sharps, (byte)(minor ? 1 : 0)]));

        public byte[] Write()
        {
            // A key is let go before it is struck again on the same track and channel.
            foreach (var key in _notes.GroupBy(static note => (note.Track, note.Channel, note.Pitch)))
            {
                var strikes = key.OrderBy(static note => note.Tick).ToArray();
                for (int i = 0; i < strikes.Length; i++)
                {
                    long off = i + 1 < strikes.Length ? Math.Min(strikes[i].End, strikes[i + 1].Tick) : strikes[i].End;
                    byte status = (byte)key.Key.Channel;
                    _tracks[key.Key.Track].Add((strikes[i].Tick, 2, [(byte)(0x90 | status), (byte)key.Key.Pitch, (byte)Math.Clamp(strikes[i].Velocity, 1, 127)]));
                    _tracks[key.Key.Track].Add((Math.Max(off, strikes[i].Tick + 1), 1, [(byte)(0x80 | status), (byte)key.Key.Pitch, 64]));
                }
            }

            using var file = new MemoryStream();
            file.Write("MThd"u8);
            file.Write([0, 0, 0, 6, 0, 1, (byte)(tracks >> 8), (byte)tracks, TicksPerQuarter >> 8, TicksPerQuarter & 0xFF]);
            for (int track = 0; track < tracks; track++)
            {
                byte[] bytes = Track(track);
                file.Write("MTrk"u8);
                file.Write([(byte)(bytes.Length >> 24), (byte)(bytes.Length >> 16), (byte)(bytes.Length >> 8), (byte)bytes.Length]);
                file.Write(bytes);
            }

            return file.ToArray();
        }

        private byte[] Track(int index)
        {
            using var track = new MemoryStream();
            if (index < names.Count && names[index].Name is { Length: > 0 } name)
            {
                byte[] text = Encoding.UTF8.GetBytes(name);
                track.WriteByte(0);
                track.Write([0xFF, 0x03]);
                MidiExporter.WriteVariableLength(track, text.Length);
                track.Write(text);
            }

            long last = 0;
            foreach ((long tick, _, byte[] data) in _tracks[index].Select(static (e, i) => (e, i)).OrderBy(static x => x.e.Tick).ThenBy(static x => x.e.Order).ThenBy(static x => x.i).Select(static x => x.e))
            {
                MidiExporter.WriteVariableLength(track, tick - last);
                track.Write(data);
                last = tick;
            }

            MidiExporter.WriteVariableLength(track, 0);
            track.Write([0xFF, 0x2F, 0x00]);
            return track.ToArray();
        }
    }
}