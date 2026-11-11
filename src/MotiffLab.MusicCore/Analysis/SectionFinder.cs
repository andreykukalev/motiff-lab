using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Import.Transcription;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// What <see cref="SectionFinder"/> reads: the notes on their bars, and what the file says about them. Notation brings its
/// bar lines, texts, keys, time signatures and tempo; a recording (<see cref="Heard"/>) brings only its notes, bars and
/// tempo, so its boundaries are found from silences.
/// </summary>
public sealed record SectionEvidence(IReadOnlyList<Note> Notes, MeterMap Meter, Tick End)
{
    public IReadOnlyList<KeySignature> Keys { get; init; } = [];

    public IReadOnlyList<PlaybackTempo> Tempos { get; init; } = [];

    public IReadOnlyList<MusicXmlBarline> Barlines { get; init; } = [];

    public IReadOnlyList<MusicXmlText> Texts { get; init; } = [];

    /// <summary>True for notes heard in a recording (<see cref="Transcribed.IsHeard"/>).</summary>
    public bool Heard { get; init; }

    /// <summary>The evidence of an imported piece, with its notes as the player has them now and the tempo it plays at.</summary>
    public static SectionEvidence Of(ImportedPiece piece, IReadOnlyList<Note> notes, IReadOnlyList<PlaybackTempo> tempos)
    {
        ArgumentNullException.ThrowIfNull(piece);
        return new SectionEvidence(notes, piece.Meter, piece.End)
        {
            Keys = piece.Keys,
            Tempos = tempos,
            Barlines = piece.MusicXml?.Barlines ?? [],
            Texts = piece.MusicXml?.Texts ?? [],
            Heard = Transcribed.IsHeard(piece),
        };
    }
}

/// <summary>
/// Finds where the pieces, movements and large sections of a long score or recording begin (Andrei, 2026-11-11,
/// "automatic bookmarks"; an experiment), to propose them as bookmarks. Each boundary rests on facts the file shows: in
/// notation a final or double bar line, a heading, a rehearsal mark, a new key, time signature or tempo, whole bars of
/// rest; in a recording a silence of some seconds, scaled to the tempo, and a different key or tempo around it. Each
/// fact adds to the boundary's confidence; a boundary is proposed when the facts together reach
/// <see cref="MinConfidence"/>, at least <see cref="MinBars"/> bars from the start, the end and any stronger boundary.
/// </summary>
/// <remarks>
/// Pure and deterministic: the same evidence gives the same sections. Every position is a bar start in exact ticks;
/// seconds come from the tempo map in integer microseconds. The result is an inference, never a fact of the file.
/// </remarks>
public static class SectionFinder
{
    /// <summary>The confidence the facts at a bar must reach together for a boundary to be proposed there.</summary>
    public const double MinConfidence = 0.35;

    /// <summary>The fewest bars a proposed section has, and the fewest between two boundaries.</summary>
    public const int MinBars = 8;

    /// <summary>The shortest silence in a recording that can part two pieces, at any tempo.</summary>
    public const long MinSilenceMicroseconds = 2_500_000;

    /// <summary>The shortest silence in a recording that can part two pieces, in beats at the tempo it falls in.</summary>
    public const int MinSilenceBeats = 4;

    /// <summary>The highest confidence a found boundary gets: it is never a fact.</summary>
    private const double MostConfidence = 0.95;

    /// <summary>The bars around a silence whose keys and tempos are compared.</summary>
    private const int AroundBars = 16;

    /// <summary>The fewest notes on each side of a silence for their keys to be compared.</summary>
    private const int MinKeyNotes = 16;

    public static FoundSections Find(SectionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Notes.Count == 0 || evidence.End.Value <= 0)
        {
            return FoundSections.None;
        }

        MeterMap meter = evidence.Meter;
        int firstBar = meter.BarAt(Tick.Zero).Number;
        int lastBar = meter.BarAt(new Tick(evidence.End.Value - 1)).Number;
        var cues = new SortedDictionary<int, List<SectionCue>>();
        void Add(Tick at, SectionCue cue)
        {
            int bar = meter.BarAt(at).Number;
            if (bar > firstBar && bar <= lastBar)
            {
                (cues.TryGetValue(bar, out List<SectionCue>? list) ? list : cues[bar] = []).Add(cue);
            }
        }

