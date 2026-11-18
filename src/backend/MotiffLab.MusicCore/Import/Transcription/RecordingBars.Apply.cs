using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.Transcription;

public static partial class RecordingBars
{
    /// <summary>
    /// The bars of the notes heard, found as <paramref name="search"/> asks (<see cref="Find"/>), and the piece on them
    /// (<see cref="Apply"/>, or as the engine wrote it when the notes follow no beat), marked transcribed.
    /// </summary>
    /// <param name="heard">The piece imported from the engine's MIDI.</param>
    /// <param name="search">How to follow a live performance (<see cref="FindLive"/>); null finds the beat once.</param>
    /// <exception cref="ArgumentException">The piece was not imported from MIDI, which is what transcription writes.</exception>
    public static (BarGrid? Bars, ImportedPiece Piece) Heard(ImportedPiece heard, GridSearch? search = null)
    {
        ArgumentNullException.ThrowIfNull(heard);
        MidiImport midi = heard.Midi ?? throw new ArgumentException("A transcription is read as MIDI.", nameof(heard));
        BarGrid? bars = Find(midi, search: search);
        return (bars, Transcribed.Mark(bars is { } grid ? grid.Apply(heard) : heard));
    }

    /// <summary>
    /// The piece with its notes, bars, tempo and keys on the grid: the same notes with the same ids,
    /// placed against the beat that was found rather than the engine's 120 BPM.
    /// </summary>
    /// <param name="grid">The grid found for the recording.</param>
    /// <param name="piece">The piece imported from the engine's MIDI.</param>
    /// <exception cref="ArgumentException">The piece was not imported from MIDI, which is what transcription writes.</exception>
    public static ImportedPiece Apply(this BarGrid grid, ImportedPiece piece)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(piece);
        MidiImport midi = piece.Midi ?? throw new ArgumentException("A transcription is read as MIDI.", nameof(piece));

        var clock = new Clock(midi.Tempos);
        long originUnits = clock.Units(grid.Origin);
        QuarterFraction At(Tick tick)
        {
            long units = clock.Units(tick) - originUnits;
            if (units <= 0)
            {
                return QuarterFraction.Zero;
            }

            long index = grid.BeatIndexAt(units / Clock.UnitsPerMicrosecond);
            long from = grid.BeatAt(index) * Clock.UnitsPerMicrosecond;
            long length = (grid.BeatAt(index + 1) * Clock.UnitsPerMicrosecond) - from;
            return new QuarterFraction(grid.StepsPerBeat * ((index * length) + (units - from)), 4 * length);
        }

        Tick Moved(Tick tick) => CanonicalTime.ToTick(At(tick)).Tick;
        Note Placed(Note note)
        {
            QuarterFraction onset = At(note.Span.Onset);
            QuarterFraction end = At(note.Span.End);
            TickSpan span = end > onset ? CanonicalTime.ToSpan(onset, end).Span : new TickSpan(CanonicalTime.ToTick(onset).Tick, 1);
            return note with { Span = span };
        }

        Note[] notes = [.. piece.Notes.Select(Placed)];
        Note[] midiNotes = [.. midi.Notes.Select(Placed)];
        Tick end = Moved(midi.End);
        foreach (Note note in notes)
        {
            end = note.Span.End > end ? note.Span.End : end;
        }

        MeterChange[] changes = [new MeterChange(Tick.Zero, grid.SignatureAt(grid.BarStarts[0])), .. grid.BarStarts.Skip(1).Select(start => new MeterChange(new Tick(start * BarGrid.StepTicks), grid.SignatureAt(start)))];
        var meter = new MeterMap(changes, grid.BarStarts[0] * BarGrid.StepTicks);

        List<MidiTempoChange> tempos = Tempos(grid, end);

        MidiKeySignature[] keys = [.. midi.KeySignatures.Select(key => key with { At = Moved(key.At) })];
        MidiPedal[] pedal = [.. midi.SustainPedal
            .Select(held => (Held: held, Onset: Moved(held.Span.Onset), End: Moved(held.Span.End)))
            .Where(moved => moved.End > moved.Onset)
            .Select(moved => moved.Held with { Span = TickSpan.FromBounds(moved.Onset, moved.End) })];
        var import = midi with
        {
            Notes = midiNotes,
            Tempos = tempos,
            TimeSignatures = [.. changes.Select(change => new MidiTimeSignature(change.Position, change.Position.Value, change.Signature.Numerator, change.Signature.Denominator, IsAssumed: true))],
            Meter = meter,
            KeySignatures = keys,
            End = end,
            SustainPedal = pedal,
        };

        // The engine's 120 BPM and 4/4 are no longer what the bars say, so their notices go.
        ImportIssue[] issues =
        [
            .. piece.Report.Issues.Where(issue => issue.Code is not (MidiIssueCodes.TempoDefault or MidiIssueCodes.MeterDefault)),
            new ImportIssue(ImportIssueKind.Assumed, BarGrid.IssueCode, null, 1, grid.Describe()) { RussianMessage = grid.Describe(Words.Russian) },
        ];
        return piece with
        {
            Notes = notes,
            Meter = meter,
            Keys = [.. piece.Keys.Select(key => key with { At = Moved(key.At) })],
            End = end,
            Report = piece.Report with { MeasureCount = meter.BarsBefore(end).Count(), Issues = issues },
            Midi = import,
            Bars = grid,
        };
    }

    /// <summary>A tempo change on every beat to <paramref name="end"/>, so the notes sound when they were played; one for a steady beat.</summary>
    private static List<MidiTempoChange> Tempos(BarGrid grid, Tick end)
    {
        var tempos = new List<MidiTempoChange>();
        long beatTicks = grid.StepsPerBeat * BarGrid.StepTicks;
        for (long index = 0; index * beatTicks <= end.Value; index++)
        {
            int perQuarter = (int)Math.Clamp((grid.BeatAt(index + 1) - grid.BeatAt(index)) * 4 / grid.StepsPerBeat, 1, int.MaxValue);
            if (tempos.Count == 0 || tempos[^1].MicrosecondsPerQuarter != perQuarter)
            {
                tempos.Add(new MidiTempoChange(new Tick(index * beatTicks), index * beatTicks, perQuarter, IsAssumed: true) { IsInferred = true });
            }

            if (!grid.FollowsPlaying)
            {
                break;
            }
        }

        return tempos;
    }
}