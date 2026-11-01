namespace MotiffLab.MusicCore.Import.MusicXml;

/// <summary>Stable codes for MusicXML import issues, and their descriptions in English and Russian.</summary>
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
    public const string SlurUnmatched = "musicxml.slur-unmatched";

    /// <summary>The issue in plain English, as <see cref="ImportIssue.Message"/> keeps it.</summary>
    public static string Describe(string code, int count) => Describe(code, count, Words.English);

    /// <summary>The issue in the language (0018, preparation item 13).</summary>
    public static string Describe(string code, int count, Words words) => code switch
    {
        NotesRounded => words.T(
            $"{Notes(count)} had timing rounded to the 480-per-quarter grid, by at most half a tick at each end.",
            $"Нот, время которых округлено до сетки 480 на четверть (не более чем на полтика с каждого конца): {count}."),
        MetaRounded => words.T(
            $"{Plural(count, "tempo, time or key signature change", "tempo, time or key signature changes")} rounded to the 480-per-quarter grid.",
            $"Смен темпа, размера или тональности, округлённых до сетки 480 на четверть: {count}."),
        TempoDefault => words.T(
            "The score sets no tempo at its start, so 120 BPM is assumed until the first tempo mark.",
            "В начале партитуры не задан темп, поэтому до первого обозначения темпа принято 120 ударов в минуту."),
        MeterDefault => words.T(
            "The score has no time signature at its start, so 4/4 is assumed.",
            "В начале партитуры нет размера, поэтому принят размер 4/4."),
        DivisionsDefault => words.T(
            "The part gives durations before saying how many divisions make a quarter note, so 1 per quarter is assumed.",
            "Партия задаёт длительности раньше, чем число долей в четверти, поэтому принята 1 доля на четверть."),
        VelocityDefault => words.T(
            $"{Plural(count, "note has", "notes have")} no playback dynamics; a moderate velocity of {MusicXmlImporter.DefaultVelocity} is assumed.",
            $"Нот без громкости для воспроизведения: {count}; принята умеренная громкость {MusicXmlImporter.DefaultVelocity}."),
        MicrotoneRounded => words.T(
            $"{Plural(count, "note with a microtonal alteration was", "notes with microtonal alterations were")} rounded to the nearest semitone.",
            $"Нот с микротоновым изменением, округлённых до ближайшего полутона: {count}."),
        MeasureShort => words.T(
            $"{Plural(count, "measure is", "measures are")} shorter than the time signature (for example a bar split at a repeat); each is kept as a short bar.",
            $"Тактов короче размера (например, такт, разделённый повтором): {count}; каждый оставлен коротким тактом."),
        MeasureLong => words.T(
            $"{Plural(count, "measure is", "measures are")} longer than the time signature; the extra time is shown as a short extra bar, so later bar numbers move on by one.",
            $"Тактов длиннее размера: {count}; лишнее время показано коротким дополнительным тактом, поэтому номера следующих тактов сдвигаются на один."),
        ZeroLengthWidened => words.T(
            $"{Notes(count)} had a duration of zero and were widened to one tick.",
            $"Нот нулевой длительности, растянутых до одного тика: {count}."),
        BackupBeforeMeasure => words.T(
            $"{Plural(count, "backup goes", "backups go")} back past the start of the measure; the position was set to the start of the measure.",
            $"Возвратов (backup) за начало такта: {count}; позиция поставлена на начало такта."),
        PartNotListed => words.T(
            "The part is missing from the score's part list; its notes were read without a part name.",
            "Партии нет в списке партий партитуры; её ноты прочитаны без названия партии."),
        DivisionsInvalid => words.T(
            $"{Plural(count, "divisions value was", "divisions values were")} not a positive whole number and were ignored.",
            $"Пропущено значений divisions, не являющихся целым положительным числом: {count}."),
        TieWithoutStart => words.T(
            $"{Plural(count, "note ends a tie that does not start on the same pitch before it; it was", "notes end a tie that does not start on the same pitch before them; they were")} kept as separate notes.",
            $"Нот, которыми заканчивается лига без начала на той же высоте: {count}; они оставлены отдельными нотами."),
        NoteWithoutDuration => words.T(
            $"{Plural(count, "note, rest, backup or forward has", "notes, rests, backups or forwards have")} no duration and were left out.",
            $"Пропущено нот, пауз, возвратов и сдвигов без длительности: {count}."),
        GraceNotesFlagged => words.T(
            $"{Plural(count, "grace note is", "grace notes are")} listed as grace notes but not played or analysed yet.",
            $"Форшлагов: {count}; они отмечены, но пока не звучат и не анализируются."),
        CueNotesIgnored => words.T(
            $"{Plural(count, "cue note was", "cue notes were")} left out, since cue notes are not played.",
            $"Пропущено подсказочных нот (cue), которые не играются: {count}."),
        UnpitchedIgnored => words.T(
            $"{Plural(count, "unpitched (percussion) note was", "unpitched (percussion) notes were")} left out.",
            $"Пропущено нот без высоты (ударные): {count}."),
        PitchInvalid => words.T(
            $"{Plural(count, "note had", "notes had")} a missing or out-of-range pitch and were left out.",
            $"Пропущено нот без высоты или с высотой вне диапазона: {count}."),
        MetaInvalid => words.T(
            $"{Plural(count, "tempo, time or key signature had", "tempo, time or key signatures had")} invalid values and were left out.",
            $"Пропущено обозначений темпа, размера или тональности с недопустимыми значениями: {count}."),
        MetaUnsupported => words.T(
            $"{Plural(count, "time or key signature of an unusual kind was", "time or key signatures of unusual kinds were")} left out (for example free time, mixed time signatures or non-traditional keys).",
            $"Пропущено необычных размеров и тональностей (например, свободный или смешанный размер, нетрадиционная тональность): {count}."),
        TempoSuperseded => words.T(
            $"{Plural(count, "tempo mark was", "tempo marks were")} replaced by a different one at the same position.",
            $"Заменено другими на том же месте обозначений темпа: {count}."),
        MeterSuperseded => words.T(
            $"{Plural(count, "time signature was", "time signatures were")} replaced by a different one at the same position.",
            $"Заменено другими на том же месте размеров: {count}."),
        MeterInPickup => words.T(
            $"{Plural(count, "time signature inside the pickup bar was", "time signatures inside the pickup bar were")} left out.",
            $"Пропущено размеров внутри затакта: {count}."),
        SlurUnmatched => words.T(
            $"{Plural(count, "slur mark has", "slur marks have")} no matching start or end and were left out, so no phrase boundary is taken from them.",
            $"Пропущено лиг без пары начала или конца: {count}; границы фраз из них не берутся."),
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown MusicXML issue code."),
    };

    private static string Notes(int count) => Plural(count, "note", "notes");

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";
}