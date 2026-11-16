using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assembly;

/// <summary>
/// Where a place in a recording's notes is in the recording itself (decision record 0024, scenario 2): the notes were
/// written in bars of their own, so each note's place in them is paired with the time it was heard at, and a place between
/// notes lies in between. The page plays the recording's own sound from here and draws its stretch on the waveform.
/// </summary>
public sealed class RecordingClock
{
    private readonly long[] _ticks;
    private readonly long[] _milliseconds;

    private RecordingClock(long[] ticks, long[] milliseconds)
    {
        _ticks = ticks;
        _milliseconds = milliseconds;
    }

    /// <summary>The places paired with times, in ticks, ascending, from 0 to the music's end.</summary>
    public IReadOnlyList<long> Ticks => _ticks;

    /// <summary>The times of <see cref="Ticks"/> in the recording, in milliseconds, never decreasing.</summary>
    public IReadOnlyList<long> Milliseconds => _milliseconds;

    /// <summary>The clock of a recording's notes as <paramref name="music"/> writes them; null for a version not heard in a recording, or without notes.</summary>
    public static RecordingClock? Of(SavedPerformance version, SourceMusic music)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(music);
        if (version.Source != PerformanceSource.Recording)
        {
            return null;
        }

        Dictionary<string, long> heard = PlayedNotes.FromMidi(version.Midi, version.RecordingStart)
            .ToDictionary(note => note.Id, note => note.Onset / 1000, StringComparer.Ordinal);
        List<(long Tick, long Milliseconds)> pairs = [];
        foreach (var note in music.Notes)
        {
            if (heard.TryGetValue(note.Id, out long at))
            {
                pairs.Add((note.Span.Onset.Value, at));
            }
        }

        if (pairs.Count == 0)
        {
            return null;
        }

        // One pair per place, the earliest time heard there; a time earlier than one before it is left out, so the clock never runs back.
        List<(long Tick, long Milliseconds)> kept = [];
        foreach (var group in pairs.GroupBy(pair => pair.Tick).OrderBy(group => group.Key))
        {
            long time = group.Min(pair => pair.Milliseconds);
            if (kept.Count == 0 || time > kept[^1].Milliseconds)
            {
                kept.Add((group.Key, time));
            }
        }

        // Before the first note and after the last, at the music's tempo there, or as the last two notes go.
        if (kept[0].Tick > 0)
        {
            long before = Math.Max(0, kept[0].Milliseconds - MillisecondsOf(kept[0].Tick, music.TempoAt(new Tick(kept[0].Tick))));
            kept.Insert(0, (0, Math.Min(before, kept[0].Milliseconds)));
        }

        long end = Math.Max(music.End.Value, kept[^1].Tick + 1);
        if (kept[^1].Tick < end)
        {
            long ticks = end - kept[^1].Tick;
            long more = kept.Count > 1 && kept[^1].Tick > kept[^2].Tick
                ? (kept[^1].Milliseconds - kept[^2].Milliseconds) * ticks / (kept[^1].Tick - kept[^2].Tick)
                : MillisecondsOf(ticks, music.TempoAt(new Tick(kept[^1].Tick)));
            kept.Add((end, kept[^1].Milliseconds + Math.Max(1, more)));
        }

        return new RecordingClock([.. kept.Select(pair => pair.Tick)], [.. kept.Select(pair => pair.Milliseconds)]);
    }

    /// <summary>The time in the recording of a place in its notes, in milliseconds.</summary>
    public long MillisecondsAt(Tick at)
    {
        long tick = Math.Clamp(at.Value, _ticks[0], _ticks[^1]);
        int index = Segment(Array.BinarySearch(_ticks, tick));
        if (index >= _ticks.Length - 1)
        {
            return _milliseconds[^1];
        }

        long span = _ticks[index + 1] - _ticks[index];
        return _milliseconds[index] + ((_milliseconds[index + 1] - _milliseconds[index]) * (tick - _ticks[index]) / span);
    }

    /// <summary>The place in the notes heard at a time of the recording.</summary>
    public Tick TickAt(long milliseconds)
    {
        long time = Math.Clamp(milliseconds, _milliseconds[0], _milliseconds[^1]);
        int index = Segment(Array.BinarySearch(_milliseconds, time));
        if (index >= _milliseconds.Length - 1)
        {
            return new Tick(_ticks[^1]);
        }

        long span = _milliseconds[index + 1] - _milliseconds[index];
        return new Tick(_ticks[index] + (span == 0 ? 0 : (_ticks[index + 1] - _ticks[index]) * (time - _milliseconds[index]) / span));
    }

    /// <summary>The pair at or before a value, from what a binary search found.</summary>
    private static int Segment(int found) => Math.Max(0, found >= 0 ? found : ~found - 1);

    /// <summary>How long <paramref name="ticks"/> last at a tempo, in milliseconds.</summary>
    private static long MillisecondsOf(long ticks, int quartersPerMinute) => ticks * 60_000 / (Math.Max(1, quartersPerMinute) * CanonicalTime.TicksPerQuarter);
}