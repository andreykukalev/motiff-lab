using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>What character facts need beyond the notes and bars: the tempo map and the kind of file.</summary>
/// <param name="Tempos">Tempo changes in order, the first at tick 0, as <see cref="PlaybackScore.Tempos"/> gives them; empty means 120 BPM throughout.</param>
/// <param name="TempoAssumed">True when nothing states the tempo at the start, so the first tempo is a default (the piece's 80 BPM).</param>
/// <param name="FromNotation">
/// True for a notation file (MusicXML): its lengths are written note values and its velocities come
/// from dynamics marks or a default, not from playing.
/// </param>
/// <param name="TempoEstimated">True when the tempo was worked out from the notes of a transcription with its bars (decision record 0016), not read from a file.</param>
internal sealed record CharacterContext(IReadOnlyList<PlaybackTempo> Tempos, bool TempoAssumed, bool FromNotation, bool TempoEstimated = false)
{
    public static CharacterContext FromPiece(ImportedPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);

        // The tempo at the start as played says where it comes from (version 0.5.4): the player's own is neither assumed nor estimated.
        PlaybackScore score = PlaybackScore.FromPiece(piece);
        TempoSource source = score.StartTempo.Source;
        return new CharacterContext(score.Tempos, source is TempoSource.Assumed or TempoSource.Default, FromNotation: piece.Midi is null, TempoEstimated: piece.Bars is not null && source != TempoSource.Player);
    }
}

/// <summary>
/// Character facts (S1-A5): the measurable side of musical character. Tempo and its changes, note
/// density per beat, register and range, velocity range, articulation and syncopation share.
/// </summary>
/// <remarks>
/// Every figure is measured from the notes, the bars and the tempo map; the engine uses no mood
/// words. Words such as "calm" or "urgent" come only from AI interpretation or the player.
/// Tempo is shown as quarter notes per minute, computed in decimal and rounded to a tenth for
/// display only; positions stay canonical ticks.
/// </remarks>
internal static partial class MotifAnalyzer
{
    /// <summary>A note sounding less than this share of the time to the next onset counts as detached.</summary>
    public const int DetachedPercent = 50;

    private const int MiddleC = 60;

    /// <param name="notes">The notes of the piece; the selection's notes are picked by id.</param>
    /// <param name="signature">The key signature in force where the selection starts, for spelling pitches; null when the file has none.</param>
    /// <param name="words">The language of the facts' text, bases and place labels; English by default.</param>
    /// <returns>The facts in display order, all in the character lens; empty when the selection has no notes.</returns>
    public static IReadOnlyList<MotifFact> AnalyzeCharacter(
        IEnumerable<Note> notes,
        MeterMap meter,
        MotifSelection selection,
        KeySignature? signature,
        CharacterContext context,
        Words words = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(context);

        Note[] chosen = Chosen(notes, selection);
        if (chosen.Length == 0)
        {
            return [];
        }

        Hit[] hits = Hits(chosen.GroupBy(note => note.Span.Onset));
        MusicalKey key = ReadKey(chosen, signature).Key;
        bool offGrid = OffGridCount(chosen, meter) > 0;

        var facts = new List<MotifFact>
        {
            TempoFact(MeasureTempo(context, meter, selection.Span), context, words),
            DensityFact(MeasureDensity(chosen, hits, meter, selection.Span), chosen, words),
            RegisterFact(MeasureRegister(chosen), chosen, key, words),
            VelocityFact(MeasureVelocity(chosen), chosen, context.FromNotation, words),
            ArticulationFact(MeasureArticulation(chosen, hits), context.FromNotation, words),
            SyncopationShareFact(Syncopations(chosen, hits, meter), hits.Length, offGrid, words),
        };
        if (MeasureTexture(chosen, selection) is { } texture)
        {
            facts.Insert(0, TextureFact(texture, words));
        }

        return WithHand(facts, selection, words);
    }

    /// <summary>The tempo where the selection starts, whether that is the map's first, and each change inside the selection.</summary>
    private sealed record TempoMeasure(PlaybackTempo Start, bool AtFirst, IReadOnlyList<TempoChange> Changes);

    /// <summary>A tempo change: where it is, and its stretch, to the next change or the end of the selection.</summary>
    private sealed record TempoChange(PlaybackTempo Tempo, MetricPosition At, TickSpan Span);

