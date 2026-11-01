using System.Globalization;
using System.Text;
using System.Xml;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// Writes notes from the shared model as notation (S1-W7, decision record 0008): how MIDI imports,
/// and later motifs and variations, are shown in the score view, whatever the input format.
/// </summary>
/// <remarks>
/// <para>
/// The steps, all deterministic: snap onsets and ends to a 32nd-note grid (counting every note that
/// moves); put each track on a staff, or on two for a piano part (by imported hands, or split at
/// middle C for a single track without hands); group notes that start and end together into chords;
/// give overlapping chords their own voice, up to four per staff, shortening a note only when all
/// four are busy; cut chords at bar lines and tie the pieces; write each piece as the longest note
/// value that fits and starts on a multiple of its own length or of the beat (the dotted quarter in
/// compound meters), and does not cross the middle of a 4/4 or 12/8 bar unless it starts the bar; beam eighths and shorter within the beat; spell pitches from the key signature
/// and write accidentals per bar and staff.
/// </para>
/// <para>
/// Tuplets, grace notes, pedal and dynamics are not written: triplets land on the nearest 32nd and
/// are counted as approximated. The notes themselves are never changed; only how they are written.
/// </para>
/// <para>
/// An excerpt writes only some bars, and ossias stand above the original bar for bar as smaller staves of the
/// same measures (decision record 0021).
/// </para>
/// </remarks>
public static class NotationGenerator
{
    /// <summary>The display grid: a 32nd note.</summary>
    public const long Grid = CanonicalTime.TicksPerQuarter / 8;

    public const int MaxVoicesPerStaff = 4;

    /// <summary>The size of an ossia's staves against the original's, as MEI writes it.</summary>
    public const string OssiaScale = "75%";

    private const int MiddleC = 60;

    /// <summary>How much earlier than the next note a key may be let go, still legato: 30 ticks, a 64th.</summary>
    private const long LegatoGap = CanonicalTime.TicksPerQuarter / 16;

    /// <summary>The longest step between two notes under one slur: a half note.</summary>
    private const long MostLegatoStep = 2 * CanonicalTime.TicksPerQuarter;

    private const string MeiNamespace = "http://www.music-encoding.org/ns/mei";

    // Note values from longest to shortest: ticks, MEI @dur, dots. The last three are only for
    // bars whose beats are shorter than the grid (x/64, x/128), so every bar can be filled.
    private static readonly (long Ticks, int Dur, int Dots)[] s_values =
    [
        (1920, 1, 0), (1440, 2, 1), (960, 2, 0), (720, 4, 1), (480, 4, 0), (360, 8, 1), (240, 8, 0),
        (180, 16, 1), (120, 16, 0), (60, 32, 0), (30, 64, 0), (15, 128, 0),
    ];

    private static readonly string[] s_stepNames = ["c", "d", "e", "f", "g", "a", "b"];

    /// <summary>The gap before the next onset that writing as heard closes: shorter than a quarter note.</summary>
    public const long HeldGap = CanonicalTime.TicksPerQuarter;

    /// <summary>The grid notes heard in a recording are written on: a 16th, or a 32nd where 16ths would merge two onsets.</summary>
    public const long HeardGrid = CanonicalTime.TicksPerQuarter / 4;

    /// <summary>Onsets this close are one chord when written as heard: a 64th.</summary>
    private const long HeardChord = Grid / 2;

    /// <summary>Notation for a MIDI import, with its key signatures.</summary>
    /// <param name="asHeard">See <see cref="Generate"/>.</param>
    public static GeneratedNotation FromMidi(MidiImport import, string? title = null, bool asHeard = false)
    {
        ArgumentNullException.ThrowIfNull(import);
        return Generate(import.Notes, import.Meter, import.KeySignatures.Select(key => new KeyChange(key.At, Math.Clamp(key.Sharps, -7, 7))), import.End, title, asHeard);
    }

    /// <summary>Notation for a MusicXML import, generated from its notes rather than engraved as written. Key signatures come from the first part.</summary>
    public static GeneratedNotation FromMusicXml(MusicXmlImport import, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(import);
        int firstTrack = import.KeySignatures.Count == 0 ? 0 : import.KeySignatures.Min(key => key.Track);
        IEnumerable<KeyChange> keys = import.KeySignatures
            .Where(key => key.Track == firstTrack)
            .Select(key => new KeyChange(key.At, Math.Clamp(key.Fifths, -7, 7)));
        return Generate(import.Notes, import.Meter, keys, import.End, title);
    }

    /// <param name="notes">The notes to write, in any order.</param>
    /// <param name="meter">The bars to write them in.</param>
    /// <param name="keys">Key signatures; C major until the first. A change takes effect at the next bar line.</param>
    /// <param name="end">The end of the piece; the last bar is the one that contains the end, or the last note's end if later.</param>
    /// <param name="title">The title written in the MEI header.</param>
    /// <param name="asHeard">
    /// For notes heard in a recording, written as a player reads them rather than as they sounded
    /// (decision record 0016): onsets on the nearest 16th (a 32nd where 16ths would merge two
    /// onsets, and onsets within a 64th are one chord); a note shorter than a 32nd just before the next
    /// as a grace note; and each note up to the next onset on its staff, so that the pedal or a key let
    /// go a little early does not write long ties, extra voices or short rests. A gap of a quarter or
    /// more stays a rest; a chord is written with one length. The notes themselves keep their timing.
    /// </param>
    /// <param name="playedTempos">
    /// For notes heard in a recording, written as played: the tempo that follows the playing. When
    /// given, the score is marked with how the notes were played, all of it inferred: dynamics from
    /// how loud the bars are, accents on notes clearly louder than those around them, "rit.",
    /// "accel." and "a tempo" where the bars slow down or speed up, hairpins where the melody grows
    /// louder or softer, and slurs where the melody's keys were held into the next note.
    /// </param>
    /// <param name="pedal">
    /// With <paramref name="playedTempos"/>: when the sustain pedal was held, written as "Ped." and
    /// "*" under the bottom staff.
    /// </param>
    /// <param name="bars">
    /// The bars to write, for an excerpt engraved on its own (decision record 0018, section 4; preparation item 10):
    /// the whole piece when null. Every note is still planned, so voices, ties and the key in force are the same
    /// as in the whole score; the bars keep their numbers.
    /// </param>
    /// <param name="ossias">
    /// Staves above the original, bar for bar, at three quarters of the size, each with its label at the left:
    /// a variation in place of the passage it changed, or the same notes written as played (item 10). Their
    /// staves are numbered first; their note ids carry the prefix <c>ossia-</c> (<c>ossia2-</c> for a second),
    /// so a note shown twice has two ids.
    /// </param>
    public static GeneratedNotation Generate(IEnumerable<Note> notes, MeterMap meter, IEnumerable<KeyChange> keys, Tick end, string? title = null, bool asHeard = false, IReadOnlyList<PlaybackTempo>? playedTempos = null, IEnumerable<TickSpan>? pedal = null, BarRange? bars = null, IReadOnlyList<NotationOssia>? ossias = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(keys);

        Note[] input = [.. notes];
        KeyChange[] keyChanges = [.. keys.OrderBy(key => key.At)];
        NotationOssia[] above = [.. ossias ?? []];
        Note[][] ossiaNotes = [.. above.Select((ossia, index) => ossia.Notes.Select(note => note with { Id = OssiaIdPrefix(index) + note.Id }).ToArray())];

        // The bars reach to the end given, or to the last note shown, in the original or an ossia.
        long last = Math.Max(1, Math.Max(end.Value, input.Concat(ossiaNotes.SelectMany(part => part))
            .Select(note => Math.Max(Snap(note.Span.End.Value), Snap(note.Span.Onset.Value) + Grid))
            .DefaultIfEmpty(0)
            .Max()));
        Bar[] allBars = [.. meter.BarsBefore(new Tick(last))];
        int firstIndex = 0, lastIndex = allBars.Length - 1;
        if (bars is { } range)
        {
            firstIndex = Array.FindIndex(allBars, bar => bar.Number >= range.FirstBar);
            lastIndex = Array.FindLastIndex(allBars, bar => bar.Number <= range.LastBar);
            if (firstIndex < 0 || lastIndex < firstIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(bars), $"The piece has bars {Number(allBars[0].Number)} to {Number(allBars[^1].Number)}.");
            }
        }

