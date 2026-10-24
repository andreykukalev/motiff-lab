using System.Text.RegularExpressions;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>
/// Every sentence of every explanation, for every operation and parameter set, is checked against the
/// diff (S1-E4, #34). The audit recomputes each stated count, length, beat and interval from the
/// variation's notes, independently of the explanation code; a sentence it does not know fails.
/// </summary>
public partial class ExplanationAuditTests
{
    private const long Quarter = 480;
    private const long Eighth = 240;

    private static readonly MusicalKey s_cMajor = new(0, KeyMode.Major);
    private static readonly MeterMap s_threeFour = new([new MeterChange(Tick.Zero, new TimeSignature(3, 4))]);

    private static Note Note(string id, int pitch, long onset, long duration, Hand? hand = null) => new()
    {
        Id = id,
        Pitch = new Pitch(pitch),
        Span = new TickSpan(new Tick(onset), duration),
        Velocity = 80,
        Track = 0,
        Hand = hand is { } value ? new Attributed<Hand>(value, Attribution.Imported) : null,
        Attribution = Attribution.Imported,
    };

    private static Note[] Line(long length, params int[] pitches) =>
        [.. pitches.Select((pitch, i) => Note($"n{i + 1}", pitch, i * length, length))];

    /// <summary>The motifs the audit runs on, each with its meter.</summary>
    private static IEnumerable<(string Name, Note[] Notes, MeterMap Meter)> Motifs()
    {
        // One bar of 4/4 quarters: C4 D4 E4 G4.
        yield return ("quarters", Line(Quarter, 60, 62, 64, 67), MeterMap.Default);

        // One bar of 4/4 eighths: E5 D5 C5 D5 E5 E5 E5 G5.
        yield return ("eighths", Line(Eighth, 76, 74, 72, 74, 76, 76, 76, 79), MeterMap.Default);

        // Two bars of 3/4: a melody over a held bass in the left hand.
        yield return ("melody and bass", [
            Note("m1", 72, 0, Quarter, Hand.Right),
            Note("m2", 76, Quarter, Quarter, Hand.Right),
            Note("m3", 79, 2 * Quarter, Quarter, Hand.Right),
            Note("m4", 77, 3 * Quarter, Eighth, Hand.Right),
            Note("m5", 76, (3 * Quarter) + Eighth, Eighth, Hand.Right),
            Note("m6", 74, 4 * Quarter, 2 * Quarter, Hand.Right),
            Note("b1", 48, 0, 3 * Quarter, Hand.Left),
            Note("b2", 43, 3 * Quarter, 3 * Quarter, Hand.Left),
        ], s_threeFour);
    }

    private static IEnumerable<(IVariationOperation Operation, OperationParameters Parameters)> EveryParameterSet()
    {
        foreach (RhythmReshapeKind kind in Enum.GetValues<RhythmReshapeKind>().Where(static kind => kind != RhythmReshapeKind.Displace))
        {
            yield return (RhythmReshape.Instance, new RhythmReshapeParameters(kind));
        }

        for (int eighths = RhythmReshapeParameters.MinEighths; eighths <= RhythmReshapeParameters.MaxEighths; eighths++)
        {
            yield return (RhythmReshape.Instance, new RhythmReshapeParameters(RhythmReshapeKind.Displace, eighths));
        }

        foreach (int steps in new[] { -4, -2, -1, 1, 2, 4 })
        {
            foreach (int repeats in new[] { 1, 3 })
            {
                yield return (MelodicSequence.Instance, new MelodicSequenceParameters(steps, repeats));
                yield return (MelodicSequence.Instance, new MelodicSequenceParameters(steps, repeats, Diatonic: false));
            }
        }

        IntervalSet?[] picks = [null, new IntervalSet([0]), new IntervalSet([1, 2])];
        foreach (IntervalSet? pick in picks)
        {
            foreach (int steps in new[] { -2, -1, 1, 2 })
            {
                yield return (IntervalReshape.Instance, new IntervalReshapeParameters(steps, pick));
            }

            yield return (ChangeDirection.Instance, new ChangeDirectionParameters(Diatonic: true, pick));
            yield return (ChangeDirection.Instance, new ChangeDirectionParameters(Diatonic: false, pick));
        }
    }

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach ((string name, _, _) in Motifs())
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_stated_fact_is_in_the_diff_for_every_operation_and_parameter_set(string motif)
    {
        (_, Note[] notes, MeterMap meter) = Motifs().Single(entry => entry.Name == motif);
        var context = new VariationContext(TickSpan.FromBounds(Tick.Zero, notes.Max(note => note.Span.End)), new HashSet<KeepProperty>(), Key: s_cMajor, Meter: meter);
        int audited = 0;
        foreach ((IVariationOperation operation, OperationParameters parameters) in EveryParameterSet())
        {
            if (operation.UnavailableReason(notes, context, parameters) is not null)
            {
                continue;
            }

            Variation variation = operation.Apply(notes, context, parameters);
            foreach (string sentence in Sentences(variation.Explanation))
            {
                string? problem = Audit(sentence, notes, variation, meter);
                Assert.True(problem is null, $"{operation.Id} {parameters} on {motif}: \"{sentence}\" {problem}");
            }

            audited++;
        }

        Assert.True(audited >= 20, $"Only {audited} parameter sets ran on {motif}.");
    }

