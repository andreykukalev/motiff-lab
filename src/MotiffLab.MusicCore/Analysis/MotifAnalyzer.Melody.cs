using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>
/// Melody facts beyond line, intervals in semitones, contour, range and degrees (S1-A2): interval
/// names, repeated notes, melody notes that sound with lower notes, and the phrase boundaries the
/// source marks or the player set.
/// </summary>
public static partial class MotifAnalyzer
{
    /// <summary>A list of intervals names at most this many, then says how many more there are.</summary>
    private const int MaxNamedIntervals = 12;

    private static MotifFact IntervalNamesFact(int[] intervals, FactCertainty certainty, string basis)
    {
        string[] names = [.. intervals.Select(step => step == 0 ? "unison" : $"{IntervalName(step)} {(step > 0 ? "up" : "down")}")];
        string list = names.Length <= MaxNamedIntervals
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(MaxNamedIntervals))} and {names.Length - MaxNamedIntervals} more";
        string text = $"Intervals by name: {list}.";

        // The most common size, up or down, when one size clearly leads.
        IGrouping<int, int>[] sizes = [.. intervals.GroupBy(Math.Abs).OrderByDescending(group => group.Count()).ThenBy(group => group.Key)];
        if (sizes[0].Count() > 1 && (sizes.Length == 1 || sizes[0].Count() > sizes[1].Count()))
        {
            text += $" Most common: {(sizes[0].Key == 0 ? "the repeated note" : WithArticle(IntervalName(sizes[0].Key)))} ({sizes[0].Count()} of {intervals.Length}).";
        }

        return new MotifFact("melody.interval-names", FactLens.Melody, certainty, text, $"{basis} Names go by size in semitones, not by spelling.");
    }

    private static MotifFact RepeatedNotesFact(Note[] line, MeterMap meter, MusicalKey key, FactCertainty certainty, string basis)
    {
        var runs = new List<string>();
        for (int start = 0; start < line.Length;)
        {
            int end = start + 1;
            while (end < line.Length && line[end].Pitch.MidiNumber == line[start].Pitch.MidiNumber)
            {
                end++;
            }

            if (end - start > 1)
            {
                string times = end - start == 2 ? "twice" : $"{end - start} times";
                runs.Add($"{key.Spell(line[start].Pitch.MidiNumber)} {times} from {Place(meter.ToPosition(line[start].Span.Onset))}");
            }

            start = end;
        }

        string text = runs.Count == 0
            ? "No note is repeated straight away."
            : $"Repeated straight away: {string.Join("; ", runs)}.";

        IGrouping<int, Note>[] pitches = [.. line.GroupBy(note => note.Pitch.MidiNumber).OrderByDescending(group => group.Count()).ThenBy(group => group.Key)];
        if (pitches[0].Count() > 1 && (pitches.Length == 1 || pitches[0].Count() > pitches[1].Count()))
        {
            text += $" {key.Spell(pitches[0].Key)} sounds most often, {pitches[0].Count()} of {Count(line.Length, "note", "notes")}.";
        }

        return new MotifFact("melody.repeated-notes", FactLens.Melody, certainty, text, basis);
    }

    /// <summary>
    /// Melody notes that sound over lower notes, struck with them or over notes still held, and those
    /// whose pitch class a lower note shares: such a note is both a melody note and a note of the chord
    /// under it, so the ear can take it either way.
    /// </summary>
    private static MotifFact SoundingWithFact(MotifTexture texture, MusicalKey key)
    {
        int struck = 0;
        int held = 0;
        var shared = new List<string>();
        foreach (Note note in texture.Melody)
        {
            Note[] lower = [.. texture.SoundingWith(note).Where(other => other.Pitch < note.Pitch)];
            if (lower.Length == 0)
            {
                continue;
            }

            if (lower.Any(other => other.Span.Onset == note.Span.Onset))
            {
                struck++;
            }
            else
            {
                held++;
            }

            Note[] doubled = [.. lower.Where(other => other.Pitch.PitchClass == note.Pitch.PitchClass).OrderByDescending(other => other.Pitch.MidiNumber)];
            if (doubled.Length > 0)
            {
                shared.Add($"{key.Spell(note.Pitch.MidiNumber)} (with {string.Join(", ", doubled.Select(other => key.Spell(other.Pitch.MidiNumber)))})");
            }
        }

        int over = struck + held;
        string text;
        if (over == 0)
        {
            text = "Every melody note sounds on its own; the other notes fall between melody notes.";
        }
        else
        {
            string[] how = [.. new[] { (struck, "struck together with them"), (held, "over notes still held") }
                .Where(part => part.Item1 > 0)
                .Select(part => $"{part.Item1} {part.Item2}")];
            text = $"{over} of {Count(texture.Melody.Count, "melody note", "melody notes")} sound over lower notes: {string.Join(", ", how)}.";
            string list = shared.Count <= MaxNamedIntervals
                ? string.Join(", ", shared)
                : $"{string.Join(", ", shared.Take(MaxNamedIntervals))} and {shared.Count - MaxNamedIntervals} more";
            text += shared.Count == 0
                ? " None shares its pitch class with a lower note."
                : $" {Count(shared.Count, "shares its pitch class", "share their pitch class")} with a lower note, so {(shared.Count == 1 ? "it is" : "they are")} also part of the chord below: {list}.";
        }

        return new MotifFact(
            "melody.over-chords",
            FactLens.Melody,
            FactCertainty.Inferred,
            text,
            "The melody is the inferred top line. A lower note sounds with a melody note when it starts with it or is still held when it starts. A shared pitch class means the melody note is also a note of the chord under it.");
    }

    private static MotifFact PhraseFact(Note[] notes, IEnumerable<PhraseBoundary> phrases, MeterMap meter)
    {
        Tick first = notes[0].Span.Onset;
        Tick last = notes.Max(note => note.Span.Onset);
        PhraseBoundary[] all = [.. phrases.OrderBy(boundary => boundary.At)];
        PhraseBoundary[] inside = [.. all.Where(boundary => boundary.At > first && boundary.At <= last)];
        PhraseBoundary? atStart = all.FirstOrDefault(boundary => boundary.At == first);

        string text = inside.Length == 0
            ? "No phrase boundary falls inside the motif."
            : $"{Count(inside.Length, "phrase boundary", "phrase boundaries")} inside the motif: {string.Join("; ", inside.Select(boundary => $"at {Place(meter.ToPosition(boundary.At))}, {EvidenceText(boundary)}"))}.";
        if (atStart is not null)
        {
            text += $" It starts where a phrase starts ({EvidenceText(atStart)}).";
        }

        PhraseBoundary[] cited = atStart is null ? inside : [atStart, .. inside];
        bool inferred = cited.Any(boundary => boundary.Attribution.Provenance is Provenance.Inferred or Provenance.Detected);
        const string Basis = "Boundaries come from phrase slurs, rests of a beat or more and repeat signs in the source, from a passage that comes back note for note, and from boundaries the player set; where none of these marks one, none is invented.";
        return inferred
            ? new MotifFact("melody.phrases", FactLens.Melody, FactCertainty.Inferred, text, $"{Basis} A boundary from a rest or a return alone is a reading of the notes, not a mark in the score.")
            : new MotifFact("melody.phrases", FactLens.Melody, FactCertainty.Detected, text, Basis);
    }

    /// <summary>"from a slur and a rest of a beat or more", "set by the player", or "from a slur, confirmed by the player".</summary>
    private static string EvidenceText(PhraseBoundary boundary)
    {
        string[] evidence = [.. boundary.Evidence.Select(mark => mark switch
        {
            PhraseEvidence.Slur => "a slur",
            PhraseEvidence.Rest => "a rest of a beat or more",
            PhraseEvidence.RepeatSign => "a repeat sign",
            PhraseEvidence.Repetition => "a passage that comes back note for note",
            _ => mark.ToString(),
        })];
        string source = evidence.Length switch
        {
            0 => "",
            1 => $"from {evidence[0]}",
            _ => $"from {string.Join(", ", evidence[..^1])} and {evidence[^1]}",
        };

        return boundary.Attribution.Provenance switch
        {
            Provenance.UserEdited or Provenance.UserConfirmed when source.Length == 0 => "set by the player",
            Provenance.UserEdited or Provenance.UserConfirmed => $"{source}, confirmed by the player",
            _ => source,
        };
    }
}