        SectionCues.Notation(evidence, Add);
        Note[] sorted = [.. evidence.Notes.OrderBy(note => note.Span.Onset).ThenBy(note => note.Id, StringComparer.Ordinal)];
        foreach ((Tick silent, Tick next) in Gaps(sorted))
        {
            if (evidence.Heard)
            {
                Heard(evidence, sorted, silent, next, Add);
            }
            else if (SectionCues.Rest(meter, silent, next) is { } rest)
            {
                Add(next, rest);
            }
        }

        int[] boundaries = Strongest(cues, firstBar, lastBar);
        return boundaries.Length == 0 ? FoundSections.None : Sections(evidence, cues, boundaries, lastBar);
    }

    /// <summary>The confidence of the facts at one bar together: each fact leaves the doubt the others left smaller.</summary>
    internal static double Confidence(IEnumerable<SectionCue> cues)
    {
        double doubt = cues.Aggregate(1.0, (left, cue) => left * (1 - cue.Weight));
        return Math.Round(Math.Min(MostConfidence, 1 - doubt), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The stretches without a sounding note between two notes: from where the last sounding note ends to the next onset.</summary>
    private static IEnumerable<(Tick Silent, Tick Next)> Gaps(Note[] sorted)
    {
        Tick sounding = sorted[0].Span.End;
        foreach (Note note in sorted.Skip(1))
        {
            if (note.Span.Onset > sounding)
            {
                yield return (sounding, note.Span.Onset);
            }

            sounding = note.Span.End > sounding ? note.Span.End : sounding;
        }
    }

    /// <summary>A silence in a recording long enough to part two pieces, with the key and tempo heard around it.</summary>
    private static void Heard(SectionEvidence evidence, Note[] sorted, Tick silent, Tick next, Action<Tick, SectionCue> add)
    {
        long microseconds = Microseconds(evidence.Tempos, silent, next);
        long beat = evidence.Meter.SignatureAt(silent).BeatTicks * MicrosecondsPerQuarter(evidence.Tempos, silent) / CanonicalTime.TicksPerQuarter;
        long shortest = Math.Max(MinSilenceMicroseconds, MinSilenceBeats * beat);
        if (microseconds < shortest)
        {
            return;
        }

        add(next, SectionCues.Silence(microseconds, microseconds >= 2 * shortest));
        (TickSpan before, TickSpan after) = Around(evidence.Meter, silent, next);
        Note[] earlier = [.. sorted.Where(note => before.Contains(note.Span.Onset))];
        Note[] later = [.. sorted.Where(note => after.Contains(note.Span.Onset))];
        if (earlier.Length >= MinKeyNotes && later.Length >= MinKeyNotes
            && Transcribed.KeyOf(earlier) is { } from && Transcribed.KeyOf(later) is { } to && from != to)
        {
            add(next, SectionCues.HeardKey(from, to));
        }

        if (Median(evidence.Tempos, before) is { } earlierTempo && Median(evidence.Tempos, after) is { } laterTempo
            && (earlierTempo * 100L >= laterTempo * 115L || laterTempo * 100L >= earlierTempo * 115L))
        {
            add(next, SectionCues.HeardTempo(earlierTempo, laterTempo));
        }
    }

    /// <summary>The bars before a silence and after it whose music is compared.</summary>
    private static (TickSpan Before, TickSpan After) Around(MeterMap meter, Tick silent, Tick next)
    {
        Bar last = meter.BarAt(silent.Value > 0 ? new Tick(silent.Value - 1) : silent);
        Tick from = meter.GetBar(Math.Max(meter.FirstBarNumber, last.Number - AroundBars + 1)).Span.Onset;
        Tick to = meter.GetBar(meter.BarAt(next).Number + AroundBars - 1).Span.End;
        return (TickSpan.FromBounds(from, from < silent ? silent : new Tick(from.Value + 1)), TickSpan.FromBounds(next, to > next ? to : new Tick(next.Value + 1)));
    }

    /// <summary>The middle of the tempos set inside the span, as microseconds per quarter; null for fewer than three.</summary>
    private static int? Median(IReadOnlyList<PlaybackTempo> tempos, TickSpan span)
    {
        int[] inside = [.. tempos.Where(tempo => span.Contains(tempo.At)).Select(tempo => tempo.MicrosecondsPerQuarter).Order()];
        return inside.Length < 3 ? null : inside[inside.Length / 2];
    }

    /// <summary>The bars proposed as boundaries: the strongest first, each far enough from the start, the end and the stronger ones.</summary>
    private static int[] Strongest(SortedDictionary<int, List<SectionCue>> cues, int firstBar, int lastBar)
    {
        var kept = new List<int>();
        foreach (int bar in cues
            .Select(pair => (Bar: pair.Key, Confidence: Confidence(pair.Value)))
            .Where(candidate => candidate.Confidence >= MinConfidence)
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Bar)
            .Select(candidate => candidate.Bar))
        {
            if (bar - firstBar >= MinBars && lastBar - bar + 1 >= MinBars && kept.All(other => Math.Abs(other - bar) >= MinBars))
            {
                kept.Add(bar);
            }
        }

        return [.. kept.Order()];
    }

    /// <summary>The sections between the boundaries: the opening up to the first, then one from each boundary to the next or the end.</summary>
    private static FoundSections Sections(SectionEvidence evidence, SortedDictionary<int, List<SectionCue>> cues, int[] boundaries, int lastBar)
    {
        MeterMap meter = evidence.Meter;
        int firstBar = meter.BarAt(Tick.Zero).Number;
        var sections = new List<FoundSection>(boundaries.Length + 1);
        int[] starts = [firstBar, .. boundaries];
        for (int index = 0; index < starts.Length; index++)
        {
            int bar = starts[index];
            Tick onset = index == 0 ? Tick.Zero : meter.GetBar(bar).Span.Onset;
            Tick end = index + 1 < starts.Length ? meter.GetBar(starts[index + 1]).Span.Onset : evidence.End;
            IReadOnlyList<SectionCue> found = index == 0
                ? [SectionCues.Opening(evidence.Heard)]
                : [.. cues[bar].OrderByDescending(cue => cue.Weight).ThenBy(cue => cue.Kind)];
            double confidence = Confidence(index == 0 ? cues[starts[1]] : found);
            int last = index + 1 < starts.Length ? starts[index + 1] - 1 : lastBar;
            sections.Add(new FoundSection(index + 1, TickSpan.FromBounds(onset, end), bar, last, confidence, found));
        }

        return new FoundSections(sections, meter, lastBar);
    }

    /// <summary>How long the stretch lasts at the tempo map, in microseconds; 120 BPM before the first tempo.</summary>
    internal static long Microseconds(IReadOnlyList<PlaybackTempo> tempos, Tick from, Tick to)
    {
        long total = 0;
        long cursor = from.Value;
        long perQuarter = MicrosecondsPerQuarter(tempos, from);
        foreach (PlaybackTempo tempo in tempos.Where(tempo => tempo.At > from && tempo.At < to).OrderBy(tempo => tempo.At))
        {
            total += (tempo.At.Value - cursor) * perQuarter / CanonicalTime.TicksPerQuarter;
            cursor = tempo.At.Value;
            perQuarter = tempo.MicrosecondsPerQuarter;
        }

        return total + ((to.Value - cursor) * perQuarter / CanonicalTime.TicksPerQuarter);
    }

    private static long MicrosecondsPerQuarter(IReadOnlyList<PlaybackTempo> tempos, Tick at) =>
        tempos.Where(tempo => tempo.At <= at).OrderBy(tempo => tempo.At).Select(tempo => (long)tempo.MicrosecondsPerQuarter).LastOrDefault(PlaybackTempo.DefaultMicrosecondsPerQuarter);
}