    [Fact]
    public void The_audit_rejects_a_sentence_the_diff_does_not_support()
    {
        Note[] notes = Line(Quarter, 60, 62, 64, 67);
        var context = new VariationContext(TickSpan.FromBounds(Tick.Zero, new Tick(4 * Quarter)), new HashSet<KeepProperty>());
        Variation variation = RhythmReshape.Instance.Apply(notes, context, new RhythmReshapeParameters(RhythmReshapeKind.Syncopate));

        Assert.Null(Audit("3 notes that started on a beat now start 1 eighth ahead of it: beats 2, 3 and 4 of bar 1", notes, variation, MeterMap.Default));
        Assert.NotNull(Audit("3 notes that started on a beat now start 1 eighth ahead of it: beats 1, 3 and 4 of bar 1", notes, variation, MeterMap.Default));
        Assert.NotNull(Audit("2 of 4 notes change pitch", notes, variation, MeterMap.Default));
        Assert.NotNull(Audit("The melody sounds brighter", notes, variation, MeterMap.Default));
    }

    private static IEnumerable<string> Sentences(string explanation) =>
        SentenceEnd().Split(explanation.TrimEnd('.')).Where(static sentence => sentence.Length > 0);

    /// <summary>Why the sentence is not supported by the diff, or null when every fact in it is.</summary>
    private static string? Audit(string sentence, Note[] before, Variation variation, MeterMap meter)
    {
        IReadOnlyList<NoteChange> changes = variation.Changes;
        int count = changes.Count;
        Match m;
        if ((m = Added().Match(sentence)).Success)
        {
            return Expect(int.Parse(m.Groups[1].Value), variation.Added.Count);
        }

        if ((m = Removed().Match(sentence)).Success)
        {
            return Expect(int.Parse(m.Groups[1].Value), variation.Removed.Count);
        }

        if ((m = Repeats().Match(sentence)).Success)
        {
            int repeats = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
            return variation.Notes.Count == before.Length * (repeats + 1) ? null : $"has {variation.Notes.Count} notes from {before.Length}";
        }

        if (sentence == "The pitch sequence is unchanged")
        {
            return changes.Any(static change => change.PitchChanged) ? "but a pitch changed" : null;
        }

        if ((m = ChangePitch().Match(sentence)).Success)
        {
            return Expect((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)), (changes.Count(static change => change.PitchChanged), count));
        }

        if ((m = MelodyIntervals().Match(sentence)).Success)
        {
            return Expect((m.Groups[1].Value, m.Groups[2].Value), (Signed(MotifTexture.Read(before)), Signed(MotifTexture.Read(variation.Notes))));
        }

