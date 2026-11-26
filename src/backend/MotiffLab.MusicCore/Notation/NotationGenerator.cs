using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Pieces;
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
/// Beats whose onsets fit thirds or sixths of the beat are written as triplets; other tuplets (fives, sevens) land on
/// the nearest 32nd and are counted as approximated. Grace notes are written only for notes heard in a recording
/// (<c>asHeard</c>); dynamics, accents, tempo words, hairpins, slurs, fermatas and pedal only from how the notes were
/// played (<c>playedTempos</c>). The notes themselves are never changed; only how they are written.
/// </para>
/// <para>
/// An excerpt writes only some bars, and ossias stand above the original bar for bar as smaller staves of the
/// same measures (decision record 0021).
/// </para>
/// <para>
/// Notes with a source record (read from a PDF, or imported as MusicXML) are written as the score prints them, bar by bar
/// and staff by staff, where the record fits; the steps above write the other bars, and inside a printed bar the voices
/// of notes the player corrected or added (<see cref="PrintedBars"/>, «Как напечатано», step 1).
/// </para>
/// <para>
/// The parts: <see cref="NotationPlan"/> plans the parts and bars, with <see cref="StaffVoices"/> (staves, clefs and
/// voices), <see cref="TupletBeats"/> (triplets), <see cref="HeardRhythm"/> (notes heard in a recording) and
/// <see cref="ControlPlan"/> (the marks of the playing); <see cref="StaffLayout"/> lays each staff out in a bar with
/// <see cref="BarValues"/>, <see cref="Beaming"/> and <see cref="BarAccidentals"/>; <see cref="MeiWriter"/> writes the document.
/// </para>
/// </remarks>
public static class NotationGenerator
{
    /// <summary>The display grid: a 32nd note.</summary>
    public const long Grid = CanonicalTime.TicksPerQuarter / 8;

    public const int MaxVoicesPerStaff = 4;

    /// <summary>The size of an ossia's staves against the original's, as MEI writes it.</summary>
    public const string OssiaScale = "75%";

    /// <summary>The gap before the next onset that writing as heard closes: shorter than a quarter note.</summary>
    public const long HeldGap = CanonicalTime.TicksPerQuarter;

    /// <summary>The grid notes heard in a recording are written on: a 16th, or a 32nd where 16ths would merge two onsets.</summary>
    public const long HeardGrid = CanonicalTime.TicksPerQuarter / 4;

    /// <summary>Onsets this close are one chord when written as heard: a 64th.</summary>
    internal const long HeardChord = Grid / 2;

    /// <summary>Notation for a MIDI import, with its key signatures.</summary>
    /// <param name="asHeard">See <see cref="Generate"/>.</param>
    /// <param name="tempos">The tempo marked on the score, as <see cref="Generate"/> takes it.</param>
    public static GeneratedNotation FromMidi(MidiImport import, string? title = null, bool asHeard = false, IReadOnlyList<PlaybackTempo>? tempos = null)
    {
        ArgumentNullException.ThrowIfNull(import);
        return Generate(import.Notes, import.Meter, import.KeySignatures.Select(key => new KeyChange(key.At, Math.Clamp(key.Sharps, -7, 7))), import.End, title, asHeard, tempos: tempos);
    }

    /// <summary>
    /// Notation for a MusicXML import, written as the score prints it bar by bar where it can (<paramref name="printed"/>),
    /// generated elsewhere. Key signatures come from the first part.
    /// </summary>
    /// <param name="tempos">The tempo marked on the score, as <see cref="Generate"/> takes it.</param>
    public static GeneratedNotation FromMusicXml(MusicXmlImport import, string? title = null, IReadOnlyList<PlaybackTempo>? tempos = null)
    {
        ArgumentNullException.ThrowIfNull(import);
        int firstTrack = import.KeySignatures.Count == 0 ? 0 : import.KeySignatures.Min(key => key.Track);
        IEnumerable<KeyChange> keys = import.KeySignatures
            .Where(key => key.Track == firstTrack)
            .Select(key => new KeyChange(key.At, Math.Clamp(key.Fifths, -7, 7)));
        return Generate(import.Notes, import.Meter, keys, import.End, title, tempos: tempos, printed: PrintedNotation.Of(import));
    }

