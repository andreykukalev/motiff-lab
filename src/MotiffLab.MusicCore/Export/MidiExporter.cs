using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Export;

/// <summary>
/// Writes notes as a Standard MIDI File for a DAW (S1-X1): type 1 at 480 ticks per quarter, so the
/// canonical timeline needs no rounding; a conductor track with the tempo map and time signatures;
/// then one track per hand when every note has one, otherwise one per source track or part.
/// </summary>
/// <remarks>
/// The export starts at the bar line of the bar that holds <c>from</c>, so a motif from bar 17 lands
/// in bar 1 of the file with its beats where they were; a pickup bar is padded to a full bar. Tempo
/// and meter in effect there move with it. When a note starts while the same key on the same track is
/// still sounding, the earlier note ends there, as a key must be released before it is struck again.
/// Exporting is pure and deterministic: the same input gives the same bytes.
/// </remarks>
public static class MidiExporter
{
    /// <summary>Ticks per quarter in the file: the canonical resolution.</summary>
    public const int TicksPerQuarter = CanonicalTime.TicksPerQuarter;

    private const int DrumChannel = 9;

    /// <summary>The file's bytes.</summary>
    /// <param name="notes">The notes to export, for example a motif's or a variation's.</param>
    /// <param name="tempos">The piece's tempo map; empty means 120 BPM.</param>
    /// <param name="meter">The piece's meter.</param>
    /// <param name="title">The conductor track's name, for example the project's.</param>
    /// <param name="from">Where the export starts; it is moved back to its bar line. Null starts at the piece's first bar.</param>
    /// <param name="trackName">The name of a source track or part, by its zero-based index, when the notes are split by track; null names it "Track n".</param>
    public static byte[] Export(
        IReadOnlyList<Note> notes,
        IReadOnlyList<PlaybackTempo> tempos,
        MeterMap meter,
        string title,
        Tick? from = null,
        Func<int, string?>? trackName = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(tempos);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(title);
        if (notes.Count == 0)
        {
            throw new ArgumentException("There are no notes to export.", nameof(notes));
        }

        Bar first = meter.BarAt(from ?? Tick.Zero);
        long origin = first.Span.Onset.Value - first.LeadIn;
        if (notes.Any(note => note.Span.Onset.Value < origin))
        {
            throw new ArgumentException("A note starts before the bar the export starts at.", nameof(notes));
        }

        long Shift(Tick at) => at.Value - origin;

        var tracks = new List<byte[]> { Conductor(title, tempos, meter, first, Shift) };
        foreach ((string name, Note[] part) in Parts(notes, trackName))
        {
            int channel = tracks.Count - 1;
            channel = (channel >= DrumChannel ? channel + 1 : channel) % 16;
            tracks.Add(NoteTrack(name, part, channel, Shift));
        }

        using var file = new MemoryStream();
        file.Write("MThd"u8);
        WriteBigEndian(file, 6, 4);
        WriteBigEndian(file, 1, 2);
        WriteBigEndian(file, tracks.Count, 2);
        WriteBigEndian(file, TicksPerQuarter, 2);
        foreach (byte[] track in tracks)
        {
            file.Write("MTrk"u8);
            WriteBigEndian(file, track.Length, 4);
            file.Write(track);
        }

        return file.ToArray();
    }

