using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Midi;

/// <summary>A Standard MIDI File as read from bytes: header values and each track's events in file order.</summary>
internal sealed record SmfFile(int Format, int DeclaredTrackCount, MidiTimeBase TimeBase, IReadOnlyList<SmfTrack> Tracks);

/// <param name="EndTick">The end-of-track tick, or the last event's tick when the marker is missing.</param>
internal sealed record SmfTrack(IReadOnlyList<SmfEvent> Events, long EndTick);

/// <param name="Tick">Absolute source tick (sum of delta times in the track).</param>
/// <param name="Index">Zero-based position of the event in its track's event list.</param>
internal abstract record SmfEvent(long Tick, int Index);

/// <param name="Status">Status byte with channel, after resolving running status.</param>
internal sealed record SmfChannelEvent(long Tick, int Index, byte Status, byte Data1, byte Data2) : SmfEvent(Tick, Index)
{
    public int Kind => Status & 0xF0;

    public int Channel => Status & 0x0F;
}

internal sealed record SmfMetaEvent(long Tick, int Index, byte Type, byte[] Data) : SmfEvent(Tick, Index);

internal sealed record SmfSysExEvent(long Tick, int Index) : SmfEvent(Tick, Index);

/// <summary>
/// Reads the chunk and event structure of a Standard MIDI File. It does not interpret notes,
/// tempo or meter; <see cref="MidiImporter"/> does.
/// </summary>
/// <remarks>
/// Header problems that leave nothing to import throw <see cref="MidiImportException"/>.
/// Damage inside a track is repaired by keeping the events read before it and reported.
/// Running status is resolved here; sysex and meta events cancel it, as the SMF specification says.
/// </remarks>
internal static class SmfReader
{
    private const uint HeaderId = 0x4D546864; // "MThd"
    private const uint TrackId = 0x4D54726B; // "MTrk"
    private const uint RiffId = 0x52494646; // "RIFF"

    public static SmfFile Read(ReadOnlySpan<byte> data, ImportIssueCollector issues)
    {
        if (data.Length < 14 || ReadUInt32(data, 0) != HeaderId)
        {
            if (data.Length >= 4 && ReadUInt32(data, 0) == RiffId)
            {
                throw new MidiImportException(
                    MidiImportFailure.Unsupported,
                    "This is a RIFF MIDI (.rmi) file. Save it as a Standard MIDI File (.mid) and import that.");
            }

            throw new MidiImportException(MidiImportFailure.NotMidi, "This is not a Standard MIDI File: it does not start with an MThd header.");
        }

        uint headerLength = ReadUInt32(data, 4);
        if (headerLength < 6 || headerLength > data.Length - 8)
        {
            throw new MidiImportException(MidiImportFailure.Damaged, "The MIDI file header is damaged.");
        }

        int format = ReadUInt16(data, 8);
        int declaredTracks = ReadUInt16(data, 10);
        ushort division = ReadUInt16(data, 12);

        if (format == 2)
        {
            throw new MidiImportException(
                MidiImportFailure.Unsupported,
                "This is a type 2 MIDI file (independent patterns). Motiff Lab reads types 0 and 1; most apps can export type 1.");
        }

        if (format > 2)
        {
            throw new MidiImportException(MidiImportFailure.Damaged, $"The MIDI file header names an unknown format ({format}).");
        }

        if ((division & 0x8000) != 0)
        {
            throw new MidiImportException(
                MidiImportFailure.Unsupported,
                "This MIDI file is timed in SMPTE frames. Motiff Lab reads files timed in beats (ticks per quarter note).");
        }

        if (division == 0)
        {
            throw new MidiImportException(MidiImportFailure.Damaged, "The MIDI file header gives a time resolution of zero ticks per quarter note.");
        }

        var timeBase = MidiTimeBase.FromHeaderDivision(division);
        var tracks = new List<SmfTrack>();
        long position = 8 + headerLength;

        while (position < data.Length)
        {
            if (data.Length - position < 8)
            {
                issues.Add(ImportIssueKind.Repaired, MidiIssueCodes.TrailingBytes);
                break;
            }

            uint id = ReadUInt32(data, (int)position);
            long length = ReadUInt32(data, (int)position + 4);
            long bodyStart = position + 8;
            long available = data.Length - bodyStart;

            if (id != TrackId)
            {
                issues.Add(ImportIssueKind.Dropped, MidiIssueCodes.UnknownChunk);
                position = bodyStart + Math.Min(length, available);
                continue;
            }

            int trackIndex = tracks.Count;
            if (length > available)
            {
                issues.Add(ImportIssueKind.Repaired, MidiIssueCodes.TrackTruncated, trackIndex);
                length = available;
            }

            tracks.Add(ReadTrack(data.Slice((int)bodyStart, (int)length), trackIndex, issues));
            position = bodyStart + length;
        }

        if (tracks.Count == 0)
        {
            throw new MidiImportException(MidiImportFailure.Damaged, "The MIDI file has no tracks.");
        }

        if (tracks.Count != declaredTracks)
        {
            issues.Add(ImportIssueKind.Repaired, MidiIssueCodes.TrackCountMismatch);
        }

        if (format == 0 && tracks.Count > 1)
        {
            issues.Add(ImportIssueKind.Assumed, MidiIssueCodes.Format0WithManyTracks);
        }

        return new SmfFile(format, declaredTracks, timeBase, tracks);
    }

