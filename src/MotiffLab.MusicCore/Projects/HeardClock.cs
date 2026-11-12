using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The clock of a recording heard (0014, 0016; version 0.5): a tick of the piece as it opens, on the bars worked out from
/// the playing, to when it was played, in microseconds from the start of the stretch of sound sent to the engine, and back.
/// With a grid, a tick falls between the two ends of its beat as the grid says the beat was played; without one, the
/// engine's own tempo map (its 120 BPM) gives the time. So a piece's place in the recording is found from its bars, the
/// sound's stretch for it is cut where it was played, and its rows say when it starts.
/// </summary>
/// <remarks>Integer arithmetic only, deterministic: the same piece gives the same times.</remarks>
public sealed class HeardClock
{
    private const int TicksPerQuarter = CanonicalTime.TicksPerQuarter;

    private readonly BarGrid? _grid;
    private readonly long _origin;
    private readonly (long At, long Units, long PerTick)[] _segments;

    private HeardClock(BarGrid? grid, long origin, IReadOnlyList<MidiTempoChange> tempos)
    {
        _grid = grid;
        _origin = origin;
        _segments = Segments(tempos);
    }

    /// <summary>The clock of a recording heard as opened (<paramref name="piece"/>, on its bars when it has some) and the engine's MIDI as read.</summary>
    /// <param name="engine">The engine's MIDI, the project's source, read again: on the recording's clock.</param>
    public static HeardClock Of(ImportedPiece piece, MidiImport engine)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(engine);
        if (piece.Bars is { } grid)
        {
            return new HeardClock(grid, OfEngine(engine).MicrosecondsAt(grid.Origin), []);
        }

        return new HeardClock(null, 0, (piece.Midi ?? engine).Tempos);
    }

    /// <summary>The engine's own clock: its MIDI's ticks to the time they were heard, by its tempo map alone.</summary>
    internal static HeardClock OfEngine(MidiImport engine) => new(null, 0, engine.Tempos);

    /// <summary>The clock of a project heard in a recording, opened as <paramref name="piece"/>; null for any other project.</summary>
    public static HeardClock? Of(Project project, ImportedPiece piece)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(piece);
        return project.Transcription is not null && PieceImporter.Import(project.FileName, project.Source).Piece?.Midi is { } engine ? Of(piece, engine) : null;
    }

    /// <summary>When a tick of the piece was played, in microseconds from the start of the stretch sent.</summary>
    public long MicrosecondsAt(Tick tick)
    {
        if (_grid is { } grid)
        {
            long beatTicks = grid.StepsPerBeat * BarGrid.StepTicks;
            long beat = tick.Value / beatTicks, within = tick.Value % beatTicks;
            long from = grid.BeatAt(beat);
            return _origin + from + (within * (grid.BeatAt(beat + 1) - from) / beatTicks);
        }

        (long at, long units, long perTick) = _segments[Last(_segments.Length, index => _segments[index].At <= tick.Value)];
        return (units + ((tick.Value - at) * perTick)) / TicksPerQuarter;
    }

    /// <summary>The last tick of the piece played at or before a time, in microseconds from the start of the stretch sent.</summary>
    public Tick TickAt(long microseconds)
    {
        if (microseconds <= MicrosecondsAt(Tick.Zero))
        {
            return Tick.Zero;
        }

        if (_grid is { } grid)
        {
            long beatTicks = grid.StepsPerBeat * BarGrid.StepTicks;
            long played = microseconds - _origin;
            long low = 0, high = 1;
            while (grid.BeatAt(high) <= played)
            {
                (low, high) = (high, high * 2);
            }

            while (high - low > 1)
            {
                long middle = low + ((high - low) / 2);
                (low, high) = grid.BeatAt(middle) <= played ? (middle, high) : (low, middle);
            }

            long from = grid.BeatAt(low);
            return new Tick((low * beatTicks) + ((played - from) * beatTicks / (grid.BeatAt(low + 1) - from)));
        }

        long target = microseconds * TicksPerQuarter;
        (long at, long units, long perTick) = _segments[Last(_segments.Length, index => _segments[index].Units <= target)];
        return new Tick(at + ((target - units) / perTick));
    }

    /// <summary>When a tick of the piece was played, in whole milliseconds from the start of the stretch sent.</summary>
    public long MillisecondsAt(Tick tick) => MicrosecondsAt(tick) / 1000;

    /// <summary>A MIDI tempo map as segments: where each starts in ticks, its time there in microseconds × 480, and how long a tick lasts in those units.</summary>
    private static (long At, long Units, long PerTick)[] Segments(IReadOnlyList<MidiTempoChange> tempos)
    {
        var segments = new List<(long At, long Units, long PerTick)> { (0, 0, MidiTempoChange.DefaultMicrosecondsPerQuarter) };
        foreach (MidiTempoChange tempo in tempos.OrderBy(static tempo => tempo.At))
        {
            (long at, long units, long perTick) = segments[^1];
            long start = units + ((tempo.At.Value - at) * perTick);
            if (tempo.At.Value == at)
            {
                segments[^1] = (at, units, tempo.MicrosecondsPerQuarter);
                continue;
            }

            segments.Add((tempo.At.Value, start, tempo.MicrosecondsPerQuarter));
        }

        return [.. segments];
    }

    private static int Last(int count, Func<int, bool> starts)
    {
        int found = 0;
        for (int index = 1; index < count && starts(index); index++)
        {
            found = index;
        }

        return found;
    }
}