using System.Text;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Midi;

/// <summary>
/// Imports a Standard MIDI File (type 0 or 1, timed in ticks per quarter note) onto the canonical
/// timeline (S1-I1 to S1-I4, S1-I8). Decision record 0004 describes the policies.
/// </summary>
/// <remarks>
/// <para>
/// Notes are paired per track, channel and key, first on first off, so a note-on that arrives at
/// the same tick before the previous note's note-off does not create a zero-length note.
/// Times convert through <see cref="MidiTimeBase"/> and round only in <see cref="CanonicalTime"/>,
/// onset and end separately.
/// </para>
/// <para>
/// Notes map to the shared <see cref="Note"/> as imported facts. A note whose end had to be
/// assumed (no note-off) is marked inferred, with confidence <see cref="AssumedEndConfidence"/>,
/// because its length is a guess.
/// </para>
/// <para>
/// When all the notes are on one piano track, their hands are inferred by pitch and continuity
/// (<see cref="HandInference"/>, S1-I4) and the report says so. Other files get no hands.
/// </para>
/// <para>
/// The import is pure and deterministic: the same bytes give the same result and report.
/// </para>
/// </remarks>
public static class MidiImporter
{
    /// <summary>The Stage 1 file size limit (S1-I1).</summary>
    public const int MaxFileBytes = 50 * 1024 * 1024;

    /// <summary>Confidence of a note whose end was assumed: the note is real, its length is a guess.</summary>
    public const double AssumedEndConfidence = 0.5;

    private const int DrumChannel = 9;
    private const int LastPianoProgram = 7; // General MIDI programs 0–7 are pianos.
    private const int SustainPedalController = 64;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    public static MidiImport Import(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileBytes)
        {
            throw new MidiImportException(MidiImportFailure.TooLarge, "This file is larger than 50 MB, the most Motiff Lab can import.");
        }

        var issues = new ImportIssueCollector();
        SmfFile file = SmfReader.Read(data, issues);
        MidiTimeBase timeBase = file.TimeBase;
        long fileEndTick = file.Tracks.Max(track => track.EndTick);

        var notes = new List<MidiNote>();
        var pedal = new List<MidiPedal>();
        var trackInfos = new List<MidiTrackInfo>();
        var tempoEvents = new List<SourceValue<int>>();
        var meterEvents = new List<SourceValue<(int Numerator, int Denominator)>>();
        var keyEvents = new List<SourceValue<(int Sharps, bool IsMinor)>>();

        for (int trackIndex = 0; trackIndex < file.Tracks.Count; trackIndex++)
        {
            var reader = new TrackReader(trackIndex, timeBase, fileEndTick, issues);
            reader.Read(file.Tracks[trackIndex], tempoEvents, meterEvents, keyEvents);

            if (trackIndex > 0 && file.Format == 1)
            {
                issues.Add(ImportIssueKind.Assumed, MidiIssueCodes.TempoOutsideFirstTrack, trackIndex, reader.TempoCount);
            }

            notes.AddRange(reader.Notes);
            pedal.AddRange(reader.Pedal);
            trackInfos.Add(reader.ToInfo());
        }

        notes.Sort(static (a, b) =>
        {
            int order = a.Span.Onset.CompareTo(b.Span.Onset);
            if (order == 0)
            {
                order = a.Track.CompareTo(b.Track);
            }

            if (order == 0)
            {
                order = a.Pitch.CompareTo(b.Pitch);
            }

            return order != 0 ? order : a.NoteOnIndex.CompareTo(b.NoteOnIndex);
        });

        issues.Add(
            ImportIssueKind.Rounded,
            MidiIssueCodes.NotesRounded,
            count: notes.Count(note => !note.IsExact && note.EndSource != MidiNoteEnd.Widened));

        var tempos = Merge(tempoEvents, timeBase, issues, MidiIssueCodes.TempoSuperseded)
            .Select(change => new MidiTempoChange(change.At, change.MidiTick, change.Value, IsAssumed: false))
            .ToList();
        if (tempos.Count == 0 || tempos[0].At != Tick.Zero)
        {
            issues.Add(ImportIssueKind.Assumed, MidiIssueCodes.TempoDefault);
            tempos.Insert(0, new MidiTempoChange(Tick.Zero, 0, MidiTempoChange.DefaultMicrosecondsPerQuarter, IsAssumed: true));
        }

