namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>Stable codes for MusicXML import issues, and their plain-English descriptions.</summary>
public static class MusicXmlIssueCodes
{
    // Rounded
    public const string NotesRounded = "musicxml.notes-rounded";
    public const string MetaRounded = "musicxml.meta-rounded";

    // Assumed
    public const string TempoDefault = "musicxml.tempo-default";
    public const string MeterDefault = "musicxml.meter-default";
    public const string DivisionsDefault = "musicxml.divisions-default";
    public const string VelocityDefault = "musicxml.velocity-default";
    public const string MicrotoneRounded = "musicxml.microtone-rounded";
    public const string MeasureShort = "musicxml.measure-short";
    public const string MeasureLong = "musicxml.measure-long";

    // Repaired
    public const string ZeroLengthWidened = "musicxml.zero-length-widened";
    public const string BackupBeforeMeasure = "musicxml.backup-before-measure";
    public const string PartNotListed = "musicxml.part-not-listed";
    public const string DivisionsInvalid = "musicxml.divisions-invalid";
    public const string NoteWithoutDuration = "musicxml.note-without-duration";
    public const string TieWithoutStart = "musicxml.tie-without-start";

    // Dropped
    public const string GraceNotesFlagged = "musicxml.grace-notes-flagged";
    public const string CueNotesIgnored = "musicxml.cue-notes-ignored";
    public const string UnpitchedIgnored = "musicxml.unpitched-ignored";
    public const string PitchInvalid = "musicxml.pitch-invalid";
    public const string MetaInvalid = "musicxml.meta-invalid";
    public const string MetaUnsupported = "musicxml.meta-unsupported";
    public const string TempoSuperseded = "musicxml.tempo-superseded";
    public const string MeterSuperseded = "musicxml.meter-superseded";
    public const string MeterInPickup = "musicxml.meter-in-pickup";

    public static string Describe(string code, int count) => code switch
    {
        NotesRounded => $"{Notes(count)} had timing rounded to the 480-per-quarter grid, by at most half a tick at each end.",
        MetaRounded => $"{Plural(count, "tempo, time or key signature change", "tempo, time or key signature changes")} rounded to the 480-per-quarter grid.",
        TempoDefault => "The score sets no tempo at its start, so 120 BPM is assumed until the first tempo mark.",
        MeterDefault => "The score has no time signature at its start, so 4/4 is assumed.",
        DivisionsDefault => "The part gives durations before saying how many divisions make a quarter note, so 1 per quarter is assumed.",
        VelocityDefault => $"{Plural(count, "note has", "notes have")} no playback dynamics; a moderate velocity of {MusicXmlImporter.DefaultVelocity} is assumed.",
        MicrotoneRounded => $"{Plural(count, "note with a microtonal alteration was", "notes with microtonal alterations were")} rounded to the nearest semitone.",
        MeasureShort => $"{Plural(count, "measure is", "measures are")} shorter than the time signature (for example a bar split at a repeat); each is kept as a short bar.",
        MeasureLong => $"{Plural(count, "measure is", "measures are")} longer than the time signature; the extra time is shown as a short extra bar, so later bar numbers move on by one.",
        ZeroLengthWidened => $"{Notes(count)} had a duration of zero and were widened to one tick.",
        BackupBeforeMeasure => $"{Plural(count, "backup goes", "backups go")} back past the start of the measure; the position was set to the start of the measure.",
        PartNotListed => "The part is missing from the score's part list; its notes were read without a part name.",
        DivisionsInvalid => $"{Plural(count, "divisions value was", "divisions values were")} not a positive whole number and were ignored.",
        TieWithoutStart => $"{Plural(count, "note ends a tie that does not start on the same pitch before it; it was", "notes end a tie that does not start on the same pitch before them; they were")} kept as separate notes.",
        NoteWithoutDuration => $"{Plural(count, "note, rest, backup or forward has", "notes, rests, backups or forwards have")} no duration and were left out.",
        GraceNotesFlagged => $"{Plural(count, "grace note is", "grace notes are")} listed as grace notes but not played or analysed yet.",
        CueNotesIgnored => $"{Plural(count, "cue note was", "cue notes were")} left out, since cue notes are not played.",
        UnpitchedIgnored => $"{Plural(count, "unpitched (percussion) note was", "unpitched (percussion) notes were")} left out.",
        PitchInvalid => $"{Plural(count, "note had", "notes had")} a missing or out-of-range pitch and were left out.",
        MetaInvalid => $"{Plural(count, "tempo, time or key signature had", "tempo, time or key signatures had")} invalid values and were left out.",
        MetaUnsupported => $"{Plural(count, "time or key signature of an unusual kind was", "time or key signatures of unusual kinds were")} left out (for example free time, mixed time signatures or non-traditional keys).",
        TempoSuperseded => $"{Plural(count, "tempo mark was", "tempo marks were")} replaced by a different one at the same position.",
        MeterSuperseded => $"{Plural(count, "time signature was", "time signatures were")} replaced by a different one at the same position.",
        MeterInPickup => $"{Plural(count, "time signature inside the pickup bar was", "time signatures inside the pickup bar were")} left out.",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown MusicXML issue code."),
    };

    private static string Notes(int count) => Plural(count, "note", "notes");

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";
}