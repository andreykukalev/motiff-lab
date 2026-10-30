using System.Globalization;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>What character facts need beyond the notes and bars: the tempo map and the kind of file.</summary>
/// <param name="Tempos">Tempo changes in order, the first at tick 0, as <see cref="PlaybackScore.Tempos"/> gives them; empty means 120 BPM throughout.</param>
/// <param name="TempoAssumed">True when the file states no tempo at its start, so the first tempo is the 120 BPM default.</param>
/// <param name="FromNotation">
/// True for a notation file (MusicXML): its lengths are written note values and its velocities come
/// from dynamics marks or a default, not from playing.
/// </param>
/// <param name="TempoEstimated">True when the tempo was worked out from the notes of a transcription with its bars (decision record 0016), not read from a file.</param>
public sealed record CharacterContext(IReadOnlyList<PlaybackTempo> Tempos, bool TempoAssumed, bool FromNotation, bool TempoEstimated = false)
{
    public static CharacterContext FromPiece(ImportedPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);

        bool assumed = piece.Midi is { } midi
            ? midi.Tempos.Count == 0 || midi.Tempos[0].IsAssumed
            : piece.MusicXml!.Tempos.Count == 0 || piece.MusicXml.Tempos[0].IsAssumed;
        return new CharacterContext(PlaybackScore.FromPiece(piece).Tempos, assumed, FromNotation: piece.Midi is null, TempoEstimated: piece.Bars is not null);
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
public static partial class MotifAnalyzer
{
    /// <summary>A note sounding less than this share of the time to the next onset counts as detached.</summary>
    public const int DetachedPercent = 50;

    private const int MiddleC = 60;

    private const string NotationLengthsBasis = " A notation file gives written lengths, and staccato or tenuto marks are not read, so this reflects the written note values rather than how they are played.";

    /// <param name="notes">The notes of the piece; the selection's notes are picked by id.</param>
    /// <param name="signature">The key signature in force where the selection starts, for spelling pitches; null when the file has none.</param>
    /// <returns>The facts in display order, all in the character lens; empty when the selection has no notes.</returns>
    public static IReadOnlyList<MotifFact> AnalyzeCharacter(
        IEnumerable<Note> notes,
        MeterMap meter,
        MotifSelection selection,
        KeySignature? signature,
        CharacterContext context)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(context);

        var byId = new Dictionary<string, Note>(StringComparer.Ordinal);
        foreach (Note note in notes)
        {
            byId.TryAdd(note.Id, note);
        }

        Note[] chosen = [.. selection.NoteIds.Where(byId.ContainsKey).Select(id => byId[id])
            .OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal)];
        if (chosen.Length == 0)
        {
            return [];
        }

        IGrouping<Tick, Note>[] onsets = [.. chosen.GroupBy(note => note.Span.Onset)];
        Hit[] hits = Hits(onsets);
        MusicalKey key = ReadKey(chosen, signature).Key;
        bool offGrid = OffGridCount(chosen, meter) > 0;