        var meters = Merge(meterEvents, timeBase, issues, MidiIssueCodes.MeterSuperseded)
            .Select(change => new MidiTimeSignature(change.At, change.MidiTick, change.Value.Numerator, change.Value.Denominator, IsAssumed: false))
            .ToList();
        if (meters.Count == 0 || meters[0].At != Tick.Zero)
        {
            issues.Add(ImportIssueKind.Assumed, MidiIssueCodes.MeterDefault);
            meters.Insert(0, new MidiTimeSignature(Tick.Zero, 0, 4, 4, IsAssumed: true));
        }

        var keys = Merge(keyEvents, timeBase, issues, MidiIssueCodes.KeySuperseded)
            .Select(change => new MidiKeySignature(change.At, change.MidiTick, change.Value.Sharps, change.Value.IsMinor))
            .ToList();

        Tick end = timeBase.ToCanonical(fileEndTick).Tick;
        if (notes.Count > 0)
        {
            Tick lastNoteEnd = notes.Max(note => note.Span.End);
            end = lastNoteEnd > end ? lastNoteEnd : end;
        }

        var meter = new MeterMap(meters.Select(change => new MeterChange(change.At, new TimeSignature(change.Numerator, change.Denominator))));
        IReadOnlyList<Note> events = notes.Select(ToNote).ToList();
        if (IsSinglePianoTrack(trackInfos) && HandInference.Applies(events.ToList()))
        {
            events = HandInference.Infer(events);
            issues.Add(ImportIssueKind.Assumed, MidiIssueCodes.HandsInferred, events[0].Track, events.Count);
        }

        var report = new ImportReport(
            Format: $"MIDI type {file.Format}",
            TrackCount: file.Tracks.Count,
            NoteCount: notes.Count,
            MeasureCount: meter.BarsBefore(end).Count(),
            Issues: issues.Build(MidiIssueCodes.Describe));

