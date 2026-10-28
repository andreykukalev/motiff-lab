using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Analysis;

/// <summary>The direction from one melody note to the next.</summary>
public enum ContourStep
{
    Up,
    Down,
    Same,
}

/// <summary>The key a motif was read in, how, and the key that came closest.</summary>
/// <param name="RunnerUp">The next best key when the key was inferred from the notes; null when the file states it.</param>
public sealed record KeyReading(MusicalKey Key, FactCertainty Certainty, MusicalKey? RunnerUp);

/// <summary>What the engine measured in a motif, as values and as plain facts.</summary>
/// <param name="Line">The melody: the notes in order, or the top line (<see cref="MotifTexture.Melody"/>) when notes sound together.</param>
/// <param name="LineIsTopNotes">True when notes sound together, so <paramref name="Line"/> is inferred as the top line.</param>
/// <param name="Intervals">Semitones from each melody note to the next; positive is up.</param>
/// <param name="Contour">The direction of each interval.</param>
/// <param name="Key">Null when the motif has no notes.</param>
/// <param name="Facts">The facts in display order: melody, rhythm, then key.</param>
public sealed record MotifAnalysis(
    IReadOnlyList<Note> Line,
    bool LineIsTopNotes,
    IReadOnlyList<int> Intervals,
    IReadOnlyList<ContourStep> Contour,
    KeyReading? Key,
    IReadOnlyList<MotifFact> Facts);

/// <summary>
/// Plain facts about a selected motif: melody line, intervals, contour, range, scale degrees
/// (S1-A2), durations, onset grid, silences, beat grouping, syncopation, accents where marked and
/// repeated rhythms (S1-A1), and key. No AI: every fact is measured
/// from the notes or read from the file, and labelled detected, inferred or uncertain.
/// </summary>
/// <remarks>
/// Analysis is pure: the same notes, meter, selection and key signature give the same facts.
/// Interval names go by size in semitones, not by spelling, so three semitones is a minor 3rd
/// even where the score writes an augmented 2nd.
/// </remarks>
public static partial class MotifAnalyzer
{
    /// <summary>A key read from fewer notes or pitch classes than this is uncertain.</summary>
    public const int MinNotesForKey = 6;

    public const int MinPitchClassesForKey = 5;

    /// <summary>A key inferred from the notes is uncertain when the next key scores within this margin.</summary>
    public const double ClearKeyMargin = 0.05;

    private static readonly string[] s_intervalNames =
        ["unison", "minor 2nd", "major 2nd", "minor 3rd", "major 3rd", "perfect 4th", "tritone", "perfect 5th", "minor 6th", "major 6th", "minor 7th", "major 7th"];

    private static readonly string[] s_majorDegrees = ["1", "♯1", "2", "♯2", "3", "4", "♯4", "5", "♯5", "6", "♭7", "7"];

    private static readonly string[] s_minorDegrees = ["1", "♭2", "2", "3", "♯3", "4", "♯4", "5", "6", "♯6", "7", "♯7"];

    // Note values by length in ticks: singular and plural.
    private static readonly (long Ticks, string One, string Many)[] s_values =
    [
        (1920, "whole note", "whole notes"),
        (1440, "dotted half", "dotted halves"),
        (960, "half", "halves"),
        (720, "dotted quarter", "dotted quarters"),
        (480, "quarter", "quarters"),
        (360, "dotted eighth", "dotted eighths"),
        (320, "quarter-note triplet", "quarter-note triplets"),
        (240, "eighth", "eighths"),
        (180, "dotted sixteenth", "dotted sixteenths"),
        (160, "eighth-note triplet", "eighth-note triplets"),
        (120, "sixteenth", "sixteenths"),
        (80, "sixteenth-note triplet", "sixteenth-note triplets"),
        (60, "thirty-second", "thirty-seconds"),
    ];