        var facts = new List<MotifFact>
        {
            TempoFact(context, meter, selection.Span),
            DensityFact(chosen, hits, meter, selection.Span),
            RegisterFact(chosen, key),
            VelocityFact(chosen, context.FromNotation),
            ArticulationFact(chosen, hits, context.FromNotation),
            SyncopationShareFact(chosen, hits, meter, offGrid),
        };
        return WithHand(facts, selection);
    }

    private static MotifFact TempoFact(CharacterContext context, MeterMap meter, TickSpan span)
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

        // Each change is a place: from where it takes effect to the next change or the end of the selection.
        FactPlace[] places = [.. changes.Select((tempo, i) => new FactPlace(
            $"{Bpm(tempo.MicrosecondsPerQuarter)} BPM at {Place(meter.ToPosition(tempo.At))}",
            TickSpan.FromBounds(tempo.At, i + 1 < changes.Count ? changes[i + 1].At : span.End),
            []))];
        bool assumed = context.TempoAssumed && first == 0;
        string start = Bpm(tempos[first].MicrosecondsPerQuarter);
        string text;
        if (changes.Count == 0)
        {
            text = $"Tempo: {start} BPM throughout the motif.";
        }
        else
        {
            int[] values = [tempos[first].MicrosecondsPerQuarter, .. changes.Select(tempo => tempo.MicrosecondsPerQuarter)];
            string list = Listed([.. changes.Select(tempo => $"{Bpm(tempo.MicrosecondsPerQuarter)} BPM at {Place(meter.ToPosition(tempo.At))}")]);
            text = $"Tempo: starts at {start} BPM and changes {Times(changes.Count)}: {list}. It ranges from {Bpm(values.Max())} to {Bpm(values.Min())} BPM.";
        }

        const string Basis = "Read from the file's tempo map, in quarter notes per minute, rounded to a tenth for display.";
        MotifFact fact = context.TempoEstimated && first == 0
            ? new MotifFact("character.tempo", FactLens.Character, FactCertainty.Inferred, text, "Worked out from the notes heard in the recording, with the bars: the steady beat their onsets fit best, in quarter notes per minute, rounded to a tenth for display.")
            : assumed
            ? new MotifFact("character.tempo", FactLens.Character, FactCertainty.Inferred, text, $"The file states no tempo at its start, so the default of {Bpm(PlaybackTempo.DefaultMicrosecondsPerQuarter)} BPM is assumed. {Basis}")
            : new MotifFact("character.tempo", FactLens.Character, FactCertainty.Detected, text, Basis);
        return fact with { Places = places };
    }

    private static MotifFact DensityFact(Note[] notes, Hit[] hits, MeterMap meter, TickSpan span)
    {
        int noteCount = notes.Length;
        List<BeatSlot> beats = Beats(meter, span);
        int[] counts = [.. beats.Select(beat => hits.Count(hit => hit.At >= beat.Start && hit.At < beat.End))];
        int busiest = counts.Max();
        string text = $"Density: {Ratio(hits.Length, beats.Count)} onsets per beat ({Count(hits.Length, "onset", "onsets")} over {Count(beats.Count, "beat", "beats")}); the busiest beat holds {busiest}.";
        if (noteCount > hits.Length)
        {
            text += $" Counting every note of a chord, {Ratio(noteCount, beats.Count)} notes per beat.";
        }

        return new MotifFact(
            "character.density",
            FactLens.Character,
            FactCertainty.Detected,
            text,
            "Counted over the beats of the selection, from the bar lines; notes that start together count as one onset.")
        {
            // Every beat that holds the most onsets, with the notes that start in it.
            Places = [.. beats
                .Where((beat, i) => counts[i] == busiest)
                .Select(beat => SlotPlace($"busiest beat, {Place(meter.ToPosition(beat.Start))}", TickSpan.FromBounds(beat.Start, beat.End), notes))],
        };
    }

    private static MotifFact RegisterFact(Note[] notes, MusicalKey key)
    {
        int[] pitches = [.. notes.Select(note => note.Pitch.MidiNumber).Order()];
        int low = pitches[0];
        int high = pitches[^1];
        int median = pitches[(pitches.Length - 1) / 2];
        int above = pitches.Count(pitch => pitch >= MiddleC);

        string where = above == pitches.Length
            ? "every note is at or above middle C"
            : above == 0
                ? "every note is below middle C"
                : $"{above} of {Count(pitches.Length, "note", "notes")} are at or above middle C";
        string text = low == high
            ? $"Register: one pitch, {key.Spell(low)}; {where}."
            : $"Register: {key.Spell(low)} to {key.Spell(high)}, a range of {Count(high - low, "semitone", "semitones")}; {where}, and the middle pitch is {key.Spell(median)}.";
        return new MotifFact(
            "character.register",
            FactLens.Character,
            FactCertainty.Detected,
            text,
            $"Measured over every note of the motif; middle C is C4. The middle pitch is the median, the lower of the two middle ones for an even count. Spelled for {key.Name}.")
        {
            Places = low == high ? ExtremePlaces(notes, key) : [.. ExtremePlaces(notes, key), FactPlace.Of($"middle pitch, {key.Spell(median)}", notes.Where(note => note.Pitch.MidiNumber == median))],
        };
    }

    private static MotifFact VelocityFact(Note[] notes, bool fromNotation)
    {
        int[] velocities = [.. notes.Select(note => note.Velocity).Order()];
        string text = velocities[0] == velocities[^1]
            ? $"Velocity: every note has {velocities[0]}, on the MIDI scale of 1 to 127."
            : $"Velocity: {velocities[0]} to {velocities[^1]}, median {velocities[(velocities.Length - 1) / 2]}, on the MIDI scale of 1 to 127.";
        string basis = fromNotation
            ? $"Read from the notes. A notation file holds no played loudness: velocities come from its dynamics marks, or the default of {MusicXmlImporter.DefaultVelocity} where it has none."
            : "Read from the notes' MIDI velocities.";
        int median = velocities[(velocities.Length - 1) / 2];
        FactPlace[] places = velocities[0] == velocities[^1]
            ? []
            :
            [
                FactPlace.Of($"lowest velocity, {velocities[0]}", notes.Where(note => note.Velocity == velocities[0])),
                FactPlace.Of($"highest velocity, {velocities[^1]}", notes.Where(note => note.Velocity == velocities[^1])),
                FactPlace.Of($"median velocity, {median}", notes.Where(note => note.Velocity == median)),
            ];
        return new MotifFact("character.velocity", FactLens.Character, FactCertainty.Detected, text, basis)
        {
            Places = places,
        };
    }

    private static MotifFact ArticulationFact(Note[] notes, Hit[] hits, bool fromNotation)
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

        const string Basis = "Measured as each note's length over the time from its onset to the next onset of the motif, counting at most the whole gap; notes of the last onset have no next onset and are not counted.";
        string basis = fromNotation ? Basis + NotationLengthsBasis : Basis;
        if (percents.Count == 0)
        {
            return new MotifFact("character.articulation", FactLens.Character, FactCertainty.Detected, "Articulation: not measured, because every note starts together.", basis);
        }

        int held = percents.Count(percent => percent == 100m);
        int detached = percents.Count(percent => percent < DetachedPercent);
        string text = $"Articulation: notes sound for {Percent(percents.Sum(), percents.Count)}% of the time to the next onset on average; "
            + $"{held} of {Count(percents.Count, "note", "notes")} {(held == 1 ? "sounds" : "sound")} until the next onset, and {detached} {(detached == 1 ? "sounds" : "sound")} for less than half of it.";

        // The two sets the text counts: the notes held until the next onset, and the detached ones.
        var places = new List<FactPlace>();
        if (held > 0)
        {
            places.Add(FactPlace.Of("until the next onset", measured.Where((note, i) => percents[i] == 100m)));
        }

        if (detached > 0)
        {
            places.Add(FactPlace.Of("less than half", measured.Where((note, i) => percents[i] < DetachedPercent)));
        }

        return new MotifFact("character.articulation", FactLens.Character, FactCertainty.Detected, text, basis)
        {
            Places = places,
        };
    }

    private static MotifFact SyncopationShareFact(Note[] notes, Hit[] hits, MeterMap meter, bool offGrid)
    {
        List<FactPlace> places = Syncopations(notes, hits, meter);
        int syncopated = places.Count;
        string text = $"Syncopation share: {syncopated} of {Count(hits.Length, "onset", "onsets")} ({Percent(syncopated * 100m, hits.Length)}%).";
        const string Basis = "Onsets counted with the same rule as the syncopation fact. " + SyncopationBasis;
        MotifFact fact = offGrid
            ? new MotifFact("character.syncopation", FactLens.Character, FactCertainty.Uncertain, text, Basis + PerformanceBasis)
            : new MotifFact("character.syncopation", FactLens.Character, FactCertainty.Detected, text, Basis);
        return fact with { Places = places };
    }

    /// <summary>Quarter notes per minute, to a tenth: "96", "92.3".</summary>
    private static string Bpm(int microsecondsPerQuarter) =>
        Math.Round(60_000_000m / microsecondsPerQuarter, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>A ratio to a tenth: "2.5".</summary>
    private static string Ratio(int count, int of) =>
        Math.Round((decimal)count / of, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>A whole percentage of a total over a count.</summary>
    private static string Percent(decimal total, int count) =>
        Math.Round(total / count, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    private static string Times(int count) => count switch
    {
        1 => "once",
        2 => "twice",
        _ => $"{count} times",
    };
}