    /// <summary>
    /// "Für Elise – bars 1–2 – Syncopate.mid": the project, the motif and the variation, each left out
    /// when null or blank, with characters that file systems refuse replaced.
    /// </summary>
    public static string FileName(string project, string? motif = null, string? variation = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        string name = string.Join(" – ", new[] { project, motif, variation }.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => part!.Trim()));
        var safe = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            safe.Append(c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c) ? '_' : c);
        }

        string stem = safe.ToString().Trim().TrimEnd('.');
        return $"{(stem.Length == 0 ? "Motiff Lab" : stem)}.mid";
    }

    /// <summary>The notes split into tracks: by hand when every note has one (right, then left), otherwise by source track.</summary>
    private static IEnumerable<(string Name, Note[] Notes)> Parts(IReadOnlyList<Note> notes, Func<int, string?>? trackName)
    {
        if (notes.All(static note => note.Hand is not null))
        {
            foreach (Hand hand in new[] { Hand.Right, Hand.Left })
            {
                Note[] played = [.. notes.Where(note => note.Hand!.Value == hand)];
                if (played.Length > 0)
                {
                    yield return (hand == Hand.Right ? "Right hand" : "Left hand", played);
                }
            }

            yield break;
        }

        foreach (IGrouping<int, Note> track in notes.GroupBy(static note => note.Track).OrderBy(static track => track.Key))
        {
            yield return (trackName?.Invoke(track.Key) ?? $"Track {track.Key + 1}", [.. track]);
        }
    }

    private static byte[] Conductor(string title, IReadOnlyList<PlaybackTempo> tempos, MeterMap meter, Bar first, Func<Tick, long> shift)
    {
        var events = new List<(long Tick, int Order, byte[] Data)> { (0, 0, Meta(0x03, Encoding.UTF8.GetBytes(title))) };

        // The meter in effect at the first bar, then every later change.
        TimeSignature start = first.Signature;
        events.Add((0, 1, TimeSignatureEvent(start)));
        foreach (MeterChange change in meter.Changes.Where(change => change.Position > first.Span.Onset))
        {
            events.Add((shift(change.Position), 1, TimeSignatureEvent(change.Signature)));
        }

        PlaybackTempo[] map = tempos.Count == 0 ? [new PlaybackTempo(Tick.Zero, PlaybackTempo.DefaultMicrosecondsPerQuarter)] : [.. tempos.OrderBy(static tempo => tempo.At)];
        PlaybackTempo initial = map.LastOrDefault(tempo => tempo.At <= first.Span.Onset, map[0]);
        events.Add((0, 2, TempoEvent(initial.MicrosecondsPerQuarter)));
        foreach (PlaybackTempo tempo in map.Where(tempo => tempo.At > first.Span.Onset))
        {
            events.Add((shift(tempo.At), 2, TempoEvent(tempo.MicrosecondsPerQuarter)));
        }

        return Track(events);
    }

    private static byte[] NoteTrack(string name, Note[] notes, int channel, Func<Tick, long> shift)
    {
        var events = new List<(long Tick, int Order, byte[] Data)> { (0, 0, Meta(0x03, Encoding.UTF8.GetBytes(name))) };
        foreach (IGrouping<int, Note> key in notes.GroupBy(static note => note.Pitch.MidiNumber))
        {
            Note[] strikes = [.. key.OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Id, StringComparer.Ordinal)];
            for (int i = 0; i < strikes.Length; i++)
            {
                Note note = strikes[i];
                long onset = shift(note.Span.Onset);
                long end = shift(note.Span.End);
                if (i + 1 < strikes.Length)
                {
                    end = Math.Min(end, shift(strikes[i + 1].Span.Onset));
                }

                if (end <= onset)
                {
                    continue;
                }

                byte velocity = (byte)Math.Clamp(note.Velocity, 1, 127);
                events.Add((onset, 2, [(byte)(0x90 | channel), (byte)key.Key, velocity]));
                events.Add((end, 1, [(byte)(0x80 | channel), (byte)key.Key, 64]));
            }
        }

        return Track(events);
    }

    /// <summary>The track's bytes: events by tick, then by order (note-offs before note-ons at the same tick), then as added; then end of track.</summary>
    private static byte[] Track(List<(long Tick, int Order, byte[] Data)> events)
    {
        using var track = new MemoryStream();
        long last = 0;
        foreach ((long tick, _, byte[] data) in events.Select(static (e, i) => (e, i)).OrderBy(static x => x.e.Tick).ThenBy(static x => x.e.Order).ThenBy(static x => x.i).Select(static x => x.e))
        {
            WriteVariableLength(track, tick - last);
            track.Write(data);
            last = tick;
        }

        WriteVariableLength(track, 0);
        track.Write(Meta(0x2F, []));
        return track.ToArray();
    }

    private static byte[] TimeSignatureEvent(TimeSignature signature) =>
        Meta(0x58, [(byte)signature.Numerator, (byte)int.Log2(signature.Denominator), 24, 8]);

    private static byte[] TempoEvent(int microsecondsPerQuarter) =>
        Meta(0x51, [(byte)(microsecondsPerQuarter >> 16), (byte)(microsecondsPerQuarter >> 8), (byte)microsecondsPerQuarter]);

    private static byte[] Meta(byte type, byte[] data)
    {
        using var meta = new MemoryStream();
        meta.WriteByte(0xFF);
        meta.WriteByte(type);
        WriteVariableLength(meta, data.Length);
        meta.Write(data);
        return meta.ToArray();
    }

    private static void WriteVariableLength(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[10];
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

    private static void WriteBigEndian(Stream stream, int value, int bytes)
    {
        for (int i = bytes - 1; i >= 0; i--)
        {
            stream.WriteByte((byte)(value >> (8 * i)));
        }
    }
}