        return new MidiImport(file.Format, timeBase, trackInfos, events, notes, tempos, meters, meter, keys, end, report)
        {
            SustainPedal = [.. pedal.OrderBy(static held => held.Span.Onset).ThenBy(static held => held.Track).ThenBy(static held => held.Channel)],
        };
    }

    /// <summary>True when one track holds all the notes and none of its channels is set to an instrument other than piano.</summary>
    private static bool IsSinglePianoTrack(List<MidiTrackInfo> tracks)
    {
        var withNotes = tracks.Where(static track => track.NoteCount > 0).ToList();
        return withNotes.Count == 1 && withNotes[0].Programs.All(static program => program.Program <= LastPianoProgram);
    }

    private static Note ToNote(MidiNote source, int index) => new()
    {
        Id = $"n_{index + 1:D4}",
        Pitch = new Pitch(source.Pitch),
        Span = source.Span,
        Velocity = source.Velocity,
        Track = source.Track,
        Attribution = source.EndSource == MidiNoteEnd.Assumed ? Attribution.Inferred(AssumedEndConfidence) : Attribution.Imported,
        Source = new MidiSourceLocation(source.Track, source.Channel, source.NoteOnIndex),
    };

    /// <summary>
    /// Orders meta values by position (ties keep file order: earlier tracks, then earlier events),
    /// rounds them to canonical ticks and keeps the last value at each canonical position.
    /// </summary>
    private static List<SourceValue<T>> Merge<T>(
        List<SourceValue<T>> values,
        MidiTimeBase timeBase,
        ImportIssueCollector issues,
        string supersededCode)
    {
        var ordered = values
            .OrderBy(value => value.MidiTick)
            .ThenBy(value => value.Track)
            .ThenBy(value => value.Index)
            .ToList();

        var merged = new List<SourceValue<T>>();
        foreach (SourceValue<T> value in ordered)
        {
            ConvertedTick converted = timeBase.ToCanonical(value.MidiTick);
            if (!converted.IsExact)
            {
                issues.Add(ImportIssueKind.Rounded, MidiIssueCodes.MetaRounded);
            }

            SourceValue<T> placed = value with { At = converted.Tick };
            if (merged.Count > 0 && merged[^1].At == placed.At)
            {
                issues.Add(ImportIssueKind.Dropped, supersededCode);
                merged[^1] = placed;
            }
            else
            {
                merged.Add(placed);
            }
        }

        return merged;
    }

    private static string? DecodeText(byte[] bytes)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.Latin1.GetString(bytes);
        }

        text = text.Trim('\0', ' ', '\t', '\r', '\n');
        return text.Length == 0 ? null : text;
    }

    private sealed record SourceValue<T>(int Track, int Index, long MidiTick, T Value)
    {
        public Tick At { get; init; }
    }

    private readonly record struct OpenNote(int Index, long Tick, int Velocity);

    /// <summary>Interprets one track's events: pairs notes, collects meta values, counts what is left out.</summary>
    private sealed class TrackReader(int track, MidiTimeBase timeBase, long fileEndTick, ImportIssueCollector issues)
    {
        private readonly Dictionary<(int Channel, int Pitch), Queue<OpenNote>> _open = [];
        private readonly SortedSet<int> _channels = [];
        private readonly SortedDictionary<int, int> _programs = [];
        private readonly Dictionary<int, long> _pedalDown = [];
        private string? _name;
        private string? _instrument;

        public List<MidiNote> Notes { get; } = [];

        /// <summary>Where the sustain pedal was held down, by channel.</summary>
        public List<MidiPedal> Pedal { get; } = [];

        public int TempoCount { get; private set; }

        public void Read(
            SmfTrack source,
            List<SourceValue<int>> tempos,
            List<SourceValue<(int Numerator, int Denominator)>> meters,
            List<SourceValue<(int Sharps, bool IsMinor)>> keys)
        {
            foreach (SmfEvent e in source.Events)
            {
                switch (e)
                {
                    case SmfChannelEvent channelEvent:
                        ReadChannelEvent(channelEvent);
                        break;
                    case SmfMetaEvent meta:
                        ReadMeta(meta, tempos, meters, keys);
                        break;
                    case SmfSysExEvent:
                        issues.Add(ImportIssueKind.Dropped, MidiIssueCodes.SysExIgnored, track);
                        break;
                }
            }

            CloseOpenNotes();
            foreach ((int channel, long down) in _pedalDown.OrderBy(static held => held.Key))
            {
                // A pedal never let go is held to the end of the piece.
                AddPedal(channel, down, Math.Max(fileEndTick, down + 1));
            }
        }

        public MidiTrackInfo ToInfo() => new(
            track,
            _name,
            _instrument,
            [.. _channels],
            Notes.Count,
            [.. _programs.Select(entry => new MidiChannelProgram(entry.Key, entry.Value))]);

        private void ReadChannelEvent(SmfChannelEvent e)
        {
            switch (e.Kind)
            {
                case 0x90 when e.Data2 > 0:
                    NoteOn(e);
                    break;
                case 0x90:
                    NoteOff(e, MidiNoteEnd.NoteOnVelocityZero);
                    break;
                case 0x80:
                    NoteOff(e, MidiNoteEnd.NoteOff);
                    break;
                case 0xB0 when e.Data1 == SustainPedalController:
                    ReadPedal(e);
                    break;
                case 0xB0:
                    issues.Add(ImportIssueKind.Dropped, MidiIssueCodes.ControllersIgnored, track);
                    break;
                case 0xC0:
                    _programs.TryAdd(e.Channel, e.Data1);
                    break;
                case 0xE0:
                    issues.Add(ImportIssueKind.Dropped, MidiIssueCodes.PitchBendIgnored, track);
                    break;
                case 0xA0 or 0xD0:
                    issues.Add(ImportIssueKind.Dropped, MidiIssueCodes.AftertouchIgnored, track);
                    break;
            }
        }

        private void NoteOn(SmfChannelEvent e)
        {
            if (e.Channel == DrumChannel)
            {
                issues.Add(ImportIssueKind.Dropped, MidiIssueCodes.DrumsExcluded, track);
                return;
            }

            var key = (e.Channel, (int)e.Data1);
            if (!_open.TryGetValue(key, out Queue<OpenNote>? queue))
            {
                queue = new Queue<OpenNote>();
                _open[key] = queue;
            }

            if (queue.Count > 0)
            {
                issues.Add(ImportIssueKind.Assumed, MidiIssueCodes.SamePitchOverlap, track);
            }

            queue.Enqueue(new OpenNote(e.Index, e.Tick, e.Data2));
        }

        private void NoteOff(SmfChannelEvent e, MidiNoteEnd endSource)
        {
            if (e.Channel == DrumChannel)
            {
                return;
            }

            if (!_open.TryGetValue((e.Channel, e.Data1), out Queue<OpenNote>? queue) || queue.Count == 0)
            {
                issues.Add(ImportIssueKind.Dropped, MidiIssueCodes.NoteOffUnmatched, track);
                return;
            }

            AddNote(e.Channel, e.Data1, queue.Dequeue(), e.Tick, endSource);
        }

        /// <summary>The sustain pedal: down from a value of 64, up below it, as General MIDI reads it.</summary>
        private void ReadPedal(SmfChannelEvent e)
        {
            bool down = e.Data2 >= 64;
            if (down)
            {
                _pedalDown.TryAdd(e.Channel, e.Tick);
            }
            else if (_pedalDown.Remove(e.Channel, out long from))
            {
                AddPedal(e.Channel, from, e.Tick);
            }
        }

        private void AddPedal(int channel, long down, long up)
        {
            if (up <= down)
            {
                return;
            }

            Tick onset = timeBase.ToCanonical(down).Tick;
            Tick end = timeBase.ToCanonical(up).Tick;
            if (end > onset)
            {
                Pedal.Add(new MidiPedal(track, channel, down, up, TickSpan.FromBounds(onset, end)));
            }
        }

        private void CloseOpenNotes()
        {
            foreach (var ((channel, pitch), queue) in _open)
            {
                while (queue.TryDequeue(out OpenNote open))
                {
                    issues.Add(ImportIssueKind.Assumed, MidiIssueCodes.NoteOffMissing, track);
                    long end = fileEndTick > open.Tick ? fileEndTick : open.Tick + timeBase.TicksPerQuarter;
                    AddNote(channel, pitch, open, end, MidiNoteEnd.Assumed);
                }
            }
        }

        private void AddNote(int channel, int pitch, OpenNote open, long offTick, MidiNoteEnd endSource)
        {
            TickSpan span;
            bool exact;

            if (offTick == open.Tick)
            {
                issues.Add(ImportIssueKind.Repaired, MidiIssueCodes.ZeroLengthWidened, track);
                span = new TickSpan(timeBase.ToCanonical(open.Tick).Tick, 1);
                exact = false;
                endSource = MidiNoteEnd.Widened;
            }
            else
            {
                ConvertedSpan converted = timeBase.ToCanonicalSpan(open.Tick, offTick);
                span = converted.Span;
                exact = converted.IsExact;
            }

            _channels.Add(channel);
            Notes.Add(new MidiNote(track, channel, open.Index, pitch, open.Velocity, open.Tick, offTick, span, exact, endSource));
        }

        private void ReadMeta(
            SmfMetaEvent e,
            List<SourceValue<int>> tempos,
            List<SourceValue<(int Numerator, int Denominator)>> meters,
            List<SourceValue<(int Sharps, bool IsMinor)>> keys)
        {
            byte[] d = e.Data;
            switch (e.Type)
            {
                case 0x03 when _name is null:
                    _name = DecodeText(d);
                    break;
                case 0x04 when _instrument is null:
                    _instrument = DecodeText(d);
                    break;
                case 0x51:
                    int microseconds = d.Length == 3 ? (d[0] << 16) | (d[1] << 8) | d[2] : 0;
                    if (microseconds == 0)
                    {
                        issues.Add(ImportIssueKind.Dropped, MidiIssueCodes.MetaInvalid, track);
                        break;
                    }

                    TempoCount++;
                    tempos.Add(new SourceValue<int>(track, e.Index, e.Tick, microseconds));
                    break;
                case 0x58:
                    if (d.Length < 4 || d[0] == 0 || d[1] > 7)
                    {
                        issues.Add(ImportIssueKind.Dropped, MidiIssueCodes.MetaInvalid, track);
                        break;
                    }

                    meters.Add(new SourceValue<(int, int)>(track, e.Index, e.Tick, (d[0], 1 << d[1])));
                    break;
                case 0x59:
                    int sharps = d.Length == 2 ? (sbyte)d[0] : int.MaxValue;
                    if (sharps is < -7 or > 7 || d[1] > 1)
                    {
                        issues.Add(ImportIssueKind.Dropped, MidiIssueCodes.MetaInvalid, track);
                        break;
                    }

                    keys.Add(new SourceValue<(int, bool)>(track, e.Index, e.Tick, (sharps, d[1] == 1)));
                    break;
                default:
                    issues.Add(ImportIssueKind.Dropped, MidiIssueCodes.MetaIgnored, track);
                    break;
            }
        }
    }
}