    // Onset grids from coarse to fine, measured from the bar line.
    private static readonly (long Ticks, string Name, bool Triplet)[] s_grids =
    [
        (480, "quarter-note", false),
        (240, "eighth-note", false),
        (160, "eighth-note triplet", true),
        (120, "sixteenth-note", false),
        (80, "sixteenth-note triplet", true),
        (60, "thirty-second-note", false),
    ];

    /// <param name="notes">The notes of the piece; the selection's notes are picked by id.</param>
    /// <param name="signature">The key signature in force where the selection starts, or null when the file has none.</param>
    /// <param name="accents">The accents the file writes, or null when the format cannot mark accents (MIDI), which leaves out the accent fact.</param>
    /// <param name="phrases">
    /// The phrase boundaries of the notes the motif was picked from, found in the source and set by
    /// the player (<see cref="PhraseBoundaryFinder"/>), or null to leave out the phrase fact.
    /// </param>
    public static MotifAnalysis Analyze(
        IEnumerable<Note> notes,
        MeterMap meter,
        MotifSelection selection,
        KeySignature? signature,
        IEnumerable<AccentMark>? accents = null,
        IEnumerable<PhraseBoundary>? phrases = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(selection);

        var byId = new Dictionary<string, Note>(StringComparer.Ordinal);
        foreach (Note note in notes)
        {
            byId.TryAdd(note.Id, note);
        }

        Note[] chosen = [.. selection.NoteIds.Where(byId.ContainsKey).Select(id => byId[id])
            .OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal)];
        if (chosen.Length == 0)
        {
            return new MotifAnalysis([], false, [], [], null, []);
        }

        IGrouping<Tick, Note>[] onsets = [.. chosen.GroupBy(note => note.Span.Onset)];
        MotifTexture texture = MotifTexture.Read(chosen);
        Note[] line = [.. texture.Melody];
        bool topNotes = !texture.IsSingleLine;
        int[] intervals = [.. line.Zip(line.Skip(1), (from, to) => to.Pitch.MidiNumber - from.Pitch.MidiNumber)];
        ContourStep[] contour = [.. intervals.Select(step => step > 0 ? ContourStep.Up : step < 0 ? ContourStep.Down : ContourStep.Same)];
        KeyReading key = ReadKey(chosen, signature);

        FactCertainty lineCertainty = topNotes ? FactCertainty.Inferred : FactCertainty.Detected;
        string lineBasis = topNotes
            ? "Notes sound together, so the melody is taken as the top line: the highest note at each onset, among right-hand notes when there are any, leaving out notes under a melody note still held."
            : "Read from the notes, one at a time.";

        var facts = new List<MotifFact>
        {
            new(
                "melody.line",
                FactLens.Melody,
                lineCertainty,
                $"{Count(line.Length, "note", "notes")} in the {(topNotes ? "top line" : "melody")}: {string.Join(" ", line.Select(note => key.Key.Spell(note.Pitch.MidiNumber)))}.",
                $"{lineBasis} Spelled for {key.Key.Name}.")
            {
                Places = [FactPlace.Of(topNotes ? "the top line" : "the melody", line)],
            },
        };

        if (intervals.Length > 0)
        {
            facts.Add(new MotifFact("melody.intervals", FactLens.Melody, lineCertainty, IntervalsText(intervals), $"{lineBasis} Intervals are named by their size in semitones.")
            {
                Places = LeapPlaces(line, intervals, key.Key),
            });
            facts.Add(new MotifFact("melody.contour", FactLens.Melody, lineCertainty, ContourText(contour, intervals.Sum()), lineBasis)
            {
                Places = ContourPlaces(line, contour, key.Key),
            });
        }

