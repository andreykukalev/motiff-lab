using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Transcription;

/// <summary>
/// A recording longer than the scan server hears in one request, heard in parts (decision record 0014):
/// the sound is cut into parts of about two minutes that overlap by five seconds each side, so the
/// engine hears every note with what leads into it, and the notes that come back are joined into one
/// MIDI file. A recording of any length is heard so, and the parts may be heard in any order or at once.
/// Given the recording's loudness, each seam goes to the quietest moment within
/// <see cref="SeamWindowMilliseconds"/> of the even cut, so it falls less often inside a chord. Each part
/// keeps only the notes that start in its own stretch; a note heard twice at a seam is kept once, a note
/// still held when a part's sound ends keeps the length the next part hears it go on for, and the pedal
/// held across a seam stays one press. Pure and deterministic.
/// </summary>
public static class RecordingParts
{
    /// <summary>The longest stretch a part keeps when the seams are cut evenly.</summary>
    public const long PartMilliseconds = 120_000;

    /// <summary>The sound sent before and after a part's own stretch.</summary>
    public const long OverlapMilliseconds = 5_000;

    /// <summary>How far a seam may move from the even cut to a quieter moment, either way.</summary>
    public const long SeamWindowMilliseconds = 10_000;

    /// <summary>
    /// The longest stretch a part keeps once its seams have moved: two minutes and one seam window, so a part sends at
    /// most 140 s of sound and the scan server, which hears 150 s a request, hears all of it.
    /// </summary>
    public const long MostKeptMilliseconds = PartMilliseconds + SeamWindowMilliseconds;

    /// <summary>The stretch of sound each loudness level covers, from the start of the recording.</summary>
    public const long LevelMilliseconds = 100;

    /// <summary>A note this close to one of the same key kept from the part before is the same note heard again.</summary>
    public const long SameNoteMilliseconds = 60;

    /// <summary>A note sounding until this close to the end of a part's sound was still held when that sound ended.</summary>
    public const long HeldToTheEndMilliseconds = 500;

    /// <summary>How many levels either side of a moment count towards how quiet it is: half a second in all.</summary>
    private const int QuietReach = 2;

    /// <summary>The joined file's tempo: 120 BPM, as an engine writes it, so a tick is 500,000 / 480 microseconds.</summary>
    private const int MicrosecondsPerQuarter = 500_000;

    private const int TicksPerQuarter = CanonicalTime.TicksPerQuarter;

    /// <summary>
    /// The parts a recording of <paramref name="milliseconds"/> is heard in: one for up to two minutes,
    /// otherwise as few equal stretches as keep each to two minutes, cut evenly.
    /// </summary>
    public static IReadOnlyList<RecordingPart> Plan(long milliseconds) => Plan(milliseconds, []);

    /// <summary>
    /// The parts a recording of <paramref name="milliseconds"/> is heard in, as many as <see cref="Plan(long)"/> plans,
    /// each seam at the quietest moment within <see cref="SeamWindowMilliseconds"/> of the even cut by
    /// <paramref name="levels"/>: how loud the recording is in each <see cref="LevelMilliseconds"/> from its start, on
    /// any scale, larger louder. Of moments as quiet, the one nearest the even cut wins, then the earlier; a seam with no
    /// levels near it stays at the even cut, as all do without levels. No part keeps more than
    /// <see cref="MostKeptMilliseconds"/>.
    /// </summary>
    public static IReadOnlyList<RecordingPart> Plan(long milliseconds, IReadOnlyList<int> levels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(milliseconds);
        ArgumentNullException.ThrowIfNull(levels);

        int count = (int)((milliseconds + PartMilliseconds - 1) / PartMilliseconds);
        var cuts = new long[count + 1];
        cuts[count] = milliseconds;
        for (int i = 1; i < count; i++)
        {
            long even = milliseconds * i / count;
            long earliest = Math.Max(even - SeamWindowMilliseconds, cuts[i - 1] + 1);
            long latest = Math.Min(even + SeamWindowMilliseconds, cuts[i - 1] + MostKeptMilliseconds);
            cuts[i] = QuietestNear(levels, even, earliest, latest);
        }

        var parts = new RecordingPart[count];
        for (int i = 0; i < count; i++)
        {
            long keepFrom = cuts[i], keepTo = cuts[i + 1];
            parts[i] = new RecordingPart(
                i,
                count == 1 ? 0 : Math.Max(0, keepFrom - OverlapMilliseconds),
                count == 1 ? milliseconds : Math.Min(milliseconds, keepTo + OverlapMilliseconds),
                keepFrom,
                keepTo);
        }

        return parts;
    }

