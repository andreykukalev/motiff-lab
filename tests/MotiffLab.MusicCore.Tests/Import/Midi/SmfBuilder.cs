namespace MotiffLab.MusicCore.Tests.Import.Midi;

/// <summary>Writes small Standard MIDI Files byte by byte for tests. Events take delta times, as in the file.</summary>
internal sealed class SmfBuilder(int format, int ppq)
{
    private readonly List<byte[]> _chunks = [];

    public int? DeclaredTrackCount { get; set; }

    public SmfBuilder Track(Action<TrackWriter> write, bool endOfTrack = true)
    {
        var writer = new TrackWriter();
        write(writer);
        if (endOfTrack)
        {
            writer.Meta(0, 0x2F);
        }

        return Chunk("MTrk", [.. writer.Bytes]);
    }

    public SmfBuilder Chunk(string id, byte[] body)
    {
        _chunks.Add([.. System.Text.Encoding.ASCII.GetBytes(id), .. UInt32(body.Length), .. body]);
        return this;
    }

    public byte[] Build()
    {
        int trackCount = DeclaredTrackCount ?? _chunks.Count;
        byte[] header = [.. "MThd"u8, .. UInt32(6), .. UInt16(format), .. UInt16(trackCount), .. UInt16(ppq)];
        return [.. header, .. _chunks.SelectMany(chunk => chunk)];
    }

    private static byte[] UInt32(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static byte[] UInt16(int value) => [(byte)(value >> 8), (byte)value];

    internal sealed class TrackWriter
    {
        public List<byte> Bytes { get; } = [];

        public TrackWriter Delta(int delta)
        {
            var groups = new Stack<byte>();
            groups.Push((byte)(delta & 0x7F));
            while ((delta >>= 7) > 0)
            {
                groups.Push((byte)((delta & 0x7F) | 0x80));
            }

            Bytes.AddRange(groups);
            return this;
        }

        /// <summary>Writes raw bytes after a delta, for running status or damaged data.</summary>
        public TrackWriter Raw(int delta, params byte[] bytes)
        {
            Delta(delta);
            Bytes.AddRange(bytes);
            return this;
        }

        public TrackWriter On(int delta, int pitch, int velocity = 80, int channel = 0) =>
            Raw(delta, (byte)(0x90 | channel), (byte)pitch, (byte)velocity);

        public TrackWriter Off(int delta, int pitch, int channel = 0) =>
            Raw(delta, (byte)(0x80 | channel), (byte)pitch, 64);

        public TrackWriter Control(int delta, int controller, int value, int channel = 0) =>
            Raw(delta, (byte)(0xB0 | channel), (byte)controller, (byte)value);

        public TrackWriter Program(int delta, int program, int channel = 0) =>
            Raw(delta, (byte)(0xC0 | channel), (byte)program);

        public TrackWriter Meta(int delta, byte type, params byte[] data)
        {
            Raw(delta, 0xFF, type);
            Delta(data.Length);
            Bytes.AddRange(data);
            return this;
        }

        public TrackWriter Tempo(int delta, int microsecondsPerQuarter) =>
            Meta(delta, 0x51, (byte)(microsecondsPerQuarter >> 16), (byte)(microsecondsPerQuarter >> 8), (byte)microsecondsPerQuarter);

        public TrackWriter TimeSignature(int delta, int numerator, int denominatorPower) =>
            Meta(delta, 0x58, (byte)numerator, (byte)denominatorPower, 24, 8);

        public TrackWriter Key(int delta, int sharps, bool minor) =>
            Meta(delta, 0x59, (byte)(sbyte)sharps, (byte)(minor ? 1 : 0));

        public TrackWriter Name(int delta, string name) =>
            Meta(delta, 0x03, System.Text.Encoding.UTF8.GetBytes(name));
    }
}