        facts.Add(RangeFact(chosen, key.Key));
        facts.Add(new MotifFact(
            "melody.degrees",
            FactLens.Melody,
            Weaker(lineCertainty, key.Certainty),
            $"Scale degrees in {key.Key.Name}: {string.Join(" ", line.Select(note => Degree(note.Pitch, key.Key)))}.",
            $"Counted from the tonic of {key.Key.Name}; ♯ and ♭ mark notes outside its {(key.Key.Mode == KeyMode.Minor ? "natural minor" : "major")} scale. {(key.Certainty == FactCertainty.Detected ? "The key is stated by the file." : "The key is a reading of the notes, so the degrees are too.")}")
        {
            // The notes outside the scale, which the degrees mark with ♯ or ♭.
            Places = [.. line
                .Select(note => (Note: note, Degree: Degree(note.Pitch, key.Key)))
                .Where(entry => entry.Degree.Length > 1)
                .Select(entry => FactPlace.Of($"{key.Key.Spell(entry.Note.Pitch.MidiNumber)}, degree {entry.Degree}", [entry.Note]))],
        });

        if (intervals.Length > 0)
        {
            facts.Add(IntervalNamesFact(line, intervals, lineCertainty, lineBasis));
        }

        facts.Add(RepeatedNotesFact(line, meter, key.Key, lineCertainty, lineBasis));
        if (topNotes)
        {
            facts.Add(SoundingWithFact(texture, key.Key));
        }

        if (phrases is not null)
        {
            facts.Add(PhraseFact(chosen, phrases, meter));
        }

        bool offGrid = OffGridCount(chosen, meter) > 0;
        facts.Add(DurationsFact(chosen, offGrid));
        facts.Add(GridFact(chosen, onsets.Select(group => group.Key).ToArray(), meter));
        facts.Add(SilenceFact(chosen, selection.Span));
        Hit[] hits = Hits(onsets);
        facts.Add(GroupingFact(hits, meter, selection.Span, offGrid));
        facts.Add(SyncopationFact(chosen, hits, meter, offGrid));
        if (accents is not null)
        {
            facts.Add(AccentFact(chosen, accents, meter, key.Key));
        }

        facts.Add(PatternFact(chosen, hits, meter, selection.Span, offGrid));
        facts.Add(KeyFact(key, signature, chosen.Length, PitchClassCount(chosen)));