    /// <summary>
    /// The quietest moment from <paramref name="earliest"/> to <paramref name="latest"/>: the middle of the level whose
    /// half second around it is quietest, the nearest <paramref name="even"/> among equals; <paramref name="even"/> when
    /// no level lies there.
    /// </summary>
    private static long QuietestNear(IReadOnlyList<int> levels, long even, long earliest, long latest)
    {
        const long Half = LevelMilliseconds / 2;
        // Only a level with its whole half second known: every moment is weighed over as much sound.
        long first = Math.Max(QuietReach, (earliest - Half + LevelMilliseconds - 1) / LevelMilliseconds);
        long last = Math.Min(levels.Count - 1L - QuietReach, (latest - Half) / LevelMilliseconds);
        long best = even, quietest = long.MaxValue;
        for (long level = first; level <= last; level++)
        {
            long loudness = 0;
            for (long near = level - QuietReach; near <= level + QuietReach; near++)
            {
                loudness += Math.Max(0, levels[(int)near]);
            }

            long moment = (level * LevelMilliseconds) + Half;
            if (loudness < quietest || (loudness == quietest && Math.Abs(moment - even) < Math.Abs(best - even)))
            {
                (best, quietest) = (moment, loudness);
            }
        }

        return best;
    }

    /// <summary>Where the parts meet, in milliseconds from the start of the recording: each part's end but the last's.</summary>
    public static IReadOnlyList<long> Seams(IReadOnlyList<RecordingPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return [.. parts.OrderBy(part => part.Index).SkipLast(1).Select(part => part.KeepToMilliseconds)];
    }

    /// <summary>
    /// The notes and pedal heard in each part, on the recording's own clock, as one type 1 MIDI file at
    /// 480 ticks a quarter and 120 BPM in 4/4, the shape an engine writes. A part's file is read with the
    /// MIDI importer; a part that is not a MIDI file throws <see cref="MidiImportException"/>.
    /// </summary>
    public static byte[] Join(IReadOnlyList<(RecordingPart Part, byte[] Midi)> heard)
    {
        ArgumentNullException.ThrowIfNull(heard);

        // Each part's notes and pedal on the recording's clock, in microseconds, the parts in order.
        Heard[] parts = [.. heard.OrderBy(entry => entry.Part.Index).Select(entry => Heard.Of(entry.Part, entry.Midi))];
        var notes = new List<(long On, long Off, int Pitch, int Velocity)>();
        var pedal = new List<(long Down, long Up)>();
        var previous = new List<(long On, int Pitch)>();
        long seam = SameNoteMilliseconds * 1000;
        for (int p = 0; p < parts.Length; p++)
        {
            (RecordingPart part, IReadOnlyList<HeardNote> heardNotes, IReadOnlyList<(long Down, long Up)> heardPedal) = parts[p];
            Heard? next = p + 1 < parts.Length && parts[p + 1].Part.Index == part.Index + 1 ? parts[p + 1] : null;
            long keepFrom = part.KeepFromMilliseconds * 1000, keepTo = part.KeepToMilliseconds * 1000;

            // A note at the seam may be heard a little either side of it by the two parts: this part also
            // keeps one just after its stretch, and the next part does not keep it again.
            var kept = new List<(long On, int Pitch)>();
            foreach (HeardNote note in heardNotes)
            {
                bool again = previous.Any(other => other.Pitch == note.Pitch && Math.Abs(other.On - note.On) <= seam);
                if (note.On >= keepFrom && note.On < keepTo + seam && !again)
                {
                    notes.Add((note.On, Math.Max(HeldOn(note, part, next), note.On + 1), note.Pitch, note.Velocity));
                    kept.Add((note.On, note.Pitch));
                }
            }

            // Only the notes near the seam can be heard again by the next part.
            previous = [.. kept.Where(note => note.On >= keepTo - seam)];
            foreach ((long heardDown, long heardUp) in heardPedal)
            {
                long down = Math.Max(keepFrom, heardDown), up = Math.Min(keepTo, heardUp);
                if (up > down)
                {
                    pedal.Add((down, up));
                }
            }
        }

        // A press cut at a seam, and the same press heard again in the next part, are one press.
        var joined = new List<(long Down, long Up)>();
        foreach ((long down, long up) in pedal.OrderBy(held => held.Down))
        {
            if (joined.Count > 0 && down <= joined[^1].Up)
            {
                joined[^1] = (joined[^1].Down, Math.Max(joined[^1].Up, up));
            }
            else
            {
                joined.Add((down, up));
            }
        }

        return Write(notes, joined);
    }

