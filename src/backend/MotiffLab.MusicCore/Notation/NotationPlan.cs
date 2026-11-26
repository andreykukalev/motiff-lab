using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Notation;

/// <summary>
/// Plans the notation over the piece's bars (<see cref="NotationGenerator"/>): each part's staves, the rhythm their notes
/// are written in, their voices and the marks of the playing, then each bar written with its key and metronome marks.
/// </summary>
internal static class NotationPlan
{
    /// <summary>The bars written, each with its key, its parts' measures and its metronome marks over the original's top staff (the last part).</summary>
    public static (List<MeasurePlan> Measures, int TempoMarks) PlanMeasures(WrittenBars written, KeyChange[] keyChanges, List<PartPlan> parts, TransposedSpelling? spelling, IReadOnlyList<PlaybackTempo>? tempos, MeterMap meter)
    {
        IReadOnlyList<TempoMark> tempoMarks = tempos is null ? [] : TempoMarks.Of(tempos, meter);
        var measures = new List<MeasurePlan>(written.Count);
        int markCount = 0;
        for (int index = written.FirstIndex; index <= written.LastIndex; index++)
        {
            Bar bar = written.Bars[index];
            int fifths = KeyAt(keyChanges, bar.Span.Onset);
            TempoMark[] inBar = [.. tempoMarks.Where(mark => bar.Span.Onset <= mark.At && mark.At < bar.Span.End)];
            markCount += inBar.Length;
            measures.Add(new MeasurePlan(bar, fifths, [.. parts.Select(part => part.Measure(bar, index, fifths, spelling))]) { Tempos = inBar, TempoStaff = parts[^1].FirstStaff });
        }

        return (measures, markCount);
    }

    /// <summary>
    /// Plans one set of staves (the original, or an ossia) over the piece's bars: the staves, their voices,
    /// and the marks of the playing. Only the bars written get control events that end inside them. An ossia
    /// has only the staves that show notes in the bars written: a variation of the melody is one staff above the grand staff.
    /// </summary>
    /// <param name="printed">The notes' source record, for the bars written as the score prints them (<see cref="PrintedBars"/>); null for none.</param>
    public static PartPlan PlanPart(Note[] input, WrittenBars written, bool asHeard, IReadOnlyList<PlaybackTempo>? playedTempos, IEnumerable<TickSpan>? pedal, int firstStaff, bool ossia, PlayedMarks kind = PlayedMarks.All, PrintedNotation? printed = null)
    {
        Bar[] bars = written.Bars;
        var shown = input.Select(note => new ShownNote(note, NotationGenerator.Snap(note.Span.Onset.Value), NotationGenerator.Snap(note.Span.End.Value))).ToArray();

        List<StaffPlan> planned = Shown(StaffVoices.PlanStaves(shown, firstStaff), written, firstStaff, ossia);

        // Beats whose onsets fit three or six to the beat better than 16ths are written as triplets.
        List<StaffPlan> staves = [.. planned.Select(staff => staff with { Tuplets = TupletBeats.Find(staff.Notes, bars) })];
        var heard = new HeardCounts();
        staves = Rhythm(staves, input, asHeard, heard);
        if (printed is not null && !asHeard)
        {
            staves.ForEach(staff => (staff.Printed, staff.AsPrinted) = PrintedBars.Plan(staff, written, printed));
        }

        // A note written as printed is written exactly as the score writes it, so it is not approximated.
        int approximated = staves.Sum(staff => staff.Notes.Count(note => !staff.AsPrinted.Contains(note.Note.Id) && (note.Onset != note.Note.Span.Onset.Value || note.End != note.Note.Span.End.Value)));

        bool significant = kind == PlayedMarks.Significant;
        PlayingMarks? marks = playedTempos is null ? null : significant ? PlayingMarks.Significant(input, bars, playedTempos) : PlayingMarks.Of(input, bars, playedTempos);
        HashSet<string> shortened = GiveVoices(staves, marks);
        staves.ForEach(staff => shortened.ExceptWith(staff.AsPrinted));
        (ControlEvents[] controls, int slurCount, int pedalCount) = ControlPlan.Of(staves, written, marks, pedal, significant);
        return new PartPlan(staves, controls, marks, heard, shortened, approximated, slurCount, pedalCount);
    }

    /// <summary>The key in force at a position: C major until the first change.</summary>
    public static int KeyAt(KeyChange[] keys, Tick position)
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

    /// <summary>The staves of an ossia that show notes in the bars written, numbered again, when only some do; all of them otherwise.</summary>
    private static List<StaffPlan> Shown(List<StaffPlan> planned, WrittenBars written, int firstStaff, bool ossia)
    {
        bool Shows(StaffPlan staff) => staff.Notes.Any(note => note.Onset < written.End && note.End > written.Start);
        if (ossia && planned.Count > 1 && planned.Any(Shows) && !planned.All(Shows))
        {
            planned = [.. planned.Where(Shows).Select((staff, index) => staff with { Number = firstStaff + index })];
        }

        return planned;
    }

    /// <summary>The staves' notes written as heard (<see cref="HeardRhythm"/>), or on the 32nd grid with their triplets.</summary>
    private static List<StaffPlan> Rhythm(List<StaffPlan> staves, Note[] input, bool asHeard, HeardCounts heard)
    {
        if (asHeard)
        {
            HeardOnsets together = HeardOnsets.Of(input);
            return [.. staves.Select(staff => staff with { Notes = HeardRhythm.AsHeard(staff.Notes, heard, staff.Tuplets, together) })];
        }

        return [.. staves.Select(staff => staff with { Notes = TupletBeats.OnTuplets(staff.Notes, staff.Tuplets) })];
    }

    /// <summary>Gives each staff its voices (<see cref="StaffVoices"/>), its grace notes and its accents; returns the ids of the notes shortened.</summary>
    private static HashSet<string> GiveVoices(List<StaffPlan> staves, PlayingMarks? marks)
    {
        var shortened = new HashSet<string>();
        foreach (StaffPlan staff in staves)
        {
            staff.Voices = StaffVoices.AssignVoices([.. staff.Notes.Where(note => !note.IsGrace)], shortened);
            HeardRhythm.AttachGraces(staff, [.. staff.Notes.Where(note => note.IsGrace)]);
            foreach (Chord chord in staff.Voices.SelectMany(voice => voice))
            {
                chord.IsAccented = marks is not null && chord.Notes.Any(note => marks.Accented.Contains(note.Note.Id));
            }
        }

        return shortened;
    }
}