        return new MotifAnalysis(line, topNotes, intervals, contour, key, WithHand(facts, selection));
    }

    /// <summary>"minor 3rd", "octave", "octave and a major 2nd", "2 octaves".</summary>
    public static string IntervalName(int semitones)
    {
        int size = Math.Abs(semitones);
        int octaves = size / 12;
        int rest = size % 12;
        if (octaves == 0)
        {
            return s_intervalNames[rest];
        }

        string octaveText = octaves == 1 ? "octave" : $"{octaves} octaves";
        return rest == 0 ? octaveText : $"{octaveText} and {WithArticle(s_intervalNames[rest])}";
    }

    /// <summary>The scale degree of a pitch in a key: "5", or "♯7" for a note outside the scale.</summary>
    public static string Degree(Pitch pitch, MusicalKey key)
    {
        int offset = ((pitch.PitchClass - key.TonicPitchClass) % 12 + 12) % 12;
        return key.Mode == KeyMode.Major ? s_majorDegrees[offset] : s_minorDegrees[offset];
    }

    /// <summary>Detected facts become inferred when the hand that picked the motif's notes is inferred.</summary>
    private static List<MotifFact> WithHand(List<MotifFact> facts, MotifSelection selection)
    {
        if (!selection.HandInferred)
        {
            return facts;
        }

        const string HandBasis = "The hand is inferred, so which notes the motif holds is inferred too. ";
        return [.. facts.Select(fact => fact.Certainty == FactCertainty.Detected
            ? fact with { Certainty = FactCertainty.Inferred, Basis = HandBasis + fact.Basis }
            : fact)];
    }

    /// <summary>The key the notes are in: stated by the file, or read from the pitches with the signature's help when it gives only the accidentals.</summary>
    internal static KeyReading ReadKey(Note[] notes, KeySignature? signature)
    {
        var weights = new double[12];
        foreach (Note note in notes)
        {
            weights[note.Pitch.PitchClass] += note.Span.Duration;
        }

        bool thin = notes.Length < MinNotesForKey || PitchClassCount(notes) < MinPitchClassesForKey;
        if (signature is { Mode: { } mode })
        {
            return new KeyReading(new MusicalKey(signature.Fifths, mode), FactCertainty.Detected, null);
        }

        if (signature is not null)
        {
            var major = new MusicalKey(signature.Fifths, KeyMode.Major);
            double majorScore = KeyFinder.Score(weights, major);
            double minorScore = KeyFinder.Score(weights, major.Relative);
            (MusicalKey best, MusicalKey other) = minorScore > majorScore ? (major.Relative, major) : (major, major.Relative);
            bool close = Math.Abs(majorScore - minorScore) < ClearKeyMargin;
            return new KeyReading(best, thin || close ? FactCertainty.Uncertain : FactCertainty.Inferred, other);
        }

        IReadOnlyList<(MusicalKey Key, double Score)> ranked = KeyFinder.Rank(weights);
        bool unclear = ranked[0].Score - ranked[1].Score < ClearKeyMargin;
        return new KeyReading(ranked[0].Key, thin || unclear ? FactCertainty.Uncertain : FactCertainty.Inferred, ranked[1].Key);
    }

    private static MotifFact KeyFact(KeyReading key, KeySignature? signature, int noteCount, int pitchClasses)
    {
        string thin = noteCount < MinNotesForKey || pitchClasses < MinPitchClassesForKey
            ? $" With {Count(noteCount, "note", "notes")} and {Count(pitchClasses, "pitch class", "pitch classes")}, that is a weak guess."
            : key.Certainty == FactCertainty.Uncertain ? $" {key.RunnerUp?.Name} fits almost as well." : "";

        string basis = signature switch
        {
            { Mode: not null } => $"The file states the key signature ({Accidentals(signature.Fifths)}) and the mode.",
            not null => $"The key signature has {Accidentals(signature.Fifths)} but no mode; the notes fit {key.Key.Name} better than {key.RunnerUp?.Name}.{thin}",
            null => $"The file has no key signature; the notes fit {key.Key.Name} best, then {key.RunnerUp?.Name}.{thin}",
        };

        return new MotifFact("key", FactLens.Key, key.Certainty, $"Key: {key.Key.Name}.", basis);
    }

    private static string IntervalsText(int[] intervals)
    {
        int steps = intervals.Count(step => Math.Abs(step) is 1 or 2);
        int leaps = intervals.Count(step => Math.Abs(step) > 2);
        int repeats = intervals.Count(step => step == 0);
        string list = string.Join(" ", intervals.Select(step => step > 0 ? $"+{step}" : step < 0 ? $"−{-step}" : "0"));

        var summary = new List<string>();
        if (steps > 0)
        {
            summary.Add(Count(steps, "step", "steps"));
        }

        if (leaps > 0)
        {
            summary.Add(Count(leaps, "leap", "leaps"));
        }

        if (repeats > 0)
        {
            summary.Add(Count(repeats, "repeated note", "repeated notes"));
        }

        string text = $"Intervals in semitones: {list}. {Capitalize(string.Join(", ", summary))}.";
        if (leaps > 0)
        {
            int largest = intervals.OrderByDescending(Math.Abs).ThenByDescending(step => step).First();
            text += $" Largest leap: {WithArticle(IntervalName(largest))} {(largest > 0 ? "up" : "down")}.";
        }

        return text;
    }

    private static string ContourText(ContourStep[] contour, int netSemitones)
    {
        var runs = new List<ContourStep>();
        foreach (ContourStep step in contour.Where(step => step != ContourStep.Same))
        {
            if (runs.Count == 0 || runs[^1] != step)
            {
                runs.Add(step);
            }
        }

        string shape = runs.Count switch
        {
            0 => "Stays on one pitch",
            1 => runs[0] == ContourStep.Up ? "Rises" : "Falls",
            <= 3 => "Moves " + string.Join(", then ", runs.Select(step => step == ContourStep.Up ? "up" : "down")),
            _ => $"Zigzags, turning {runs.Count - 1} times",
        };

        if (runs.Count == 0)
        {
            return $"{shape}.";
        }

        string end = netSemitones == 0
            ? "ends where it starts"
            : $"ends {WithArticle(IntervalName(netSemitones))} {(netSemitones > 0 ? "higher" : "lower")} than it starts";
        return $"{shape}; {end}.";
    }

    private static MotifFact RangeFact(Note[] notes, MusicalKey key)
    {
        Note low = notes.MinBy(note => note.Pitch.MidiNumber)!;
        Note high = notes.MaxBy(note => note.Pitch.MidiNumber)!;
        int span = high.Pitch.MidiNumber - low.Pitch.MidiNumber;
        string text = span == 0
            ? $"Range: one pitch, {key.Spell(low.Pitch.MidiNumber)}."
            : $"Range: {key.Spell(low.Pitch.MidiNumber)} to {key.Spell(high.Pitch.MidiNumber)}, {WithArticle(IntervalName(span))} ({Count(span, "semitone", "semitones")}).";
        return new MotifFact("melody.range", FactLens.Melody, FactCertainty.Detected, text, "Measured over every note of the motif.")
        {
            Places = ExtremePlaces(notes, key),
        };
    }

    /// <summary>The lowest and the highest notes, each pitch as one place with every note on it; one place when every note has one pitch.</summary>
    private static FactPlace[] ExtremePlaces(Note[] notes, MusicalKey key)
    {
        int low = notes.Min(note => note.Pitch.MidiNumber);
        int high = notes.Max(note => note.Pitch.MidiNumber);
        return low == high
            ? [FactPlace.Of($"one pitch, {key.Spell(low)}", notes)]
            :
            [
                FactPlace.Of($"lowest, {key.Spell(low)}", notes.Where(note => note.Pitch.MidiNumber == low)),
                FactPlace.Of($"highest, {key.Spell(high)}", notes.Where(note => note.Pitch.MidiNumber == high)),
            ];
    }

    /// <summary>Each place where the line makes its largest leap, the two notes of the leap; none when the line has no leap.</summary>
    private static FactPlace[] LeapPlaces(Note[] line, int[] intervals, MusicalKey key)
    {
        if (!intervals.Any(step => Math.Abs(step) > 2))
        {
            return [];
        }

        int largest = intervals.OrderByDescending(Math.Abs).ThenByDescending(step => step).First();
        return [.. Enumerable.Range(0, intervals.Length)
            .Where(i => intervals[i] == largest)
            .Select(i => FactPlace.Of($"largest leap, {key.Spell(line[i].Pitch.MidiNumber)} to {key.Spell(line[i + 1].Pitch.MidiNumber)}", [line[i], line[i + 1]]))];
    }

    /// <summary>
    /// Where the contour starts, turns and ends: a turn is the note the line leaves in a new
    /// direction, a peak when it turns down and a low point when it turns up. None for one pitch.
    /// </summary>
    private static FactPlace[] ContourPlaces(Note[] line, ContourStep[] contour, MusicalKey key)
    {
        if (contour.All(step => step == ContourStep.Same))
        {
            return [];
        }

        var places = new List<FactPlace> { FactPlace.Of($"start, {key.Spell(line[0].Pitch.MidiNumber)}", [line[0]]) };
        ContourStep? direction = null;
        for (int i = 0; i < contour.Length; i++)
        {
            if (contour[i] == ContourStep.Same)
            {
                continue;
            }

            if (direction is not null && contour[i] != direction)
            {
                places.Add(FactPlace.Of($"{(contour[i] == ContourStep.Down ? "peak" : "low point")}, {key.Spell(line[i].Pitch.MidiNumber)}", [line[i]]));
            }

            direction = contour[i];
        }

        places.Add(FactPlace.Of($"end, {key.Spell(line[^1].Pitch.MidiNumber)}", [line[^1]]));
        return [.. places];
    }

    private static MotifFact DurationsFact(Note[] notes, bool offGrid)
    {
        var counts = new List<(long Ticks, string One, string Many, Note[] Notes)>();
        var other = new List<Note>();
        foreach (IGrouping<long, Note> group in notes.GroupBy(note => note.Span.Duration))
        {
            int index = Array.FindIndex(s_values, value => value.Ticks == group.Key);
            if (index < 0)
            {
                other.AddRange(group);
            }
            else
            {
                counts.Add((s_values[index].Ticks, s_values[index].One, s_values[index].Many, [.. group]));
            }
        }

        // Each length the text names is a place: the notes of that length.
        var ordered = counts
            .OrderByDescending(value => value.Notes.Length)
            .ThenByDescending(value => value.Ticks)
            .ToList();
        var parts = ordered.Select(value => Count(value.Notes.Length, value.One, value.Many)).ToList();
        var places = ordered.Select(value => FactPlace.Of(value.Notes.Length == 1 ? value.One : value.Many, value.Notes)).ToList();
        if (other.Count > 0)
        {
            parts.Add(Count(other.Count, "length that is no single note value", "lengths that are no single note value"));
            places.Add(FactPlace.Of("no single note value", other));
        }

        MotifFact fact = offGrid
            ? new MotifFact("rhythm.durations", FactLens.Rhythm, FactCertainty.Uncertain, $"Lengths: {string.Join(", ", parts)}.", "Onsets fall between grid lines, as in a played performance, so lengths may not be the written note values.")
            : new MotifFact("rhythm.durations", FactLens.Rhythm, FactCertainty.Detected, $"Lengths: {string.Join(", ", parts)}.", "Read from the notes' lengths; a tied note counts as one length.");
        return fact with { Places = places };
    }

    private static MotifFact GridFact(Note[] notes, Tick[] onsets, MeterMap meter)
    {
        int onBeat = onsets.Count(onset => OffsetInBar(meter, onset) % meter.SignatureAt(onset).BeatTicks == 0);
        int offGrid = 0;
        int finestStraight = -1;
        int finestTriplet = -1;
        foreach (Tick onset in onsets)
        {
            long offset = OffsetInBar(meter, onset);
            int index = Array.FindIndex(s_grids, grid => offset % grid.Ticks == 0);
            if (index < 0)
            {
                offGrid++;
            }
            else if (s_grids[index].Triplet)
            {
                finestTriplet = Math.Max(finestTriplet, index);
            }
            else
            {
                finestStraight = Math.Max(finestStraight, index);
            }
        }

        MetricPosition start = meter.ToPosition(onsets[0]);
        string startText = start.BeatOffset == 0
            ? $"It starts on beat {start.Beat} of bar {start.Bar}."
            : $"It starts after beat {start.Beat} of bar {start.Bar}, off the beat.";
        string beats = $"{onBeat} of {Count(onsets.Length, "onset", "onsets")} {(onBeat == 1 ? "falls" : "fall")} on a beat.";
        FactPlace startPlace = FactPlace.Of($"start, {Place(start)}", notes.Where(note => note.Span.Onset == onsets[0]));

        if (offGrid > 0)
        {
            return new MotifFact(
                "rhythm.grid",
                FactLens.Rhythm,
                FactCertainty.Uncertain,
                $"{offGrid} of {Count(onsets.Length, "onset", "onsets")} {(offGrid == 1 ? "falls" : "fall")} between thirty-second and triplet grid lines. {beats} {startText}",
                "Measured from the bar lines. Onsets off every grid usually mean a played performance whose timing was not quantised.")
            {
                Places = [FactPlace.Of("between grid lines", notes.Where(note => !s_grids.Any(grid => OffsetInBar(meter, note.Span.Onset) % grid.Ticks == 0))), startPlace],
            };
        }

        // A triplet grid covers straight onsets that fall on its lines, such as quarters on the eighth-note triplet grid.
        string grid = (finestStraight, finestTriplet) switch
        {
            ( < 0, _) => $"the {s_grids[finestTriplet].Name} grid",
            (_, < 0) => $"the {s_grids[finestStraight].Name} grid",
            _ when s_grids[finestStraight].Ticks % s_grids[finestTriplet].Ticks == 0 => $"the {s_grids[finestTriplet].Name} grid",
            _ => $"the {s_grids[finestStraight].Name} grid, with {s_grids[finestTriplet].Name}s",
        };
        return new MotifFact("rhythm.grid", FactLens.Rhythm, FactCertainty.Detected, $"Onsets fit {grid}. {beats} {startText}", "Measured from the bar lines.")
        {
            Places = [startPlace],
        };
    }

    private static MotifFact SilenceFact(Note[] notes, TickSpan span)
    {
        var gaps = new List<long>();
        var places = new List<FactPlace>();
        Tick soundingUntil = notes[0].Span.End;
        foreach (Note note in notes.Skip(1))
        {
            if (note.Span.Onset > soundingUntil)
            {
                gaps.Add(note.Span.Onset.Value - soundingUntil.Value);
                places.Add(new FactPlace($"silence, {LengthName(gaps[^1])}", TickSpan.FromBounds(soundingUntil, note.Span.Onset), []));
            }

            if (note.Span.End > soundingUntil)
            {
                soundingUntil = note.Span.End;
            }
        }

        string text = gaps.Count == 0
            ? "No silence between notes"
            : $"{Count(gaps.Count, "silence", "silences")} between notes: {string.Join(", ", gaps.Select(LengthName))}";
        long before = notes[0].Span.Onset.Value - span.Onset.Value;
        if (before > 0)
        {
            text += $"; the selection opens with {LengthName(before)} of silence";
            places.Add(new FactPlace($"opening silence, {LengthName(before)}", TickSpan.FromBounds(span.Onset, notes[0].Span.Onset), []));
        }

        return new MotifFact("rhythm.silences", FactLens.Rhythm, FactCertainty.Detected, $"{text}.", "Measured where no note of the motif sounds; rests in other voices or hands are not counted.")
        {
            Places = places,
        };
    }

    private static int OffGridCount(Note[] notes, MeterMap meter) =>
        notes.Count(note => !s_grids.Any(grid => OffsetInBar(meter, note.Span.Onset) % grid.Ticks == 0));

    /// <summary>Ticks from the bar line, counting a pickup from where a full bar would start.</summary>
    private static long OffsetInBar(MeterMap meter, Tick position)
    {
        Bar bar = meter.BarAt(position);
        return position.Value - bar.Span.Onset.Value + bar.LeadIn;
    }

    private static string LengthName(long ticks)
    {
        int index = Array.FindIndex(s_values, value => value.Ticks == ticks);
        return index < 0 ? "a length that is no single note value" : WithArticle(s_values[index].One);
    }

    private static int PitchClassCount(Note[] notes) => notes.Select(note => note.Pitch.PitchClass).Distinct().Count();

    private static string Accidentals(int fifths) => fifths switch
    {
        0 => "no sharps or flats",
        > 0 => Count(fifths, "sharp", "sharps"),
        _ => Count(-fifths, "flat", "flats"),
    };

    private static FactCertainty Weaker(FactCertainty a, FactCertainty b) => (FactCertainty)Math.Max((int)a, (int)b);

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    private static string WithArticle(string noun) => noun[0] switch
    {
        >= '0' and <= '9' => noun,
        'a' or 'e' or 'i' or 'o' or 'u' => "an " + noun,
        _ => "a " + noun,
    };

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}