    /// <summary>
    /// Where a note a part keeps ends: where the part heard it end, or, when it still sounded as the part's sound ran
    /// out, where the next part hears it end, when the next part hears it go on: the same key sounding from within the
    /// note, before the next part's own stretch, and longer.
    /// </summary>
    private static long HeldOn(HeardNote note, RecordingPart part, Heard? next)
    {
        if (next is null || note.Off < (part.SendToMilliseconds - HeldToTheEndMilliseconds) * 1000)
        {
            return note.Off;
        }

        long seam = SameNoteMilliseconds * 1000, nextOwn = next.Part.KeepFromMilliseconds * 1000;
        long off = note.Off;
        foreach (HeardNote later in next.Notes)
        {
            if (later.Pitch == note.Pitch && later.On >= note.On - seam && later.On <= note.Off && later.On < nextOwn + seam)
            {
                off = Math.Max(off, later.Off);
            }
        }

        return off;
    }

    /// <summary>What one part heard, on the recording's clock in microseconds: its notes by onset and key, and its pedal.</summary>
    private sealed record Heard(RecordingPart Part, IReadOnlyList<HeardNote> Notes, IReadOnlyList<(long Down, long Up)> Pedal)
    {
        public static Heard Of(RecordingPart part, byte[] midi)
        {
            MidiImport import = MidiImporter.Import(midi);
            Func<Tick, long> clock = Clock(import.Tempos, part.SendFromMilliseconds * 1000);
            return new(
                part,
                [.. import.SourceNotes.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch)
                    .Select(note => new HeardNote(clock(note.Span.Onset), clock(note.Span.End), note.Pitch, note.Velocity))],
                [.. import.SustainPedal.Select(held => (clock(held.Span.Onset), clock(held.Span.End)))]);
        }
    }

    /// <summary>A note one part heard, on the recording's clock in microseconds.</summary>
    private readonly record struct HeardNote(long On, long Off, int Pitch, int Velocity);

    /// <summary>Microseconds into the recording at a tick of a part's file, given where the part starts.</summary>
    private static Func<Tick, long> Clock(IReadOnlyList<MidiTempoChange> tempos, long startMicroseconds)
    {
        MidiTempoChange[] map = [.. tempos.OrderBy(tempo => tempo.At)];
        return tick =>
        {
            // Microseconds a quarter times ticks: divided by the ticks in a quarter once, at the end.
            long units = 0, at = 0, perQuarter = MidiTempoChange.DefaultMicrosecondsPerQuarter;
            foreach (MidiTempoChange tempo in map)
            {
                if (tempo.At.Value > tick.Value)
                {
                    break;
                }

                units += (tempo.At.Value - at) * perQuarter;
                (at, perQuarter) = (tempo.At.Value, tempo.MicrosecondsPerQuarter);
            }

            return startMicroseconds + ((units + ((tick.Value - at) * perQuarter)) / TicksPerQuarter);
        };
    }

    private static long Ticks(long microseconds) => ((microseconds * TicksPerQuarter) + (MicrosecondsPerQuarter / 2)) / MicrosecondsPerQuarter;

    private static byte[] Write(List<(long On, long Off, int Pitch, int Velocity)> notes, List<(long Down, long Up)> pedal)
    {
        // Events by tick; at one tick, the pedal and note-offs before note-ons.
        var events = new List<(long Tick, int Order, byte[] Data)>();
        foreach (IGrouping<int, (long On, long Off, int Pitch, int Velocity)> key in notes.GroupBy(note => note.Pitch))
        {
            (long On, long Off, int Pitch, int Velocity)[] strikes = [.. key.OrderBy(note => note.On)];
            for (int i = 0; i < strikes.Length; i++)
            {
                long on = Ticks(strikes[i].On), off = Ticks(strikes[i].Off);

                // A key is let go before it is struck again.
                if (i + 1 < strikes.Length)
                {
                    off = Math.Min(off, Ticks(strikes[i + 1].On));
                }

                off = Math.Max(off, on + 1);
                events.Add((on, 2, [0x90, (byte)key.Key, (byte)Math.Clamp(strikes[i].Velocity, 1, 127)]));
                events.Add((off, 1, [0x80, (byte)key.Key, 64]));
            }
        }

        foreach ((long down, long up) in pedal)
        {
            events.Add((Ticks(down), 0, [0xB0, 64, 127]));
            events.Add((Math.Max(Ticks(up), Ticks(down) + 1), 0, [0xB0, 64, 0]));
        }

        byte[] conductor = Track([
            (0, 0, [0xFF, 0x51, 0x03, MicrosecondsPerQuarter >> 16, (MicrosecondsPerQuarter >> 8) & 0xFF, MicrosecondsPerQuarter & 0xFF]),
            (0, 1, [0xFF, 0x58, 0x04, 4, 2, 24, 8]),
        ]);
        byte[] piano = Track(events);
        using var file = new MemoryStream();
        file.Write("MThd"u8);
        file.Write([0, 0, 0, 6, 0, 1, 0, 2, TicksPerQuarter >> 8, TicksPerQuarter & 0xFF]);
        foreach (byte[] track in new[] { conductor, piano })
        {
            file.Write("MTrk"u8);
            file.Write([(byte)(track.Length >> 24), (byte)(track.Length >> 16), (byte)(track.Length >> 8), (byte)track.Length]);
            file.Write(track);
        }

        return file.ToArray();
    }

    private static byte[] Track(List<(long Tick, int Order, byte[] Data)> events)
    {
        using var track = new MemoryStream();
        long last = 0;
        foreach ((long tick, _, byte[] data) in events.Select((e, i) => (e, i)).OrderBy(x => x.e.Tick).ThenBy(x => x.e.Order).ThenBy(x => x.i).Select(x => x.e))
        {
            WriteVariableLength(track, tick - last);
            track.Write(data);
            last = tick;
        }

        WriteVariableLength(track, 0);
        track.Write([0xFF, 0x2F, 0x00]);
        return track.ToArray();
    }

    private static void WriteVariableLength(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[5];
        int count = 0;
        buffer[count++] = (byte)(value & 0x7F);
        while ((value >>= 7) > 0)
        {
            buffer[count++] = (byte)((value & 0x7F) | 0x80);
        }

        for (int i = count - 1; i >= 0; i--)
        {
            stream.WriteByte(buffer[i]);
        }
    }
}

/// <summary>
/// One part of a recording heard in parts: the stretch of sound sent, and the stretch inside it whose
/// notes are kept, in milliseconds from the start of the recording.
/// </summary>
public sealed record RecordingPart(int Index, long SendFromMilliseconds, long SendToMilliseconds, long KeepFromMilliseconds, long KeepToMilliseconds);