    private static SmfTrack ReadTrack(ReadOnlySpan<byte> body, int trackIndex, ImportIssueCollector issues)
    {
        var events = new List<SmfEvent>();
        var cursor = new Cursor(body);
        long tick = 0;
        byte? runningStatus = null;
        bool ended = false;

        try
        {
            while (!cursor.AtEnd)
            {
                tick = checked(tick + cursor.ReadVariableLength());
                byte first = cursor.Peek();

                if (first == 0xFF)
                {
                    cursor.Skip(1);
                    byte type = cursor.ReadByte();
                    int length = cursor.ReadVariableLength();
                    byte[] payload = cursor.ReadBytes(length).ToArray();
                    runningStatus = null;

                    if (type == 0x2F)
                    {
                        ended = true;
                        break;
                    }

                    events.Add(new SmfMetaEvent(tick, events.Count, type, payload));
                    continue;
                }

                if (first is 0xF0 or 0xF7)
                {
                    cursor.Skip(1);
                    cursor.ReadBytes(cursor.ReadVariableLength());
                    runningStatus = null;
                    events.Add(new SmfSysExEvent(tick, events.Count));
                    continue;
                }

                if (first > 0xF0)
                {
                    // System common and real-time messages are not valid in a file track.
                    throw new DamagedTrackException();
                }

                byte status;
                if ((first & 0x80) != 0)
                {
                    status = first;
                    cursor.Skip(1);
                    runningStatus = status;
                }
                else
                {
                    status = runningStatus ?? throw new DamagedTrackException();
                }

                byte data1 = cursor.ReadDataByte();
                byte data2 = (status & 0xF0) is 0xC0 or 0xD0 ? (byte)0 : cursor.ReadDataByte();
                events.Add(new SmfChannelEvent(tick, events.Count, status, data1, data2));
            }
        }
        catch (Exception exception) when (exception is DamagedTrackException or OverflowException)
        {
            issues.Add(ImportIssueKind.Repaired, MidiIssueCodes.TrackDamaged, trackIndex);
            return new SmfTrack(events, events.Count > 0 ? events[^1].Tick : 0);
        }

        if (!ended)
        {
            issues.Add(ImportIssueKind.Repaired, MidiIssueCodes.EndOfTrackMissing, trackIndex);
        }
        else if (!cursor.AtEnd)
        {
            issues.Add(ImportIssueKind.Dropped, MidiIssueCodes.DataAfterEndOfTrack, trackIndex);
        }

        return new SmfTrack(events, tick);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private sealed class DamagedTrackException : Exception
    {
    }

    /// <summary>Bounds-checked reader over one track body; running past the end marks the track damaged.</summary>
    private ref struct Cursor(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public readonly bool AtEnd => _position >= _data.Length;

        public readonly byte Peek() => AtEnd ? throw new DamagedTrackException() : _data[_position];

        public void Skip(int count) => ReadBytes(count);

        public byte ReadByte()
        {
            byte value = Peek();
            _position++;
            return value;
        }

        public byte ReadDataByte()
        {
            byte value = ReadByte();
            return (value & 0x80) == 0 ? value : throw new DamagedTrackException();
        }

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || count > _data.Length - _position)
            {
                throw new DamagedTrackException();
            }

            ReadOnlySpan<byte> slice = _data.Slice(_position, count);
            _position += count;
            return slice;
        }

        /// <summary>Reads a variable-length quantity of at most four bytes (28 bits).</summary>
        public int ReadVariableLength()
        {
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                byte b = ReadByte();
                value = (value << 7) | (b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    return value;
                }
            }

            throw new DamagedTrackException();
        }
    }
}