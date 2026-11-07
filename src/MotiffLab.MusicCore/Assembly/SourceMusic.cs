using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assembly;

/// <summary>
/// The music of one source of an assembly in its own time (decision record 0024, section 1): its notes, its own bars
/// and bar numbers, its tempo and its key signatures. The player's notes are music of the same shape.
/// </summary>
public sealed record SourceMusic(IReadOnlyList<Note> Notes, IReadOnlyList<AssemblyBar> Bars, IReadOnlyList<PlaybackTempo> Tempos, IReadOnlyList<KeySignature> Keys)
{
    /// <summary>Where the last bar ends.</summary>
    public Tick End => Bars.Count == 0 ? Tick.Zero : Bars[^1].Span.End;

    /// <summary>The bars as a meter, for engraving and playback.</summary>
    public MeterMap Meter => AssemblyBars.ToMeter(Bars);

    /// <summary>Music from notes and a meter that runs at least to <paramref name="end"/>.</summary>
    public static SourceMusic Of(IReadOnlyList<Note> notes, MeterMap meter, Tick end, IReadOnlyList<PlaybackTempo> tempos, IReadOnlyList<KeySignature> keys)
    {
        ArgumentNullException.ThrowIfNull(notes);
        Tick last = notes.Count == 0 ? end : new Tick(Math.Max(end.Value, notes.Max(note => note.Span.End.Value)));
        IReadOnlyList<Note> ordered = [.. notes.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal)];
        return new SourceMusic(ordered, AssemblyBars.FromMeter(meter, last), tempos, keys);
    }

    /// <summary>The music of a piece with these notes (its own, or as the player corrected them).</summary>
    public static SourceMusic OfPiece(ImportedPiece piece, IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(piece);
        PlaybackScore playback = PlaybackScore.FromPiece(piece);
        return Of(notes, piece.Meter, piece.End, playback.Tempos, piece.Keys);
    }

    /// <summary>
    /// The music of another version as its own file has it: its bars, its bar numbers, its tempo. A recording's notes, which
    /// the engine wrote at 120 a minute in 4/4, are written in the bars worked out from them, with a tempo that follows the
    /// playing (decision record 0016), in the key that fits them, and marked heard. Null when its notes no longer read.
    /// </summary>
    public static SourceMusic? OfVersion(SavedPerformance version)
    {
        ArgumentNullException.ThrowIfNull(version);
        PieceImportResult imported = PieceImporter.Import($"{version.Id}.mid", version.Midi);
        if (imported.Piece is not { } piece)
        {
            return null;
        }

        if (version.Source != PerformanceSource.Recording || piece.Midi is null)
        {
            return OfPiece(piece, piece.Notes);
        }

        ImportedPiece heard = Transcribed.Mark(BarGrid.Find(piece.Midi) is { } grid ? grid.Apply(piece) : piece);
        SourceMusic music = OfPiece(heard, heard.Notes);
        return heard.Keys.Count == 0 && Transcribed.KeyOf(heard.Notes) is { } key
            ? music with { Keys = [new KeySignature(Tick.Zero, key.Fifths, key.Mode)] }
            : music;
    }

    /// <summary>The bar that holds a position: its number, counted from 1 (0 for a pickup), and the bar.</summary>
    public (int Number, AssemblyBar Bar) BarAt(Tick position)
    {
        if (Bars.Count == 0)
        {
            return (1, new AssemblyBar(new TickSpan(Tick.Zero, TimeSignature.CommonTime.BarTicks), TimeSignature.CommonTime));
        }

        int index = 0;
        for (int i = 0; i < Bars.Count; i++)
        {
            if (Bars[i].Span.Onset <= position)
            {
                index = i;
            }
        }

        return (NumberOf(index), Bars[index]);
    }

    /// <summary>The number of the bar at an index: from 1, or from 0 when the first bar is a pickup.</summary>
    public int NumberOf(int index) => HasPickup ? index : index + 1;

    /// <summary>Whether the first bar is a pickup, numbered 0.</summary>
    public bool HasPickup => Bars.Count > 1 && Bars[0].IsShort && !Bars[1].IsShort;

    /// <summary>The beat of a position within its bar, from 1, and the offset past it in ticks.</summary>
    public (int Bar, int Beat, long Offset) PositionOf(Tick position)
    {
        (int number, AssemblyBar bar) = BarAt(position);
        long inBar = position.Value - bar.Span.Onset.Value;
        if (number == 0)
        {
            inBar += bar.Signature.BarTicks - bar.Span.Duration;
        }

        long beat = bar.Signature.BeatTicks;
        return (number, (int)(inBar / beat) + 1, inBar % beat);
    }

    /// <summary>The tempo at a position, in quarters a minute.</summary>
    public int TempoAt(Tick position)
    {
        int perQuarter = PlaybackTempo.DefaultMicrosecondsPerQuarter;
        foreach (PlaybackTempo tempo in Tempos.OrderBy(tempo => tempo.At))
        {
            if (tempo.At > position)
            {
                break;
            }

            perQuarter = tempo.MicrosecondsPerQuarter;
        }

        return (int)Math.Round(60_000_000.0 / perQuarter);
    }

    /// <summary>The key the notes of <c>[start, end)</c> sound in, with a bar of context on either side; null without notes.</summary>
    public MusicalKey? KeyAround(Tick start, Tick end)
    {
        long context = Bars.Count > 0 ? Bars[0].Signature.BarTicks * 2 : CanonicalTime.TicksPerQuarter * 8;
        Tick from = new(Math.Max(0, start.Value - context));
        Tick to = end.Offset(context);
        Note[] notes = [.. Notes.Where(note => note.Span.Onset >= from && note.Span.Onset < to)];
        return Transcribed.KeyOf(notes);
    }

    /// <summary>The notes that start in <c>[start, end)</c>.</summary>
    public IReadOnlyList<Note> NotesIn(Tick start, Tick end) =>
        [.. Notes.Where(note => note.Span.Onset >= start && note.Span.Onset < end)];
}