    /// <summary>Notes and onsets against the selection's beats, and the beats that hold the most onsets.</summary>
    private sealed record DensityMeasure(int Notes, int Onsets, int Beats, int Busiest, IReadOnlyList<(MetricPosition At, TickSpan Span)> BusiestBeats);

    /// <summary>The lowest, highest and median (the lower middle) pitch, and how many of the notes are at or above middle C.</summary>
    private sealed record RegisterMeasure(int Low, int High, int Median, int Above, int Count);

    /// <summary>The lowest, highest and median (the lower middle) velocity.</summary>
    private sealed record VelocityMeasure(int Low, int High, int Median);

    /// <summary>
    /// Each measured note's length as a percentage of the time to the next onset (notes of the last onset have none), with
    /// how many sound until the next onset and how many for less than <see cref="DetachedPercent"/>.
    /// </summary>
    private sealed record ArticulationMeasure(IReadOnlyList<decimal> Percents, IReadOnlyList<Note> Measured, int Held, int Detached);

    private static TempoMeasure MeasureTempo(CharacterContext context, MeterMap meter, TickSpan span)
    {
        PlaybackTempo[] tempos = context.Tempos.Count == 0
            ? [new PlaybackTempo(Tick.Zero, PlaybackTempo.DefaultMicrosecondsPerQuarter)]
            : [.. context.Tempos];

        int first = Array.FindLastIndex(tempos, tempo => tempo.At <= span.Onset);
        first = Math.Max(first, 0);
        var changes = new List<PlaybackTempo>();
        int current = tempos[first].MicrosecondsPerQuarter;
        foreach (PlaybackTempo tempo in tempos.Skip(first + 1).Where(tempo => tempo.At < span.End))
        {
            if (tempo.MicrosecondsPerQuarter != current)
            {
                changes.Add(tempo);
                current = tempo.MicrosecondsPerQuarter;
            }
        }

        // Each change takes effect from where it is to the next change or the end of the selection.
        TempoChange[] stretches = [.. changes.Select((tempo, i) => new TempoChange(
            tempo,
            meter.ToPosition(tempo.At),
            TickSpan.FromBounds(tempo.At, i + 1 < changes.Count ? changes[i + 1].At : span.End)))];
        return new TempoMeasure(tempos[first], first == 0, stretches);
    }

    private static DensityMeasure MeasureDensity(Note[] notes, Hit[] hits, MeterMap meter, TickSpan span)
    {
        List<BeatSlot> beats = Beats(meter, span);
        int[] counts = [.. beats.Select(beat => hits.Count(hit => hit.At >= beat.Start && hit.At < beat.End))];
        int busiest = counts.Max();
        return new DensityMeasure(
            notes.Length,
            hits.Length,
            beats.Count,
            busiest,
            [.. beats.Where((beat, i) => counts[i] == busiest).Select(beat => (meter.ToPosition(beat.Start), TickSpan.FromBounds(beat.Start, beat.End)))]);
    }

    private static RegisterMeasure MeasureRegister(Note[] notes)
    {
        int[] pitches = [.. notes.Select(note => note.Pitch.MidiNumber).Order()];
        return new RegisterMeasure(pitches[0], pitches[^1], pitches[(pitches.Length - 1) / 2], pitches.Count(pitch => pitch >= MiddleC), pitches.Length);
    }

    private static VelocityMeasure MeasureVelocity(Note[] notes)
    {
        int[] velocities = [.. notes.Select(note => note.Velocity).Order()];
        return new VelocityMeasure(velocities[0], velocities[^1], velocities[(velocities.Length - 1) / 2]);
    }

    private static ArticulationMeasure MeasureArticulation(Note[] notes, Hit[] hits)
    {
        // Each note against the time from its onset to the next onset of the motif; the last onset has none.
        var percents = new List<decimal>();
        var measured = new List<Note>();
        foreach (Note note in notes)
        {
            int next = Array.FindIndex(hits, hit => hit.At > note.Span.Onset);
            if (next >= 0)
            {
                long gap = hits[next].At.Value - note.Span.Onset.Value;
                percents.Add(Math.Min(note.Span.Duration, gap) * 100m / gap);
                measured.Add(note);
            }
        }

        return new ArticulationMeasure(percents, measured, percents.Count(percent => percent == 100m), percents.Count(percent => percent < DetachedPercent));
    }
}