    /// <param name="notes">The notes to write, in any order.</param>
    /// <param name="meter">The bars to write them in.</param>
    /// <param name="keys">Key signatures; C major until the first. A change takes effect at the next bar line.</param>
    /// <param name="end">The end of the piece; the last bar is the one that contains the end, or the last note's end if later.</param>
    /// <param name="title">The title written in the MEI header.</param>
    /// <param name="asHeard">
    /// For notes heard in a recording, written as a player reads them rather than as they sounded
    /// (decision record 0016): onsets on the nearest 16th (a 32nd where 16ths would merge two
    /// onsets, and onsets within a 64th are one chord on every staff, as is a chord spread over a 32nd);
    /// a note shorter than a 32nd just before the next, or one that leads into a spread chord, as a grace note; and each note up to the next onset on its staff, so that the pedal or a key let
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
    /// <param name="marks">
    /// With <paramref name="playedTempos"/>: <see cref="PlayedMarks.Significant"/> for «Для чтения» (version 0.5), only the
    /// dynamics that change by two steps or more, "rit." and "accel." where the bars grow a fifth longer or shorter, and
    /// fermatas; no accents, hairpins, slurs or pedal. Usually with <paramref name="asHeard"/>.
    /// </param>
    /// <param name="ossias">
    /// Staves above the original, bar for bar, at three quarters of the size, each with its label at the left:
    /// a variation in place of the passage it changed, or the same notes written as played (item 10). Their
    /// staves are numbered first; their note ids carry the prefix <c>ossia-</c> (<c>ossia2-</c> for a second),
    /// so a note shown twice has two ids.
    /// </param>
    /// <param name="spelling">
    /// For notes moved to another key (#351): each written as the original note was, moved by the interval's letters,
    /// rather than from the key signature alone.
    /// </param>
    /// <param name="tempos">
    /// The tempo the notes play at, as played (version 0.5.4): marked over the original's top staff as
    /// <see cref="TempoMarks"/> says, "♩ = 92" at the start and at each change the notes state; none when null.
    /// </param>
    /// <param name="printed">
    /// The notes' source record, for notes read from a PDF or imported as MusicXML («Как напечатано», step 1): each bar of
    /// a staff of the original written as the score prints it, where the record fits (<see cref="PrintedBars"/>): voices,
    /// rests, note values with dots and ties, tuplets, stems and beams. Not for notes written as heard, or an ossia.
    /// </param>
    public static GeneratedNotation Generate(IEnumerable<Note> notes, MeterMap meter, IEnumerable<KeyChange> keys, Tick end, string? title = null, bool asHeard = false, IReadOnlyList<PlaybackTempo>? playedTempos = null, IEnumerable<TickSpan>? pedal = null, BarRange? bars = null, IReadOnlyList<NotationOssia>? ossias = null, PlayedMarks marks = PlayedMarks.All, TransposedSpelling? spelling = null, IReadOnlyList<PlaybackTempo>? tempos = null, PrintedNotation? printed = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(keys);

        Note[] input = [.. notes];
        KeyChange[] keyChanges = [.. keys.OrderBy(key => key.At)];
        NotationOssia[] above = [.. ossias ?? []];
        Note[][] ossiaNotes = [.. above.Select((ossia, index) => ossia.Notes.Select(note => note with { Id = OssiaIdPrefix(index) + note.Id }).ToArray())];

        var written = WrittenBars.Of(meter, LastTick(end, input, ossiaNotes), bars);

        // The ossias stand above the original, so their staves come first.
        var parts = new List<PartPlan>();
        int nextStaff = 1;
        for (int index = 0; index < above.Length; index++)
        {
            PartPlan part = NotationPlan.PlanPart(ossiaNotes[index], written, above[index].AsHeard, above[index].PlayedTempos, above[index].Pedal, nextStaff, ossia: true);
            parts.Add(part);
            nextStaff += part.Staves.Count;
        }

        PartPlan main = NotationPlan.PlanPart(input, written, asHeard, playedTempos, pedal, nextStaff, ossia: false, marks, printed);
        parts.Add(main);

        (List<MeasurePlan> measures, int markCount) = NotationPlan.PlanMeasures(written, keyChanges, parts, spelling, tempos, meter);

        string mei = MeiWriter.Write(title ?? "Generated notation", parts, above, measures);
        return Counted(new GeneratedNotation(mei, main.StaffInfo, input.Length, main.Approximated, main.Shortened.Count, main.Heard.Held, main.Heard.Cut, main.Heard.Graces, asHeard), main) with
        {
            TempoMarkCount = markCount,
            Excerpt = bars is null ? null : written.Range,
            Ossias = [.. above.Select((ossia, index) => new GeneratedOssia(ossia.Label, parts[index].StaffInfo, ossiaNotes[index].Length, OssiaIdPrefix(index)))],
        };
    }

    /// <summary>The prefix of an ossia's note ids: the first ossia's "ossia-", the second's "ossia2-".</summary>
    public static string OssiaIdPrefix(int index) => index == 0 ? "ossia-" : $"ossia{MeiWriter.Number(index + 1)}-";

    /// <summary>A tick on the display grid (<see cref="Grid"/>), the nearer way.</summary>
    internal static long Snap(long tick) => SnapTo(tick, Grid);

    /// <summary>A tick on <paramref name="grid"/>, the nearer way.</summary>
    internal static long SnapTo(long tick, long grid) => (tick + (grid / 2)) / grid * grid;

    /// <summary>Where the bars reach: to the end given, or to the last note shown, in the original or an ossia.</summary>
    private static long LastTick(Tick end, Note[] input, Note[][] ossiaNotes) =>
        Math.Max(1, Math.Max(end.Value, input.Concat(ossiaNotes.SelectMany(part => part))
            .Select(note => Math.Max(Snap(note.Span.End.Value), Snap(note.Span.Onset.Value) + Grid))
            .DefaultIfEmpty(0)
            .Max()));

    /// <summary>The notation with the counts of the marks of the playing the original's part carries.</summary>
    private static GeneratedNotation Counted(GeneratedNotation notation, PartPlan main) => notation with
    {
        AccentCount = main.Marks?.Accented.Count ?? 0,
        DynamicCount = main.Marks?.Dynamics.Count ?? 0,
        TempoWordCount = main.Marks?.TempoWords.Count ?? 0,
        HairpinCount = main.Marks?.Hairpins.Count ?? 0,
        FermataCount = main.Marks?.Fermatas.Count ?? 0,
        TripletCount = main.Staves.Sum(staff => staff.Tuplets.Length),
        SlurCount = main.SlurCount,
        PedalCount = main.PedalCount,
    };
}