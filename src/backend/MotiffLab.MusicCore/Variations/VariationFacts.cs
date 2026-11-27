using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Variations;

/// <summary>
/// What every operation and every chain of steps shares: the motif's notes, the diff, the
/// kept-property checks (S1-E9) and the explanation, which states only facts read from the diff (S1-E4).
/// </summary>
internal static partial class VariationFacts
{
    /// <summary>The selected notes, in the selection's order.</summary>
    public static IReadOnlyList<Note> MotifNotes(IEnumerable<Note> notes, MotifSelection selection)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(selection);
        Dictionary<string, Note> byId = notes.ToDictionary(static note => note.Id, StringComparer.Ordinal);
        return selection.NoteIds.Select(id => byId[id]).ToArray();
    }

    /// <summary>
    /// Builds the variation from the notes before and after: the diff by note id, ordered notes,
    /// checks of the kept properties against <paramref name="before"/>, and the explanation.
    /// </summary>
    /// <param name="key">The key the step counted scale steps in, or null; intervals then also count as kept when their sizes in scale steps are (the Keep table's footnote 2).</param>
    /// <param name="meter">The meter the explanation names beats in; null reads <see cref="MeterMap.Default"/>.</param>
    /// <param name="words">The language the explanation is written in; English by default.</param>
    public static Variation Build(
        string operation,
        TickSpan span,
        IReadOnlyList<Note> before,
        IEnumerable<Note> after,
        IReadOnlySet<KeepProperty> keep,
        IReadOnlyList<string> flags,
        MusicalKey? key = null,
        MeterMap? meter = null,
        Words words = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(keep);
        ArgumentNullException.ThrowIfNull(flags);
        Note[] ordered = Ordered(after);
        Dictionary<string, Note> afterById = ordered.ToDictionary(static note => note.Id, StringComparer.Ordinal);
        var beforeIds = before.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        NoteChange[] changes = before
            .Where(note => afterById.ContainsKey(note.Id))
            .Select(note => new NoteChange(note, afterById[note.Id]))
            .ToArray();
        Note[] added = ordered.Where(note => !beforeIds.Contains(note.Id)).ToArray();
        Note[] removed = before.Where(note => !afterById.ContainsKey(note.Id)).ToArray();
        Note[] source = [.. before];
        var textures = (Before: MotifTexture.Read(source), After: MotifTexture.Read(ordered));
        Note[][]? copies = Copies(source, ordered, span);
        bool melodyPitchClasses = KeepTable.Qualifies(operation.Split(VariationChain.StepSeparator), KeepProperty.Melody, KeepTable.MelodyPitchClasses);
        KeepCheck[] properties = Enum.GetValues<KeepProperty>()
            .Select(property => new KeepCheck(property, Check(property, source, ordered, copies, span, key, melodyPitchClasses, textures.Before, textures.After, meter ?? MeterMap.Default)))
            .ToArray();
        KeepCheck[] checks = properties.Where(check => keep.Contains(check.Property)).ToArray();
        string explanation = Explain(Measure(changes, added.Length, removed.Length, source, ordered, textures.Before, textures.After), Sequence(source, copies, span, key), words);
        if (OperationTemplates.Lead(operation, changes, textures.Before, textures.After, key, meter ?? MeterMap.Default, words) is { } lead)
        {
            explanation = $"{lead} {explanation}";
        }

        bool sameMelody = Pitches(textures.Before.Melody).SequenceEqual(Pitches(textures.After.Melody));
        string unchanged = Unchanged(properties, textures.Before, sameMelody, words);
        if (changes.Length + added.Length + removed.Length > 0 && unchanged.Length > 0)
        {
            explanation = $"{explanation} {unchanged}";
        }

        return new Variation(operation, ordered, changes, added, removed, checks, properties, flags, explanation);
    }

    /// <summary>
    /// How far apart copies of these notes sit when a step repeats them: the span, or the smallest
    /// whole number of spans that holds every note to its end.
    /// </summary>
    public static long Period(IReadOnlyList<Note> notes, TickSpan span)
    {
        ArgumentNullException.ThrowIfNull(notes);
        long extent = notes.Count == 0 ? 0 : notes.Max(static note => note.Span.End.Value) - span.Onset.Value;
        return Math.Max(1, (extent + span.Duration - 1) / span.Duration) * span.Duration;
    }

    /// <summary>
    /// The result cut into back-to-back windows of the source's <see cref="Period"/>, when it fills
    /// two or more of them in a row from the span's start, as a sequence does; null otherwise.
    /// </summary>
    private static Note[][]? Copies(Note[] before, Note[] after, TickSpan span)
    {
        long period = Period(before, span);
        long start = span.Onset.Value;
        if (after.Length == 0 || after.Any(note => note.Span.Onset.Value < start))
        {
            return null;
        }

        IGrouping<long, Note>[] windows = [.. after.GroupBy(note => (note.Span.Onset.Value - start) / period).OrderBy(static window => window.Key)];
        return windows.Length < 2 || windows.Where((window, i) => window.Key != i).Any()
            ? null
            : [.. windows.Select(static window => window.ToArray())];
    }

    private static Note[] Ordered(IEnumerable<Note> notes) => notes
        .OrderBy(static note => note.Span.Onset)
        .ThenBy(static note => note.Pitch.MidiNumber)
        .ThenBy(static note => note.Id, StringComparer.Ordinal)
        .ToArray();

    /// <remarks>
    /// Pitches, pitch order and register read every note. Contour and intervals read the melody, as
    /// the melody facts do (S1-A2), so the notes of a chord under it are not melodic steps. Melody
    /// and bass are kept when their lines keep their pitches in order; their rhythm may change
    /// (the Keep table's footnote 1). Contour, intervals and rhythm are also kept when the result is
    /// copies of the motif, one after the other (<see cref="Copies"/>), and each copy keeps them, as
    /// in a sequence; the moves between copies are not the motif's own. With a key, intervals are
    /// also kept when their sizes in scale steps are, so a diatonic copy keeps them (footnote 2).
    /// When <paramref name="melodyPitchClasses"/> is set, a step was V3, and the melody is also kept
    /// when its pitch classes stay in order and only octaves change (footnote 3).
    /// </remarks>
    private static KeepStatus Check(
        KeepProperty property,
        Note[] before,
        Note[] after,
        Note[][]? copies,
        TickSpan span,
        MusicalKey? key,
        bool melodyPitchClasses,
        MotifTexture textureBefore,
        MotifTexture textureAfter,
        MeterMap meter)
    {
        if (property is KeepProperty.Harmony)
        {
            return SameHarmony(before, after, span, key, meter);
        }

        int[] pitchesBefore = before.Select(static note => note.Pitch.MidiNumber).ToArray();
        int[] pitchesAfter = after.Select(static note => note.Pitch.MidiNumber).ToArray();
        int[] melodyBefore = Pitches(textureBefore.Melody);
        int[] melodyAfter = Pitches(textureAfter.Melody);
        bool kept = property switch
        {
            KeepProperty.Pitches => pitchesBefore.Order().SequenceEqual(pitchesAfter.Order()),
            KeepProperty.PitchOrder => pitchesBefore.SequenceEqual(pitchesAfter),
            KeepProperty.Contour => Each(melodyAfter, after, copies, melody => SameContour(melodyBefore, melody)),
            KeepProperty.Intervals => Each(melodyAfter, after, copies, melody => SameIntervals(melodyBefore, melody, key)),
            KeepProperty.Melody => melodyBefore.SequenceEqual(melodyAfter)
                || (melodyPitchClasses && melodyBefore.Select(PitchClass).SequenceEqual(melodyAfter.Select(PitchClass))),
            KeepProperty.Bass => Pitches(textureBefore.Bass).SequenceEqual(Pitches(textureAfter.Bass)),
            KeepProperty.Rhythm => Rhythm(before, span.Onset.Value).SequenceEqual(Rhythm(after, span.Onset.Value))
                || (copies is not null && copies.Select((copy, i) => Rhythm(before, span.Onset.Value).Order().SequenceEqual(Rhythm(copy, span.Onset.Value + (i * Period(before, span))).Order())).All(static same => same)),
            KeepProperty.Register => pitchesBefore.Length == pitchesAfter.Length
                && (pitchesBefore.Length == 0 || (pitchesBefore.Min() == pitchesAfter.Min() && pitchesBefore.Max() == pitchesAfter.Max())),
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, "Unknown property."),
        };
        return kept ? KeepStatus.Kept : KeepStatus.NoLongerKept;
    }

    /// <summary>
    /// Harmony is kept when the chords read from the notes (<see cref="HarmonyAnalyzer.FindChords"/>) are the same at
    /// every place a chord starts, before or after; not checked when the source has no chords, as on a single line.
    /// </summary>
    private static KeepStatus SameHarmony(Note[] before, Note[] after, TickSpan span, MusicalKey? key, MeterMap meter)
    {
        IReadOnlyList<ChordSpan> was = HarmonyAnalyzer.FindChords(before, meter, span, key);
        if (was.Count == 0)
        {
            return KeepStatus.NotChecked;
        }

        IReadOnlyList<ChordSpan> now = HarmonyAnalyzer.FindChords(after, meter, span, key);
        static ChordLabel? At(IReadOnlyList<ChordSpan> chords, Tick at) => chords.FirstOrDefault(chord => chord.Span.Contains(at))?.Chord;
        bool same = was.Concat(now).Select(static chord => chord.Span.Onset).All(at => At(was, at) == At(now, at));
        return same ? KeepStatus.Kept : KeepStatus.NoLongerKept;
    }

    /// <summary>Whether the whole result's melody, or else the melody of every copy, passes <paramref name="same"/>.</summary>
    private static bool Each(int[] melodyAfter, Note[] after, Note[][]? copies, Func<int[], bool> same) =>
        same(melodyAfter) || (after.Length > 0 && copies is not null && copies.All(copy => same(Pitches(MotifTexture.Read(copy).Melody))));

    private static bool SameContour(int[] before, int[] after) => Steps(before).Select(Math.Sign).SequenceEqual(Steps(after).Select(Math.Sign));

    private static bool SameIntervals(int[] before, int[] after, MusicalKey? key)
    {
        if (Steps(before).SequenceEqual(Steps(after)))
        {
            return true;
        }

        if (key is not { } scale || before.Length != after.Length)
        {
            return false;
        }

        ScalePosition[] from = [.. before.Select(pitch => ScaleSteps.Locate(scale, pitch))];
        ScalePosition[] to = [.. after.Select(pitch => ScaleSteps.Locate(scale, pitch))];
        return from.Select(static position => position.Alteration).SequenceEqual(to.Select(static position => position.Alteration))
            && from.Zip(from.Skip(1), static (a, b) => b.Step - a.Step).SequenceEqual(to.Zip(to.Skip(1), static (a, b) => b.Step - a.Step));
    }

    private static int PitchClass(int pitch) => pitch % 12;

    private static int[] Pitches(IReadOnlyList<Note> line) => line.Select(static note => note.Pitch.MidiNumber).ToArray();

    private static IEnumerable<int> Steps(int[] pitches) => pitches.Zip(pitches.Skip(1), static (a, b) => b - a);

    private static IEnumerable<(long Offset, long Duration)> Rhythm(Note[] notes, long start) =>
        notes.Select(note => (note.Span.Onset.Value - start, note.Span.Duration));

    /// <summary>What the explanation is written from (<c>VariationFacts.Explanation.cs</c>): the diff's counts, lengths, onsets and the melody's steps.</summary>
    /// <param name="Changed">Notes kept by id, changed or not.</param>
    /// <param name="MelodySteps">The melody's intervals in semitones before and after, when it keeps its notes by id and its intervals changed; null otherwise.</param>
    /// <param name="Doubled">Every kept note lasts twice as long.</param>
    /// <param name="Halved">Every kept note lasts half as long.</param>
    /// <param name="Moved">Kept notes that start at a new place.</param>
    /// <param name="Shift">The one distance in ticks every kept note moved by, when they all moved by the same; null otherwise.</param>
    /// <param name="GapsDouble">The time between the kept notes' onsets doubles.</param>
    /// <param name="GapsHalve">The time between the kept notes' onsets halves.</param>
    /// <param name="ExtentBefore">Ticks from the first onset to the last note's end, before.</param>
    private sealed record DiffMeasures(
        int Changed,
        int Added,
        int Removed,
        int PitchChanges,
        (int[] From, int[] To)? MelodySteps,
        int LengthChanges,
        bool Doubled,
        bool Halved,
        int Louder,
        int Softer,
        int Moved,
        long? Shift,
        bool GapsDouble,
        bool GapsHalve,
        long ExtentBefore,
        long ExtentAfter);

    /// <summary>How the step moved a copy of the motif from the one before: by semitones, or, when <paramref name="InScale"/>, by scale steps of the key.</summary>
    /// <param name="Repeats">Copies after the first, which is the motif as it was.</param>
    private sealed record CopyMove(int Repeats, int Distance, bool InScale);

    private static DiffMeasures Measure(IReadOnlyList<NoteChange> changes, int added, int removed, Note[] before, Note[] after, MotifTexture textureBefore, MotifTexture textureAfter)
    {
        long[] shifts = [.. changes.Select(static change => change.After.Span.Onset.Value - change.Before.Span.Onset.Value)];
        long[] gapsBefore = Gaps(changes.Select(static change => change.Before));
        long[] gapsAfter = Gaps(changes.Select(static change => change.After));
        return new DiffMeasures(
            changes.Count,
            added,
            removed,
            changes.Count(static change => change.PitchChanged),
            MelodySteps(textureBefore, textureAfter),
            changes.Count(static change => change.DurationChanged),
            changes.All(static change => change.After.Span.Duration == change.Before.Span.Duration * 2),
            changes.All(static change => change.After.Span.Duration * 2 == change.Before.Span.Duration),
            changes.Count(static change => change.After.Velocity > change.Before.Velocity),
            changes.Count(static change => change.After.Velocity < change.Before.Velocity),
            shifts.Count(static shift => shift != 0),
            shifts.Distinct().Count() == 1 ? shifts[0] : null,
            gapsBefore.Length > 0 && gapsBefore.Zip(gapsAfter).All(static gap => gap.Second == gap.First * 2),
            gapsBefore.Length > 0 && gapsBefore.Zip(gapsAfter).All(static gap => gap.Second * 2 == gap.First),
            Extent(before),
            Extent(after));
    }

    /// <summary>The melody's intervals in semitones before and after, when the melody has the same notes by id before and after and its intervals changed; null otherwise.</summary>
    private static (int[] From, int[] To)? MelodySteps(MotifTexture before, MotifTexture after)
    {
        if (before.Melody.Count < 2
            || !before.Melody.Select(static note => note.Id).SequenceEqual(after.Melody.Select(static note => note.Id), StringComparer.Ordinal))
        {
            return null;
        }

        int[] from = [.. Steps(Pitches(before.Melody))];
        int[] to = [.. Steps(Pitches(after.Melody))];
        return from.SequenceEqual(to) ? null : (from, to);
    }

    /// <summary>
    /// How the copies move, when the first copy is the motif as it was and every later copy has its rhythm and moves
    /// every note the same distance again, in semitones or else in scale steps; null otherwise.
    /// </summary>
    private static CopyMove? Sequence(Note[] before, Note[][]? copies, TickSpan span, MusicalKey? key)
    {
        if (copies is null)
        {
            return null;
        }

        long period = Period(before, span);
        Note[] source = Shape(before, span.Onset.Value);
        Note[][] shaped = [.. copies.Select((copy, i) => Shape(copy, span.Onset.Value + (i * period)))];
        if (!shaped[0].Select(Sound).SequenceEqual(source.Select(Sound))
            || shaped.Skip(1).Any(copy => !Rhythm(copy, 0).SequenceEqual(Rhythm(source, 0))))
        {
            return null;
        }

        int semitones = shaped[1][0].Pitch.MidiNumber - source[0].Pitch.MidiNumber;
        if (Moves(shaped, source, (pitch, copy) => pitch + (copy * semitones)))
        {
            return new CopyMove(shaped.Length - 1, semitones, false);
        }

        if (key is not { } scale)
        {
            return null;
        }

        int steps = ScaleSteps.Locate(scale, shaped[1][0].Pitch.MidiNumber).Step - ScaleSteps.Locate(scale, source[0].Pitch.MidiNumber).Step;
        return steps != 0 && Moves(shaped, source, (pitch, copy) => ScaleSteps.Move(scale, pitch, copy * steps))
            ? new CopyMove(shaped.Length - 1, steps, true)
            : null;
    }

    /// <summary>Whether every later copy has the source's pitches moved as <paramref name="moved"/> says, given a pitch and the copy's number.</summary>
    private static bool Moves(Note[][] copies, Note[] source, Func<int, int, int> moved) =>
        copies.Skip(1).Select((copy, i) => copy.Length == source.Length
            && copy.Zip(source).All(pair => pair.First.Pitch.MidiNumber == moved(pair.Second.Pitch.MidiNumber, i + 1))).All(static same => same);

    /// <summary>Notes moved to start from <paramref name="start"/>, ordered by onset, then pitch.</summary>
    private static Note[] Shape(Note[] notes, long start) => [.. notes
        .Select(note => note with { Span = new TickSpan(new Tick(note.Span.Onset.Value - start), note.Span.Duration) })
        .OrderBy(static note => note.Span.Onset)
        .ThenBy(static note => note.Pitch.MidiNumber)];

    private static (TickSpan Span, Pitch Pitch) Sound(Note note) => (note.Span, note.Pitch);

    private static long[] Gaps(IEnumerable<Note> notes)
    {
        long[] onsets = notes.Select(static note => note.Span.Onset.Value).ToArray();
        return onsets.Zip(onsets.Skip(1), static (a, b) => b - a).ToArray();
    }

    private static long Extent(Note[] notes) =>
        notes.Length == 0 ? 0 : notes.Max(static note => note.Span.End.Value) - notes.Min(static note => note.Span.Onset.Value);
}