        var written = new WrittenBars(allBars, firstIndex, lastIndex);

        // The ossias stand above the original, so their staves come first.
        var parts = new List<PartPlan>();
        int nextStaff = 1;
        for (int index = 0; index < above.Length; index++)
        {
            PartPlan part = PlanPart(ossiaNotes[index], written, above[index].AsHeard, above[index].PlayedTempos, above[index].Pedal, nextStaff, ossia: true);
            parts.Add(part);
            nextStaff += part.Staves.Count;
        }

        PartPlan main = PlanPart(input, written, asHeard, playedTempos, pedal, nextStaff, ossia: false);
        parts.Add(main);

        var measures = new List<MeasurePlan>(written.Count);
        for (int index = firstIndex; index <= lastIndex; index++)
        {
            Bar bar = allBars[index];
            int fifths = KeyAt(keyChanges, bar.Span.Onset);
            measures.Add(new MeasurePlan(bar, fifths, [.. parts.Select(part => part.Measure(bar, index, fifths))]));
        }

        string mei = WriteMei(title ?? "Generated notation", parts, above, measures);
        return new GeneratedNotation(mei, main.StaffInfo, input.Length, main.Approximated, main.Shortened.Count, main.Heard.Held, main.Heard.Cut, main.Heard.Graces, asHeard)
        {
            AccentCount = main.Marks?.Accented.Count ?? 0,
            DynamicCount = main.Marks?.Dynamics.Count ?? 0,
            TempoWordCount = main.Marks?.TempoWords.Count ?? 0,
            HairpinCount = main.Marks?.Hairpins.Count ?? 0,
            TripletCount = main.Staves.Sum(staff => staff.Tuplets.Length),
            SlurCount = main.SlurCount,
            PedalCount = main.PedalCount,
            Excerpt = bars is null ? null : new BarRange(allBars[firstIndex].Number, allBars[lastIndex].Number),
            Ossias = [.. above.Select((ossia, index) => new GeneratedOssia(ossia.Label, parts[index].StaffInfo, ossiaNotes[index].Length, OssiaIdPrefix(index)))],
        };
    }

    /// <summary>The prefix of an ossia's note ids: the first ossia's "ossia-", the second's "ossia2-".</summary>
    public static string OssiaIdPrefix(int index) => index == 0 ? "ossia-" : $"ossia{Number(index + 1)}-";

    /// <summary>
    /// Plans one set of staves (the original, or an ossia) over the piece's bars: the staves, their voices,
    /// and the marks of the playing. Only the bars written get control events that end inside them. An ossia
    /// has only the staves that show notes in the bars written: a variation of the melody is one staff above the grand staff.
    /// </summary>
    private static PartPlan PlanPart(Note[] input, WrittenBars written, bool asHeard, IReadOnlyList<PlaybackTempo>? playedTempos, IEnumerable<TickSpan>? pedal, int firstStaff, bool ossia)
    {
        Bar[] bars = written.Bars;
        var shown = input.Select(note => new ShownNote(note, Snap(note.Span.Onset.Value), Snap(note.Span.End.Value))).ToArray();

        List<StaffPlan> planned = PlanStaves(shown, firstStaff);
        bool Shows(StaffPlan staff) => staff.Notes.Any(note => note.Onset < written.End && note.End > written.Start);
        if (ossia && planned.Count > 1 && planned.Any(Shows) && !planned.All(Shows))
        {
            planned = [.. planned.Where(Shows).Select((staff, index) => staff with { Number = firstStaff + index })];
        }

        // Beats whose onsets fit three or six to the beat better than 16ths are written as triplets.
        List<StaffPlan> staves = [.. planned.Select(staff => staff with { Tuplets = FindTuplets(staff.Notes, bars) })];
        var heard = new HeardCounts();
        if (asHeard)
        {
            staves = [.. staves.Select(staff => staff with { Notes = AsHeard(staff.Notes, heard, staff.Tuplets) })];
        }
        else
        {
            staves = [.. staves.Select(staff => staff with { Notes = OnTuplets(staff.Notes, staff.Tuplets) })];
        }

        int approximated = staves.SelectMany(staff => staff.Notes).Count(note => note.Onset != note.Note.Span.Onset.Value || note.End != note.Note.Span.End.Value);

        PlayingMarks? marks = playedTempos is null ? null : PlayingMarks.Of(input, bars, playedTempos);
        var shortened = new HashSet<string>();
        foreach (StaffPlan staff in staves)
        {
            staff.Voices = AssignVoices([.. staff.Notes.Where(note => !note.IsGrace)], shortened);
            AttachGraces(staff, [.. staff.Notes.Where(note => note.IsGrace)]);
            foreach (Chord chord in staff.Voices.SelectMany(voice => voice))
            {
                chord.IsAccented = marks is not null && chord.Notes.Any(note => marks.Accented.Contains(note.Note.Id));
            }
        }

        var controls = new ControlEvents[bars.Length];
        for (int index = 0; index < bars.Length; index++)
        {
            controls[index] = new ControlEvents();
        }

        int pedalCount = 0, slurCount = 0;
        if (marks is not null)
        {
            foreach ((long start, long stop, bool crescendo) in marks.Hairpins)
            {
                // Longer than a bar a hairpin is thin and hard to follow, so it is written as a word; so is one that ends beyond the bars written.
                int first = BarIndex(bars, start), final = BarIndex(bars, stop);
                bool word = stop - start > bars[first].Signature.BarTicks || !written.Holds(final);
                controls[first].Hairpins.Add(new HairpinPlan(start, crescendo, word ? null : $"{Number(final - first)}m+{Beat(bars[final], stop)}"));
            }

            foreach ((Chord from, Chord to, int staff) in Slurs(staves, bars))
            {
                // A slur to a chord beyond the bars written has nowhere to end.
                if (to.Onset >= written.End)
                {
                    continue;
                }

                controls[BarIndex(bars, from.Onset)].Slurs.Add((from, to, staff));
                slurCount++;
            }

            int bottom = staves.Max(staff => staff.Number);
            foreach (TickSpan held in (pedal ?? []).OrderBy(span => span.Onset))
            {
                long down = Snap(held.Onset.Value), up = Math.Min(Snap(held.End.Value), written.End);
                if (up - down < Grid || down >= written.End)
                {
                    continue;
                }

                controls[BarIndex(bars, down)].Pedals.Add((down, true, bottom));
                controls[BarIndex(bars, Math.Min(up, written.End - 1))].Pedals.Add((up, false, bottom));
                pedalCount++;
            }
        }

        return new PartPlan(staves, controls, marks, heard, shortened, approximated, slurCount, pedalCount);
    }

    /// <summary>The index of the bar that holds <paramref name="tick"/>: the last bar for a tick at or after the end.</summary>
    private static int BarIndex(Bar[] bars, long tick)
    {
        int low = 0, high = bars.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (bars[middle].Span.Onset.Value <= tick)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>A position in a bar as an MEI time stamp: 1 on the bar's first beat, counted in the bar's beats.</summary>
    private static string Beat(Bar bar, long at)
    {
        long origin = bar.IsPickup ? bar.Span.End.Value - bar.Signature.BarTicks : bar.Span.Onset.Value;
        return (1 + ((decimal)(at - origin) * bar.Signature.Denominator / (4 * CanonicalTime.TicksPerQuarter))).ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Legato on the top staff of each part: the melody (the highest note at each onset) under a slur
    /// wherever at least three of its notes follow each other with the key held to the next one
    /// (within <see cref="LegatoGap"/>), at most two bars to a slur, ending at a bar line.
    /// </summary>
    private static IEnumerable<(Chord From, Chord To, int Staff)> Slurs(List<StaffPlan> staves, Bar[] bars)
    {
        foreach (StaffPlan staff in staves.GroupBy(staff => staff.Track).Select(track => track.MinBy(staff => staff.Number)!))
        {
            Chord[] line = [.. staff.Voices.SelectMany(voice => voice)
                .GroupBy(chord => chord.Onset)
                .Select(group => group.MaxBy(chord => chord.Notes[^1].Note.Pitch.MidiNumber)!)
                .OrderBy(chord => chord.Onset)];
            int start = 0;
            for (int i = 1; i <= line.Length; i++)
            {
                bool joined = i < line.Length
                    && line[i].Onset - line[i - 1].Onset <= MostLegatoStep
                    && line[i - 1].Notes[^1].Note.Span.End.Value + LegatoGap >= line[i].Notes[^1].Note.Span.Onset.Value
                    && BarIndex(bars, line[i].Onset) - BarIndex(bars, line[start].Onset) < 2;
                if (joined)
                {
                    continue;
                }

                if (i - start >= 3)
                {
                    yield return (line[start], line[i - 1], staff.Number);
                }

                start = i;
            }
        }
    }

    private static long Snap(long tick) => (tick + (Grid / 2)) / Grid * Grid;

    private sealed class HeardCounts
    {
        public int Held { get; set; }

        public int Cut { get; set; }

        public int Graces { get; set; }
    }

    private static long SnapTo(long tick, long grid) => (tick + (grid / 2)) / grid * grid;

    /// <summary>A staff's notes written as heard (see <see cref="Generate"/>'s <c>asHeard</c>).</summary>
    private static ShownNote[] AsHeard(ShownNote[] notes, HeardCounts counts, TupletSpan[] tuplets)
    {
        // Onsets that start together are one chord.
        var chords = new List<List<ShownNote>>();
        foreach (ShownNote note in notes.OrderBy(note => note.Note.Span.Onset).ThenBy(note => note.Note.Pitch.MidiNumber).ThenBy(note => note.Note.Id, StringComparer.Ordinal))
        {
            if (chords.Count > 0 && note.Note.Span.Onset.Value - chords[^1][0].Note.Span.Onset.Value < HeardChord)
            {
                chords[^1].Add(note);
            }
            else
            {
                chords.Add([note]);
            }
        }

        // A single note shorter than a 32nd, with the next onset within a dotted 32nd, is a grace note.
        bool[] grace = new bool[chords.Count];
        for (int i = 0; i + 1 < chords.Count; i++)
        {
            Note note = chords[i][0].Note;
            grace[i] = chords[i].Count == 1
                && note.Span.Duration < Grid
                && chords[i + 1][0].Note.Span.Onset.Value - note.Span.Onset.Value < Grid * 3 / 2
                && (i == 0 || !grace[i - 1]);
        }

        long[] onsets = new long[chords.Count];
        long previous = -1;
        for (int i = 0; i < chords.Count; i++)
        {
            if (grace[i])
            {
                continue;
            }

            long raw = chords[i][0].Note.Span.Onset.Value;
            long onset = SnapIn(raw, tuplets, HeardGrid);
            onset = onset > previous ? onset : SnapIn(raw, tuplets, Grid);
            onset = onset > previous ? onset : previous + StepAt(previous, tuplets);
            onsets[i] = previous = onset;
        }

        var written = new List<ShownNote>(notes.Length);
        for (int i = 0; i < chords.Count; i++)
        {
            int next = Array.FindIndex(grace, i + 1, flag => !flag);
            if (grace[i])
            {
                // Played just before the next chord, written in front of it.
                counts.Graces++;
                written.Add(new ShownNote(chords[i][0].Note, onsets[next], onsets[next]) { IsGrace = true, Target = chords[next][0].Note.Id });
                continue;
            }

            // A chord is written with one length, that of its longest note, so that it stays one chord.
            long end = Math.Max(SnapIn(chords[i].Max(note => note.Note.Span.End.Value), tuplets, HeardGrid), onsets[i] + StepAt(onsets[i], tuplets));
            if (next >= 0 && end > onsets[next])
            {
                end = onsets[next];
                counts.Cut += chords[i].Count;
            }
            else if (next >= 0 && onsets[next] - end < HeldGap && end < onsets[next])
            {
                end = onsets[next];
                counts.Held += chords[i].Count;
            }

            written.AddRange(chords[i].Select(note => new ShownNote(note.Note, onsets[i], end)));
        }

        return [.. written];
    }

    /// <summary>
    /// The beats of a staff whose onsets are triplets: beats of a simple meter (not 6/8, 9/8, 12/8, which
    /// already divide by three) where the onsets, within a 12th of a beat early, lie on thirds of the beat
    /// (triplet eighths in a quarter) or sixths (triplet 16ths), at least one off the 16ths, with half the
    /// distance from that grid they have from the 16ths, and none further than a 12th of the beat from a
    /// third, or a 24th from a sixth, and no triplet value shorter than a 32nd. A beat with only two onsets
    /// counts only next to a beat of three or more, so that a dotted rhythm played a little short stays dotted.
    /// </summary>
    private static TupletSpan[] FindTuplets(ShownNote[] notes, Bar[] bars)
    {
        var onsets = new List<long>();
        foreach (long onset in notes.Where(note => !note.IsGrace).Select(note => note.Note.Span.Onset.Value).Order())
        {
            if (onsets.Count == 0 || onset - onsets[^1] >= HeardChord)
            {
                onsets.Add(onset);
            }
        }

        var candidates = new List<(TupletSpan Span, int Onsets)>();
        foreach (Bar bar in bars)
        {
            long beat = bar.Signature.BeatTicks;
            if (IsCompound(bar.Signature) || beat % 24 != 0)
            {
                continue;
            }

            long origin = bar.Span.Onset.Value - bar.LeadIn;
            for (long start = origin; start + beat <= bar.Span.End.Value; start += beat)
            {
                long early = beat / 12;
                int first = onsets.BinarySearch(start - early);
                first = first >= 0 ? first : ~first;
                long[] offsets = [.. onsets.Skip(first).TakeWhile(onset => onset < start + beat - early).Select(onset => onset - start)];
                if (offsets.Length < 2 || offsets.All(offset => offset < 0))
                {
                    continue;
                }

                // The distance to a grid, and the grid point, from the beat's start (an onset may be a little early).
                long Distance(long offset, long step) => Math.Abs(offset - Nearest(offset, step));
                long Nearest(long offset, long step) => SnapTo(offset + beat, step) - beat;
                long sixteenths = offsets.Sum(offset => Distance(offset, beat / 4));
                long thirds = offsets.Sum(offset => Distance(offset, beat / 3));
                long sixths = offsets.Sum(offset => Distance(offset, beat / 6));
                // A triplet value is never shorter than the 32nd every note is shown at least as.
                bool three = beat / 3 >= Grid
                    && 2 * thirds < sixteenths
                    && offsets.All(offset => Distance(offset, beat / 3) <= beat / 12)
                    && offsets.Any(offset => Nearest(offset, beat / 3) % (beat / 4) != 0);
                bool six = beat / 6 >= Grid
                    && 2 * sixths < sixteenths
                    && offsets.All(offset => Distance(offset, beat / 6) <= beat / 24)
                    && offsets.Any(offset => Nearest(offset, beat / 6) % (beat / 4) != 0);
                if (three && (!six || thirds <= sixths + (offsets.Length * beat / 48)))
                {
                    candidates.Add((new TupletSpan(start, beat, 3), offsets.Length));
                }
                else if (six)
                {
                    candidates.Add((new TupletSpan(start, beat, 6), offsets.Length));
                }
            }
        }

        var full = candidates.Where(candidate => candidate.Onsets >= 3).Select(candidate => candidate.Span.Start).ToHashSet();
        return [.. candidates
            .Where(candidate => candidate.Onsets >= 3 || full.Contains(candidate.Span.Start - candidate.Span.Length) || full.Contains(candidate.Span.End))
            .Select(candidate => candidate.Span)];
    }

    /// <summary>The triplet beat whose onsets a tick goes with: from a 12th of the beat before it to a 12th before its end.</summary>
    private static TupletSpan? TupletAround(long tick, TupletSpan[] tuplets)
    {
        foreach (TupletSpan span in tuplets)
        {
            if (tick >= span.Start - (span.Length / 12) && tick < span.End - (span.Length / 12))
            {
                return span;
            }
        }

        return null;
    }

    /// <summary>
    /// A tick on its triplet beat's grid, or on <paramref name="grid"/> outside triplets; a tick just before a
    /// triplet beat's end that <paramref name="grid"/> would put inside it goes on the triplets' grid too.
    /// </summary>
    private static long SnapIn(long tick, TupletSpan[] tuplets, long grid)
    {
        long snapped = SnapTo(tick, grid);
        TupletSpan? around = TupletAround(tick, tuplets) ?? (tuplets.FirstOrDefault(span => span.Start < snapped && snapped < span.End) is { Length: > 0 } inside ? inside : null);
        return around is { } span ? span.Start + SnapTo(tick - span.Start + span.Length, span.Step) - span.Length : snapped;
    }

    /// <summary>The shortest value written at a position: a step of the triplet beat it is in, or a 32nd.</summary>
    private static long StepAt(long tick, TupletSpan[] tuplets) =>
        tuplets.FirstOrDefault(span => span.Start <= tick && tick < span.End) is { Length: > 0 } span ? span.Step : Grid;

    /// <summary>Notes written on the 32nd grid, with those in triplet beats moved to the triplets' grid.</summary>
    private static ShownNote[] OnTuplets(ShownNote[] notes, TupletSpan[] tuplets)
    {
        if (tuplets.Length == 0)
        {
            return notes;
        }

        return [.. notes.Select(note =>
        {
            long onset = SnapIn(note.Note.Span.Onset.Value, tuplets, Grid);
            long end = Math.Max(SnapIn(note.Note.Span.End.Value, tuplets, Grid), onset + StepAt(onset, tuplets));
            return note with { Onset = onset, RawEnd = end };
        })];
    }

    /// <summary>Each grace note in front of the chord that holds the note it leads to.</summary>
    private static void AttachGraces(StaffPlan staff, ShownNote[] graces)
    {
        foreach (ShownNote grace in graces)
        {
            Chord chord = staff.Voices.SelectMany(voice => voice).First(chord => chord.Notes.Any(note => note.Note.Id == grace.Target));
            chord.Graces.Add(new Chord(chord.Onset, chord.Onset, [grace]));
        }
    }

    private static int KeyAt(KeyChange[] keys, Tick position)
    {
        int fifths = 0;
        foreach (KeyChange key in keys)
        {
            if (key.At > position)
            {
                break;
            }

            fifths = key.Fifths;
        }

        return fifths;
    }

    /// <summary>The staves for the notes, numbered from <paramref name="firstStaff"/> down.</summary>
    private static List<StaffPlan> PlanStaves(ShownNote[] notes, int firstStaff)
    {
        int[] tracks = [.. notes.Select(note => note.Note.Track).Distinct().Order()];
        var staves = new List<StaffPlan>();
        if (tracks.Length == 0)
        {
            staves.Add(new StaffPlan(firstStaff, 0, Clef.Treble, null, false, []));
            return staves;
        }

        foreach (int track in tracks)
        {
            ShownNote[] trackNotes = [.. notes.Where(note => note.Note.Track == track)];
            bool hasHands = trackNotes.Any(note => note.Note.Hand is not null);
            bool spansBothClefs = trackNotes.Any(note => note.Note.Pitch.MidiNumber < MiddleC - 5)
                && trackNotes.Any(note => note.Note.Pitch.MidiNumber > MiddleC + 5);

            if (hasHands || (tracks.Length == 1 && spansBothClefs))
            {
                static bool IsRight(ShownNote note) => note.Note.Hand?.Value is { } hand ? hand == Hand.Right : note.Note.Pitch.MidiNumber >= MiddleC;
                staves.Add(new StaffPlan(firstStaff + staves.Count, track, Clef.Treble, Hand.Right, !hasHands, [.. trackNotes.Where(IsRight)]));
                staves.Add(new StaffPlan(firstStaff + staves.Count, track, Clef.Bass, Hand.Left, !hasHands, [.. trackNotes.Where(note => !IsRight(note))]));
            }
            else
            {
                int high = trackNotes.Count(note => note.Note.Pitch.MidiNumber >= MiddleC);
                Clef clef = high >= trackNotes.Length - high ? Clef.Treble : Clef.Bass;
                staves.Add(new StaffPlan(firstStaff + staves.Count, track, clef, null, false, trackNotes));
            }
        }

        return staves;
    }

    /// <summary>Chords in voices that never overlap, the highest voice first.</summary>
    private static List<List<Chord>> AssignVoices(ShownNote[] notes, HashSet<string> shortened)
    {
        Chord[] chords = [.. notes
            .GroupBy(note => (note.Onset, note.End))
            .Select(group => new Chord(group.Key.Onset, group.Key.End, [.. group.OrderBy(note => note.Note.Pitch.MidiNumber).ThenBy(note => note.Note.Id, StringComparer.Ordinal)]))
            .OrderBy(chord => chord.Onset)
            .ThenByDescending(chord => chord.Notes[^1].Note.Pitch.MidiNumber)
            .ThenByDescending(chord => chord.End)];

        var voices = new List<List<Chord>>();
        foreach (Chord chord in chords)
        {
            List<Chord>? voice = voices.FirstOrDefault(candidate => candidate[^1].End <= chord.Onset);
            if (voice is null && voices.Count < MaxVoicesPerStaff)
            {
                voice = [];
                voices.Add(voice);
            }
            else if (voice is null)
            {
                // Every voice is busy: shorten the chord that ends first, or join it if it starts here too.
                voice = voices.MinBy(candidate => candidate[^1].End)!;
                Chord previous = voice[^1];
                if (previous.Onset == chord.Onset)
                {
                    long joinedEnd = Math.Min(previous.End, chord.End);
                    foreach (ShownNote note in previous.Notes.Concat(chord.Notes).Where(note => note.End > joinedEnd))
                    {
                        shortened.Add(note.Note.Id);
                    }

                    previous.Notes = [.. previous.Notes.Concat(chord.Notes).OrderBy(note => note.Note.Pitch.MidiNumber).ThenBy(note => note.Note.Id, StringComparer.Ordinal)];
                    previous.End = joinedEnd;
                    continue;
                }

                foreach (ShownNote note in previous.Notes)
                {
                    shortened.Add(note.Note.Id);
                }

                previous.End = chord.Onset;
            }

            voice.Add(chord);
        }

        return [.. voices.OrderByDescending(voice => voice.SelectMany(chord => chord.Notes).Average(note => note.Note.Pitch.MidiNumber))];
    }

    private static StaffMeasure LayOutStaff(StaffPlan staff, Bar bar, int fifths)
    {
        var grid = new BarGrid(bar.IsPickup ? bar.Span.End.Value - bar.Signature.BarTicks : bar.Span.Onset.Value, Pulse(bar.Signature), Middle(bar.Signature));
        long beamGroup = BeamGroup(bar.Signature);
        long start = bar.Span.Onset.Value;
        long stop = bar.Span.End.Value;

        var layers = new List<List<Element>>();
        for (int index = 0; index < Math.Max(1, staff.Voices.Count); index++)
        {
            Chord[] inBar = index < staff.Voices.Count
                ? [.. staff.Voices[index].Where(chord => chord.Onset < stop && chord.End > start)]
                : [];
            if (inBar.Length == 0)
            {
                // Voices leave empty bars out; the top voice rests for the whole bar when no voice plays.
                if (index == 0 && staff.Voices.All(voice => !voice.Any(chord => chord.Onset < stop && chord.End > start)))
                {
                    layers.Add(bar.IsComplete && !bar.IsPickup ? [new MeasureRest()] : [.. Fill(start, stop, grid, space: false)]);
                }
                else
                {
                    layers.Add([]);
                }

                continue;
            }

            // Triplets are written in a voice only where it starts or ends a note between the 16ths.
            TupletSpan[] tuplets = [.. staff.Tuplets.Where(span => span.Start >= start && span.End <= stop && inBar.Any(chord =>
                (chord.Onset > span.Start && chord.Onset < span.End && (chord.Onset - span.Start) % (span.Length / 4) != 0)
                || (chord.End > span.Start && chord.End < span.End && (chord.End - span.Start) % (span.Length / 4) != 0)))];
            var elements = new List<Element>();
            long cursor = start;
            foreach (Chord chord in inBar)
            {
                long from = Math.Max(chord.Onset, start);
                long to = Math.Min(chord.End, stop);
                elements.AddRange(Fill(cursor, from, grid, space: index > 0, tuplets));
                if (from == chord.Onset)
                {
                    TupletSpan? around = tuplets.FirstOrDefault(span => span.Start <= from && from < span.End) is { Length: > 0 } found ? found : null;
                    elements.AddRange(chord.Graces.Select(grace => new ChordPiece(from, 0, 8, 0, grace, 0, tieIn: false, tieOut: false) { IsGrace = true, Tuplet = around }));
                }

                foreach ((long pieceStart, long ticks, int dur, int dots, TupletSpan? tuplet) in Pieces(from, to, grid, tuplets))
                {
                    elements.Add(new ChordPiece(pieceStart, ticks, dur, dots, chord, chord.NextPiece++, pieceStart > chord.Onset, pieceStart + ticks < chord.End) { Tuplet = tuplet });
                }

                cursor = to;
            }

            elements.AddRange(Fill(cursor, stop, grid, space: index > 0, tuplets));
            layers.Add(Group(elements, grid.Origin, beamGroup));
        }

        WriteAccidentals(layers, fifths);
        return new StaffMeasure(layers);
    }

    /// <summary>The span a note value has to start on a multiple of: the beat, or three beats in compound meters (6/8, 9/8, 12/16).</summary>
    private static long Pulse(TimeSignature signature) =>
        IsCompound(signature) ? 3 * signature.BeatTicks : signature.BeatTicks;

    /// <summary>
    /// The middle of a bar of four beats or pulses (4/4, 12/8): only a value that starts the bar may
    /// cross it, so the second half of the bar stays visible. Zero for other meters.
    /// </summary>
    private static long Middle(TimeSignature signature)
    {
        int pulses = IsCompound(signature) ? signature.Numerator / 3 : signature.Numerator;
        return pulses >= 4 && pulses % 2 == 0 ? signature.BarTicks / 2 : 0;
    }

    /// <summary>The span eighths and shorter are beamed within.</summary>
    private static long BeamGroup(TimeSignature signature)
    {
        if (IsCompound(signature))
        {
            return 3 * signature.BeatTicks;
        }

        if (signature.BeatTicks >= CanonicalTime.TicksPerQuarter)
        {
            return signature.BeatTicks;
        }

        // 2/8 and 3/8 beam the whole bar; 5/8 and 7/8 by the quarter.
        return signature.Numerator <= 3 ? signature.BarTicks : CanonicalTime.TicksPerQuarter;
    }

    private static bool IsCompound(TimeSignature signature) =>
        signature.Denominator >= 8 && signature.Numerator > 3 && signature.Numerator % 3 == 0;

    private static IEnumerable<(long Start, long Ticks, int Dur, int Dots)> Decompose(long from, long to, BarGrid grid)
    {
        for (long position = from; position < to;)
        {
            long offset = position - grid.Origin;
            (long ticks, int dur, int dots) = s_values.FirstOrDefault(value => value.Ticks <= to - position
                && offset % Math.Min(value.Ticks, grid.Pulse) == 0
                && (grid.Middle == 0 || offset == 0 || offset >= grid.Middle || offset + value.Ticks <= grid.Middle));
            if (ticks == 0)
            {
                throw new InvalidOperationException($"No note value fits at {position}t before {to}t.");
            }

            yield return (position, ticks, dur, dots);
            position += ticks;
        }
    }

    private static IEnumerable<Element> Fill(long from, long to, BarGrid grid, bool space) =>
        Decompose(from, to, grid).Select(value => new Rest(value.Start, value.Ticks, value.Dur, value.Dots, space));

    private static IEnumerable<Element> Fill(long from, long to, BarGrid grid, bool space, TupletSpan[] tuplets) =>
        Pieces(from, to, grid, tuplets).Select(value => new Rest(value.Start, value.Ticks, value.Dur, value.Dots, space) { Tuplet = value.Tuplet });

    /// <summary>
    /// Note values from one tick to another, as <see cref="Decompose"/> writes them, except inside a triplet
    /// beat: there, three values take the time of two, so each is written as its length times 3/2.
    /// </summary>
    private static IEnumerable<(long Start, long Ticks, int Dur, int Dots, TupletSpan? Tuplet)> Pieces(long from, long to, BarGrid grid, TupletSpan[] tuplets)
    {
        for (long position = from; position < to;)
        {
            if (tuplets.FirstOrDefault(span => span.Start <= position && position < span.End) is { Length: > 0 } span)
            {
                long stop = Math.Min(to, span.End);
                var written = new BarGrid(0, span.Step * 3 / 2, 0);
                foreach ((long start, long ticks, int dur, int dots) in Decompose((position - span.Start) * 3 / 2, (stop - span.Start) * 3 / 2, written))
                {
                    yield return (span.Start + (start * 2 / 3), ticks * 2 / 3, dur, dots, span);
                }

                position = stop;
            }
            else
            {
                long stop = Math.Min(to, tuplets.Where(next => next.Start > position).Select(next => next.Start).DefaultIfEmpty(to).Min());
                foreach ((long start, long ticks, int dur, int dots) in Decompose(position, stop, grid))
                {
                    yield return (start, ticks, dur, dots, null);
                }

                position = stop;
            }
        }
    }

    /// <summary>The elements of a layer with each triplet beat's in a tuplet, and eighths and shorter beamed.</summary>
    private static List<Element> Group(List<Element> elements, long origin, long beamGroup)
    {
        var result = new List<Element>();
        for (int i = 0; i < elements.Count;)
        {
            TupletSpan? tuplet = elements[i].Tuplet;
            int next = i;
            while (next < elements.Count && elements[next].Tuplet == tuplet)
            {
                next++;
            }

            List<Element> run = elements[i..next];
            if (tuplet is { } span)
            {
                result.Add(new TupletGroup([.. Beam(run, span.Start, span.Length)], span));
            }
            else
            {
                result.AddRange(Beam(run, origin, beamGroup));
            }

            i = next;
        }

        return result;
    }

    private static List<Element> Beam(List<Element> elements, long origin, long beamGroup)
    {
        var result = new List<Element>();
        var group = new List<Element>();
        long groupIndex = -1;

        void Flush()
        {
            if (group.Count >= 2)
            {
                result.Add(new Beamed([.. group]));
            }
            else
            {
                result.AddRange(group);
            }

            group.Clear();
        }

        foreach (Element element in elements)
        {
            long first = (element.Start - origin) / beamGroup;
            long lastTick = (element.Start + element.Ticks - 1 - origin) / beamGroup;
            bool beamable = element is ChordPiece { IsGrace: false } && element.Dur >= 8 && first == lastTick;
            if (!beamable || first != groupIndex)
            {
                Flush();
            }

            if (beamable)
            {
                group.Add(element);
                groupIndex = first;
            }
            else
            {
                result.Add(element);
                groupIndex = -1;
            }
        }

        Flush();
        return result;
    }

    /// <summary>Accidentals per bar and staff, in time order across voices; tied continuations carry theirs silently.</summary>
    private static void WriteAccidentals(List<List<Element>> layers, int fifths)
    {
        int[] key = PitchSpeller.KeyAlterations(fifths);
        var current = new Dictionary<(int Step, int Octave), int>();
        IEnumerable<ChordPiece> pieces = layers
            .SelectMany((layer, index) => Flatten(layer).OfType<ChordPiece>().Select(piece => (piece, index)))
            .OrderBy(entry => entry.piece.Start)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.piece);

        foreach (ChordPiece piece in pieces)
        {
            piece.Spellings = [.. piece.Chord.Notes.Select(note => PitchSpeller.Spell(note.Note.Pitch.MidiNumber, fifths))];
            piece.ShowAccidental = new bool[piece.Spellings.Length];
            for (int i = 0; i < piece.Spellings.Length; i++)
            {
                SpelledPitch spelled = piece.Spellings[i];
                if (piece.TieIn)
                {
                    continue;
                }

                int inEffect = current.TryGetValue((spelled.Step, spelled.Octave), out int alter) ? alter : key[spelled.Step];
                if (spelled.Alter != inEffect)
                {
                    piece.ShowAccidental[i] = true;
                    current[(spelled.Step, spelled.Octave)] = spelled.Alter;
                }
            }
        }
    }

    private static IEnumerable<Element> Flatten(IEnumerable<Element> elements) =>
        elements.SelectMany(element => element switch
        {
            Beamed beam => Flatten(beam.Elements),
            TupletGroup tuplet => Flatten(tuplet.Elements),
            _ => [element],
        });

    private static string WriteMei(string title, List<PartPlan> parts, NotationOssia[] ossias, List<MeasurePlan> measures)
    {
        // A StringBuilder would make the writer declare UTF-16; the document is handed on as UTF-8 text.
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
        using (var xml = XmlWriter.Create(builder, settings))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("mei", MeiNamespace);
            xml.WriteAttributeString("meiversion", "5.1");

            xml.WriteStartElement("meiHead", MeiNamespace);
            xml.WriteStartElement("fileDesc", MeiNamespace);
            xml.WriteStartElement("titleStmt", MeiNamespace);
            xml.WriteElementString("title", MeiNamespace, title);
            xml.WriteEndElement();
            xml.WriteElementString("pubStmt", MeiNamespace, string.Empty);
            xml.WriteEndElement();
            xml.WriteEndElement();

            xml.WriteStartElement("music", MeiNamespace);
            xml.WriteStartElement("body", MeiNamespace);
            xml.WriteStartElement("mdiv", MeiNamespace);
            xml.WriteStartElement("score", MeiNamespace);

            MeasurePlan first = measures[0];
            xml.WriteStartElement("scoreDef", MeiNamespace);
            WriteMeter(xml, first.Bar.Signature);
            xml.WriteAttributeString("keysig", KeySig(first.Fifths));
            xml.WriteStartElement("staffGrp", MeiNamespace);
            for (int index = 0; index < ossias.Length; index++)
            {
                // An ossia is a group of its own with the label at its left, above the original.
                xml.WriteStartElement("staffGrp", MeiNamespace);
                xml.WriteElementString("label", MeiNamespace, ossias[index].Label);
                WriteStaffGroups(xml, parts[index].Staves, OssiaScale);
                xml.WriteEndElement();
            }

            WriteStaffGroups(xml, parts[^1].Staves, null);
            xml.WriteEndElement();
            xml.WriteEndElement();

            xml.WriteStartElement("section", MeiNamespace);
            MeasurePlan? previous = null;
            foreach (MeasurePlan measure in measures)
            {
                bool meterChanged = previous is not null && previous.Bar.Signature != measure.Bar.Signature;
                bool keyChanged = previous is not null && previous.Fifths != measure.Fifths;
                if (meterChanged || keyChanged)
                {
                    xml.WriteStartElement("scoreDef", MeiNamespace);
                    if (meterChanged)
                    {
                        WriteMeter(xml, measure.Bar.Signature);
                    }

                    if (keyChanged)
                    {
                        xml.WriteAttributeString("keysig", KeySig(measure.Fifths));
                    }

                    xml.WriteEndElement();
                }

                WriteMeasure(xml, measure);
                previous = measure;
            }

            xml.WriteEndDocument();
        }

        return builder.ToString();
    }

    private static void WriteMeter(XmlWriter xml, TimeSignature signature)
    {
        xml.WriteAttributeString("meter.count", Number(signature.Numerator));
        xml.WriteAttributeString("meter.unit", Number(signature.Denominator));
    }

    private static string KeySig(int fifths) => fifths switch
    {
        0 => "0",
        > 0 => $"{Number(fifths)}s",
        _ => $"{Number(-fifths)}f",
    };

    /// <summary>The staves of one part, a braced pair for a piano part, each at <paramref name="scale"/> when given.</summary>
    private static void WriteStaffGroups(XmlWriter xml, List<StaffPlan> staves, string? scale)
    {
        foreach (IGrouping<int, StaffPlan> track in staves.GroupBy(staff => staff.Track))
        {
            bool grand = track.Count() > 1;
            if (grand)
            {
                xml.WriteStartElement("staffGrp", MeiNamespace);
                xml.WriteAttributeString("symbol", "brace");
                xml.WriteAttributeString("bar.thru", "true");
            }

            foreach (StaffPlan staff in track)
            {
                xml.WriteStartElement("staffDef", MeiNamespace);
                xml.WriteAttributeString("n", Number(staff.Number));
                xml.WriteAttributeString("lines", "5");
                xml.WriteAttributeString("clef.shape", staff.Clef == Clef.Treble ? "G" : "F");
                xml.WriteAttributeString("clef.line", staff.Clef == Clef.Treble ? "2" : "4");
                if (scale is not null)
                {
                    xml.WriteAttributeString("scale", scale);
                }

                xml.WriteEndElement();
            }

            if (grand)
            {
                xml.WriteEndElement();
            }
        }
    }

    private static void WriteMeasure(XmlWriter xml, MeasurePlan measure)
    {
        xml.WriteStartElement("measure", MeiNamespace);
        xml.WriteAttributeString("n", Number(measure.Bar.Number));
        if (!measure.Bar.IsComplete || measure.Bar.IsPickup)
        {
            xml.WriteAttributeString("metcon", "false");
        }

        foreach (PartMeasure part in measure.Parts)
        {
            for (int staff = 0; staff < part.Staves.Length; staff++)
            {
                xml.WriteStartElement("staff", MeiNamespace);
                xml.WriteAttributeString("n", Number(part.FirstStaff + staff));
                List<List<Element>> layers = part.Staves[staff].Layers;
                for (int layer = 0; layer < layers.Count; layer++)
                {
                    if (layers[layer].Count == 0)
                    {
                        continue;
                    }

                    xml.WriteStartElement("layer", MeiNamespace);
                    xml.WriteAttributeString("n", Number(layer + 1));
                    foreach (Element element in layers[layer])
                    {
                        WriteElement(xml, element);
                    }

                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
            }
        }

        foreach (PartMeasure part in measure.Parts)
        {
            WriteDirections(xml, measure.Bar, part);
        }

        xml.WriteEndElement();
    }

    /// <summary>A part's words, dynamics, hairpins, slurs and pedal marks in a bar, on the part's own staves.</summary>
    private static void WriteDirections(XmlWriter xml, Bar bar, PartMeasure part)
    {
        long origin = bar.IsPickup ? bar.Span.End.Value - bar.Signature.BarTicks : bar.Span.Onset.Value;
        string top = Number(part.FirstStaff);
        string Beat(long at) => NotationGenerator.Beat(bar, at);
        if (part.TempoWord is { } word)
        {
            xml.WriteStartElement("dir", MeiNamespace);
            xml.WriteAttributeString("staff", top);
            xml.WriteAttributeString("place", "above");
            xml.WriteAttributeString("tstamp", Beat(Math.Max(origin, bar.Span.Onset.Value)));
            xml.WriteStartElement("rend", MeiNamespace);
            xml.WriteAttributeString("fontstyle", "italic");
            xml.WriteString(word);
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        if (part.Dynamic is { } dynamic)
        {
            xml.WriteStartElement("dynam", MeiNamespace);
            xml.WriteAttributeString("staff", top);
            xml.WriteAttributeString("place", "below");
            xml.WriteAttributeString("tstamp", Beat(dynamic.At));
            xml.WriteString(dynamic.Level);
            xml.WriteEndElement();
        }

        foreach (HairpinPlan hairpin in part.Controls.Hairpins.Where(hairpin => hairpin.Tstamp2 is null))
        {
            xml.WriteStartElement("dir", MeiNamespace);
            xml.WriteAttributeString("staff", top);
            xml.WriteAttributeString("place", "below");
            xml.WriteAttributeString("tstamp", Beat(hairpin.At));
            xml.WriteStartElement("rend", MeiNamespace);
            xml.WriteAttributeString("fontstyle", "italic");
            xml.WriteString(hairpin.Crescendo ? "cresc." : "dim.");
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        foreach (HairpinPlan hairpin in part.Controls.Hairpins.Where(hairpin => hairpin.Tstamp2 is not null))
        {
            xml.WriteStartElement("hairpin", MeiNamespace);
            xml.WriteAttributeString("form", hairpin.Crescendo ? "cres" : "dim");
            xml.WriteAttributeString("staff", top);
            xml.WriteAttributeString("place", "below");
            xml.WriteAttributeString("tstamp", Beat(hairpin.At));
            xml.WriteAttributeString("tstamp2", hairpin.Tstamp2!);
            xml.WriteEndElement();
        }

        foreach ((Chord from, Chord to, int staff) in part.Controls.Slurs)
        {
            xml.WriteStartElement("slur", MeiNamespace);
            xml.WriteAttributeString("staff", Number(staff));
            xml.WriteAttributeString("startid", "#" + ChordId(from));
            xml.WriteAttributeString("endid", "#" + ChordId(to));
            xml.WriteEndElement();
        }

        foreach ((long at, bool down, int staff) in part.Controls.Pedals)
        {
            xml.WriteStartElement("pedal", MeiNamespace);
            xml.WriteAttributeString("staff", Number(staff));
            xml.WriteAttributeString("place", "below");
            xml.WriteAttributeString("tstamp", Beat(at));
            xml.WriteAttributeString("dir", down ? "down" : "up");
            xml.WriteEndElement();
        }
    }

    /// <summary>The id of a chord's first piece, as <see cref="WriteElement"/> writes it.</summary>
    private static string ChordId(Chord chord) =>
        XmlConvert.EncodeLocalName(chord.Notes.Length == 1 ? chord.Notes[0].Note.Id : "c_" + chord.Notes[0].Note.Id)!;

    private static void WriteAccent(XmlWriter xml, ChordPiece piece)
    {
        if (piece.Chord.IsAccented && piece.Index == 0 && !piece.IsGrace)
        {
            xml.WriteStartElement("artic", MeiNamespace);
            xml.WriteAttributeString("artic", "acc");
            xml.WriteEndElement();
        }
    }

    private static void WriteElement(XmlWriter xml, Element element)
    {
        switch (element)
        {
            case MeasureRest:
                xml.WriteStartElement("mRest", MeiNamespace);
                xml.WriteEndElement();
                break;

            case Rest rest:
                xml.WriteStartElement(rest.IsSpace ? "space" : "rest", MeiNamespace);
                WriteDuration(xml, rest);
                xml.WriteEndElement();
                break;

            case TupletGroup tuplet:
                xml.WriteStartElement("tuplet", MeiNamespace);
                xml.WriteAttributeString("num", Number(tuplet.Span.Num));
                xml.WriteAttributeString("numbase", Number(tuplet.Span.Num * 2 / 3));
                xml.WriteAttributeString("num.format", "count");
                foreach (Element inner in tuplet.Elements)
                {
                    WriteElement(xml, inner);
                }

                xml.WriteEndElement();
                break;

            case Beamed beam:
                xml.WriteStartElement("beam", MeiNamespace);
                foreach (Element inner in beam.Elements)
                {
                    WriteElement(xml, inner);
                }

                xml.WriteEndElement();
                break;

            case ChordPiece piece when piece.Chord.Notes.Length == 1:
                xml.WriteStartElement("note", MeiNamespace);
                WriteId(xml, piece.Chord.Notes[0].Note.Id, piece.Index);
                if (piece.IsGrace)
                {
                    xml.WriteAttributeString("grace", "acc");
                }

                WriteDuration(xml, piece);
                WritePitch(xml, piece, 0);
                WriteAccent(xml, piece);
                xml.WriteEndElement();
                break;

            case ChordPiece piece:
                xml.WriteStartElement("chord", MeiNamespace);
                WriteId(xml, "c_" + piece.Chord.Notes[0].Note.Id, piece.Index);
                WriteDuration(xml, piece);
                WriteAccent(xml, piece);
                for (int i = 0; i < piece.Chord.Notes.Length; i++)
                {
                    xml.WriteStartElement("note", MeiNamespace);
                    WriteId(xml, piece.Chord.Notes[i].Note.Id, piece.Index);
                    WritePitch(xml, piece, i);
                    xml.WriteEndElement();
                }

                xml.WriteEndElement();
                break;

            default:
                throw new InvalidOperationException($"Unknown element {element.GetType().Name}.");
        }
    }

    private static void WriteId(XmlWriter xml, string id, int piece)
    {
        string name = XmlConvert.EncodeLocalName(id)!;
        xml.WriteAttributeString("xml", "id", null, piece == 0 ? name : $"{name}-{Number(piece + 1)}");
    }

    private static void WriteDuration(XmlWriter xml, Element element)
    {
        xml.WriteAttributeString("dur", Number(element.Dur));
        if (element.Dots > 0)
        {
            xml.WriteAttributeString("dots", Number(element.Dots));
        }
    }

    private static void WritePitch(XmlWriter xml, ChordPiece piece, int index)
    {
        SpelledPitch spelled = piece.Spellings[index];
        xml.WriteAttributeString("pname", s_stepNames[spelled.Step]);
        xml.WriteAttributeString("oct", Number(spelled.Octave));
        if (piece.ShowAccidental[index])
        {
            xml.WriteAttributeString("accid", Accidental(spelled.Alter));
        }
        else if (spelled.Alter != 0)
        {
            xml.WriteAttributeString("accid.ges", Accidental(spelled.Alter));
        }

        if (piece.TieIn || piece.TieOut)
        {
            xml.WriteAttributeString("tie", (piece.TieIn, piece.TieOut) switch
            {
                (false, true) => "i",
                (true, true) => "m",
                _ => "t",
            });
        }
    }

    private static string Accidental(int alter) => alter switch
    {
        -1 => "f",
        0 => "n",
        1 => "s",
        _ => throw new InvalidOperationException($"No accidental for an alteration of {alter}."),
    };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record ShownNote(Note Note, long Onset, long RawEnd)
    {
        /// <summary>At least one grid step after the onset, so a very short note still shows; a grace note takes no time.</summary>
        public long End => IsGrace ? Onset : Math.Max(RawEnd, Onset + Grid);

        /// <summary>Written as a grace note in front of the chord that holds <see cref="Target"/>.</summary>
        public bool IsGrace { get; init; }

        public string? Target { get; init; }
    }

    private sealed class Chord(long onset, long end, ShownNote[] notes)
    {
        public long Onset { get; } = onset;

        public long End { get; set; } = end;

        /// <summary>Lowest pitch first.</summary>
        public ShownNote[] Notes { get; set; } = notes;

        /// <summary>The piece number the next bar's piece of this chord gets, for its id.</summary>
        public int NextPiece { get; set; }

        /// <summary>Grace notes written in front of the chord, in the order played.</summary>
        public List<Chord> Graces { get; } = [];

        /// <summary>Played clearly louder than the notes around it.</summary>
        public bool IsAccented { get; set; }
    }

    private sealed record StaffPlan(int Number, int Track, Clef Clef, Hand? Hand, bool IsSplitInferred, ShownNote[] Notes)
    {
        public List<List<Chord>> Voices { get; set; } = [];

        /// <summary>The staff's beats written as triplets, in order.</summary>
        public TupletSpan[] Tuplets { get; init; } = [];
    }

    /// <summary>A beat written as a tuplet: three (triplet eighths in a quarter) or six (triplet 16ths) values in the time of two or four.</summary>
    private readonly record struct TupletSpan(long Start, long Length, int Num)
    {
        public long End => Start + Length;

        /// <summary>The time between two of its onsets.</summary>
        public long Step => Length / Num;
    }

    /// <param name="Origin">Where a full bar would start: before the start of a pickup.</param>
    private readonly record struct BarGrid(long Origin, long Pulse, long Middle);

    /// <summary>The bars of the piece, and which of them are written: all of them, or an excerpt.</summary>
    private sealed class WrittenBars(Bar[] bars, int firstIndex, int lastIndex)
    {
        public Bar[] Bars => bars;

        public int Count => lastIndex - firstIndex + 1;

        /// <summary>The tick where the first bar written starts.</summary>
        public long Start => bars[firstIndex].Span.Onset.Value;

        /// <summary>The tick after the last bar written.</summary>
        public long End => bars[lastIndex].Span.End.Value;

        public bool Holds(int index) => index >= firstIndex && index <= lastIndex;
    }

    /// <summary>One set of staves over the whole piece: the original, or an ossia above it.</summary>
    private sealed record PartPlan(List<StaffPlan> Staves, ControlEvents[] Controls, PlayingMarks? Marks, HeardCounts Heard, HashSet<string> Shortened, int Approximated, int SlurCount, int PedalCount)
    {
        public int FirstStaff => Staves[0].Number;

        public NotationStaff[] StaffInfo => [.. Staves.Select(staff => new NotationStaff(staff.Number, staff.Track, staff.Clef, staff.Hand, staff.IsSplitInferred))];

        /// <summary>The part's staves laid out in a bar, with the marks of the playing that start there.</summary>
        public PartMeasure Measure(Bar bar, int index, int fifths) => new(FirstStaff, [.. Staves.Select(staff => LayOutStaff(staff, bar, fifths))])
        {
            Dynamic = Marks is not null && Marks.Dynamics.TryGetValue(index, out (long At, string Level) dynamic) ? dynamic : null,
            TempoWord = Marks is not null && Marks.TempoWords.TryGetValue(index, out string? word) ? word : null,
            Controls = Controls[index],
        };
    }

    private sealed record MeasurePlan(Bar Bar, int Fifths, PartMeasure[] Parts);

    /// <param name="FirstStaff">The MEI number of the part's top staff; the others follow it.</param>
    private sealed record PartMeasure(int FirstStaff, StaffMeasure[] Staves)
    {
        /// <summary>A dynamic written under the top staff, at an onset in the bar.</summary>
        public (long At, string Level)? Dynamic { get; init; }

        /// <summary>A tempo word written over the top staff at the bar's start.</summary>
        public string? TempoWord { get; init; }

        /// <summary>Hairpins, slurs and pedal marks that start in the bar.</summary>
        public ControlEvents Controls { get; init; } = new();
    }

    private sealed class ControlEvents
    {
        public List<HairpinPlan> Hairpins { get; } = [];

        public List<(Chord From, Chord To, int Staff)> Slurs { get; } = [];

        /// <summary>"Ped." (down) or "*" (up) at a tick, under a staff.</summary>
        public List<(long At, bool Down, int Staff)> Pedals { get; } = [];
    }

    /// <param name="Tstamp2">Where it ends: bars ahead and the beat in that bar, as MEI writes it; none for "cresc." or "dim." written as a word.</param>
    private sealed record HairpinPlan(long At, bool Crescendo, string? Tstamp2);

    private sealed record StaffMeasure(List<List<Element>> Layers);

    private abstract class Element(long start, long ticks, int dur, int dots)
    {
        public long Start { get; } = start;

        public long Ticks { get; } = ticks;

        public int Dur { get; } = dur;

        public int Dots { get; } = dots;

        /// <summary>The triplet beat the element is written in, if any: its @dur is then 3/2 of its length.</summary>
        public TupletSpan? Tuplet { get; init; }
    }

    private sealed class MeasureRest() : Element(0, 0, 0, 0);

    private sealed class Rest(long start, long ticks, int dur, int dots, bool space) : Element(start, ticks, dur, dots)
    {
        public bool IsSpace { get; } = space;
    }

    private sealed class Beamed(Element[] elements) : Element(elements[0].Start, elements.Sum(element => element.Ticks), 0, 0)
    {
        public Element[] Elements { get; } = elements;
    }

    private sealed class TupletGroup(Element[] elements, TupletSpan span) : Element(elements[0].Start, elements.Sum(element => element.Ticks), 0, 0)
    {
        public Element[] Elements { get; } = elements;

        public TupletSpan Span { get; } = span;
    }

    private sealed class ChordPiece(long start, long ticks, int dur, int dots, Chord chord, int index, bool tieIn, bool tieOut)
        : Element(start, ticks, dur, dots)
    {
        public Chord Chord { get; } = chord;

        public int Index { get; } = index;

        public bool TieIn { get; } = tieIn;

        public bool TieOut { get; } = tieOut;

        public SpelledPitch[] Spellings { get; set; } = [];

        /// <summary>A grace note: written small and slashed, taking no time.</summary>
        public bool IsGrace { get; init; }

        public bool[] ShowAccidental { get; set; } = [];
    }
}