        if ((m = LengthScale().Match(sentence)).Success)
        {
            int stated = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
            Func<NoteChange, bool> scaled = m.Groups[3].Value == "twice"
                ? static change => change.After.Span.Duration == change.Before.Span.Duration * 2
                : static change => change.After.Span.Duration * 2 == change.Before.Span.Duration;
            return stated == count && changes.All(scaled) ? null : "but not every note's length scales so";
        }

        if (sentence == "Every note keeps its length")
        {
            return changes.Any(static change => change.DurationChanged) ? "but a length changed" : null;
        }

        if ((m = ChangeLength().Match(sentence)).Success)
        {
            return Expect((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)), (changes.Count(static change => change.DurationChanged), count));
        }

        if (sentence == "Every note starts where it did")
        {
            return changes.Any(static change => change.OnsetChanged) ? "but an onset moved" : null;
        }

        if ((m = Shift().Match(sentence)).Success)
        {
            long shift = Ticks(m.Groups[3].Value) * (m.Groups[4].Value == "later" ? 1 : -1);
            int stated = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
            return stated == count && changes.All(change => change.After.Span.Onset.Value - change.Before.Span.Onset.Value == shift) ? null : "but the notes do not all move so";
        }

        if ((m = Gaps().Match(sentence)).Success)
        {
            long[] from = Gaps(changes.Select(static change => change.Before));
            long[] to = Gaps(changes.Select(static change => change.After));
            bool scaled = m.Groups[1].Value == "doubles"
                ? from.Zip(to).All(static gap => gap.Second == gap.First * 2)
                : from.Zip(to).All(static gap => gap.Second * 2 == gap.First);
            return scaled ? Expect((int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)), (changes.Count(static change => change.OnsetChanged), count)) : "but the gaps do not scale so";
        }

        if ((m = NewPlace().Match(sentence)).Success)
        {
            return Expect((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)), (changes.Count(static change => change.OnsetChanged), count));
        }

        if ((m = Extent().Match(sentence)).Success)
        {
            return Expect((Ticks(m.Groups[1].Value), Ticks(m.Groups[2].Value)), (Extent(variation.Notes), Extent(before)));
        }

        if ((m = Unchanged().Match(sentence)).Success)
        {
            string[] names = m.Groups[1].Value.Replace(" and ", ", ", StringComparison.Ordinal).Split(", ");
            string? notKept = names.FirstOrDefault(name => variation.Properties.Single(check => check.Property == Property(name)).Status != KeepStatus.Kept);
            return notKept is null ? null : $"but {notKept} is not kept";
        }

        if ((m = Anticipated().Match(sentence)).Success)
        {
            NoteChange[] moved = [.. changes.Where(static change => change.OnsetChanged)];
            long early = Ticks(m.Groups[2].Value);
            bool ahead = moved.All(change => meter.ToPosition(change.Before.Span.Onset).BeatOffset == 0
                && change.Before.Span.Onset.Value - change.After.Span.Onset.Value == early);
            return !ahead ? "but a moved note did not start on a beat or moved by another length"
                : Expect(int.Parse(m.Groups[1].Value), moved.Length)
                    ?? Expect(BeatSet(m.Groups[3].Value), BeatSet(moved.Select(static change => change.Before.Span.Onset), meter));
        }

        if ((m = Dotted().Match(sentence)).Success)
        {
            NoteChange[] firsts = [.. changes.Where(static change => !change.OnsetChanged && change.After.Span.Duration > change.Before.Span.Duration)];
            Tick[] starts = [.. firsts.Select(static change => change.Before.Span.Onset).Distinct()];
            long apart = 2 * (firsts[0].After.Span.Duration - firsts[0].Before.Span.Duration);
            string? lengths = m.Groups[2].Value.Contains(" then ", StringComparison.Ordinal)
                ? Expect(m.Groups[2].Value.Split(" then ").Select(Ticks).ToArray(), [apart * 3 / 2, apart / 2])
                : null;
            return lengths ?? Expect(int.Parse(m.Groups[1].Value), starts.Length) ?? Expect(BeatSet(m.Groups[3].Value), BeatSet(starts, meter));
        }

        if ((m = Reshaped().Match(sentence)).Success)
        {
            (int From, int To)[] intervals = Intervals(before, variation, pitch => ScaleSteps.Locate(s_cMajor, pitch).Step);
            (int From, int To)[] changed = [.. intervals.Where(static interval => interval.From != interval.To)];
            int steps = int.Parse(m.Groups[5].Value) * (m.Groups[6].Value == "wider" ? 1 : -1);
            int stated = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
            int of = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : stated;
            bool sized = changed.All(interval => Math.Sign(interval.From) == Math.Sign(interval.To) && Math.Abs(interval.To) - Math.Abs(interval.From) == steps);
            return sized ? Expect((stated, of), (changed.Length, intervals.Length)) : "but the intervals did not change by that many steps";
        }

        if ((m = Mirrored().Match(sentence)).Success)
        {
            (int From, int To)[] intervals = Measure(m.Groups[2].Value, before, variation);
            string first = MotifTexture.Read(before).Melody[0].Pitch.ToString();
            return m.Groups[1].Value != first || MotifTexture.Read(variation.Notes).Melody[0].Pitch.ToString() != first ? "but the first note is not that note"
                : intervals.All(static interval => interval.To == -interval.From) ? null : "but an interval did not turn over";
        }

        if ((m = Turned().Match(sentence)).Success)
        {
            (int From, int To)[] intervals = Measure(m.Groups[3].Value, before, variation);
            bool flips = intervals.All(static interval => interval.To == interval.From || interval.To == -interval.From);
            int turned = intervals.Count(static interval => interval.From != 0 && interval.To == -interval.From);
            return flips ? Expect((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)), (turned, intervals.Length)) : "but an interval changed size";
        }

        return "is not a sentence the audit knows";
    }

    private static string? Expect<T>(T stated, T actual) =>
        EqualityComparer<T>.Default.Equals(stated, actual) || (stated is System.Collections.IEnumerable a && actual is System.Collections.IEnumerable b && a.Cast<object>().SequenceEqual(b.Cast<object>()))
            ? null
            : $"states {stated}, the diff has {actual}";

    private static (int From, int To)[] Measure(string size, Note[] before, Variation variation) => size == "semitones"
        ? Intervals(before, variation, static pitch => pitch)
        : Intervals(before, variation, pitch => ScaleSteps.Locate(s_cMajor, pitch).Step);

    private static (int From, int To)[] Intervals(Note[] before, Variation variation, Func<int, int> measure)
    {
        int[] from = [.. MotifTexture.Read(before).Melody.Select(note => measure(note.Pitch.MidiNumber))];
        int[] to = [.. MotifTexture.Read(variation.Notes).Melody.Select(note => measure(note.Pitch.MidiNumber))];
        return [.. from.Zip(from.Skip(1), static (a, b) => b - a).Zip(to.Zip(to.Skip(1), static (a, b) => b - a))];
    }

    private static string Signed(MotifTexture texture)
    {
        int[] pitches = [.. texture.Melody.Select(static note => note.Pitch.MidiNumber)];
        return string.Join(" ", pitches.Zip(pitches.Skip(1), static (a, b) => b - a).Select(static step => step > 0 ? $"+{step}" : step < 0 ? $"−{-step}" : "0"));
    }

    private static KeepProperty Property(string name) => name switch
    {
        "pitch order" => KeepProperty.PitchOrder,
        "melody pitches" or "melody pitch classes" => KeepProperty.Melody,
        "bass pitches" => KeepProperty.Bass,
        _ => Enum.Parse<KeepProperty>(name, ignoreCase: true),
    };

    private static long Ticks(string length)
    {
        Match m = LengthName().Match(length);
        Assert.True(m.Success, $"\"{length}\" is not a length");
        long unit = m.Groups[2].Value switch
        {
            "quarter note" => Quarter,
            "eighth" => Eighth,
            "sixteenth" => Quarter / 4,
            "thirty-second" => Quarter / 8,
            _ => 1,
        };
        return long.Parse(m.Groups[1].Value) * unit;
    }

    private static long[] Gaps(IEnumerable<Note> notes)
    {
        long[] onsets = [.. notes.Select(static note => note.Span.Onset.Value)];
        return [.. onsets.Zip(onsets.Skip(1), static (a, b) => b - a)];
    }

    private static long Extent(IEnumerable<Note> notes) =>
        notes.Max(static note => note.Span.End.Value) - notes.Min(static note => note.Span.Onset.Value);

    /// <summary>"beats 2, 3 and 4 of bar 1 and beat 1 of bar 2" as bar and beat pairs, in order.</summary>
    private static (int Bar, int Beat)[] BeatSet(string text) => [.. BeatGroup().Matches(text)
        .SelectMany(static group => Number().Matches(group.Groups[1].Value).Select(beat => (int.Parse(group.Groups[2].Value), int.Parse(beat.Value))))
        .Order()];

    private static (int Bar, int Beat)[] BeatSet(IEnumerable<Tick> onsets, MeterMap meter) => [.. onsets
        .Distinct()
        .Select(meter.ToPosition)
        .Select(static position => (position.Bar, position.Beat))
        .Order()];

    [GeneratedRegex(@"\.\s+")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"^(\d+) notes? (?:is|are) added$")]
    private static partial Regex Added();

    [GeneratedRegex(@"^(\d+) notes? (?:is|are) removed$")]
    private static partial Regex Removed();

    [GeneratedRegex(@"^The motif repeats (once|(\d+) times)\b.*, keeping its rhythm$")]
    private static partial Regex Repeats();

    [GeneratedRegex(@"^(\d+) of (\d+) notes change pitch$")]
    private static partial Regex ChangePitch();

    [GeneratedRegex(@"^The melody's intervals in semitones go from (.+) to (.+)$")]
    private static partial Regex MelodyIntervals();

    [GeneratedRegex(@"^(The note lasts|All (\d+) notes last) (twice|half) as long$")]
    private static partial Regex LengthScale();

    [GeneratedRegex(@"^(\d+) of (\d+) notes change length$")]
    private static partial Regex ChangeLength();

    [GeneratedRegex(@"^(The note starts|All (\d+) notes start) (.+) (later|earlier)$")]
    private static partial Regex Shift();

    [GeneratedRegex(@"^The time between onsets (doubles|halves), so (\d+) of (\d+) notes moves?$")]
    private static partial Regex Gaps();

    [GeneratedRegex(@"^(\d+) of (\d+) notes start at a new place$")]
    private static partial Regex NewPlace();

    [GeneratedRegex(@"^From its first onset to its last note's end, the motif now takes (.+) instead of (.+)$")]
    private static partial Regex Extent();

    [GeneratedRegex(@"^Unchanged: (.+)$")]
    private static partial Regex Unchanged();

    [GeneratedRegex(@"^(\d+) notes? that started on a beat now starts? (.+) ahead of it: (.+)$")]
    private static partial Regex Anticipated();

    [GeneratedRegex(@"^(\d+) pairs? of equal notes becomes? long–short, (.+), starting on (.+)$")]
    private static partial Regex Dotted();

    [GeneratedRegex(@"^(The melody interval is|All (\d+) melody intervals are|(\d+) of (\d+) melody intervals (?:is|are)) (\d+) scale steps? (wider|narrower) in the key's scale, keeping (?:its|their) direction$")]
    private static partial Regex Reshaped();

    [GeneratedRegex(@"^The melody is mirrored around its first note, (\S+): every interval turns the other way, keeping its size in (semitones|steps of the key's scale)$")]
    private static partial Regex Mirrored();

    [GeneratedRegex(@"^(\d+) of (\d+) melody intervals turns? the other way, keeping (?:its|their) size in (semitones|steps of the key's scale)$")]
    private static partial Regex Turned();

    [GeneratedRegex(@"^(\d+) (quarter note|eighth|sixteenth|thirty-second|tick)s?$")]
    private static partial Regex LengthName();

    [GeneratedRegex(@"beats? ([\d, and]+?) of bar (\d+)")]
    private static partial Regex BeatGroup();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();
}