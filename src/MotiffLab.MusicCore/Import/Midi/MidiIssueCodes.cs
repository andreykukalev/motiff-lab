namespace MotiffLab.MusicCore.Import.Midi;

/// <summary>Stable codes for MIDI import issues, and their descriptions in English and Russian.</summary>
public static class MidiIssueCodes
{
    // Rounded
    public const string NotesRounded = "midi.notes-rounded";
    public const string MetaRounded = "midi.meta-rounded";

    // Assumed
    public const string TempoDefault = "midi.tempo-default";
    public const string MeterDefault = "midi.meter-default";
    public const string NoteOffMissing = "midi.note-off-missing";
    public const string SamePitchOverlap = "midi.same-pitch-overlap";
    public const string TempoOutsideFirstTrack = "midi.tempo-outside-first-track";
    public const string Format0WithManyTracks = "midi.format0-many-tracks";
    public const string HandsInferred = "midi.hands-inferred";

    // Repaired
    public const string ZeroLengthWidened = "midi.zero-length-widened";
    public const string TrackDamaged = "midi.track-damaged";
    public const string TrackTruncated = "midi.track-truncated";
    public const string EndOfTrackMissing = "midi.end-of-track-missing";
    public const string TrackCountMismatch = "midi.track-count-mismatch";
    public const string TrailingBytes = "midi.trailing-bytes";

    // Dropped
    public const string DrumsExcluded = "midi.drums-excluded";
    public const string NoteOffUnmatched = "midi.note-off-unmatched";
    public const string ControllersIgnored = "midi.controllers-ignored";
    public const string PitchBendIgnored = "midi.pitch-bend-ignored";
    public const string AftertouchIgnored = "midi.aftertouch-ignored";
    public const string SysExIgnored = "midi.sysex-ignored";
    public const string MetaIgnored = "midi.meta-ignored";
    public const string MetaInvalid = "midi.meta-invalid";
    public const string TempoSuperseded = "midi.tempo-superseded";
    public const string MeterSuperseded = "midi.meter-superseded";
    public const string KeySuperseded = "midi.key-superseded";
    public const string UnknownChunk = "midi.unknown-chunk";
    public const string DataAfterEndOfTrack = "midi.data-after-end-of-track";

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
            "The file sets no tempo at its start, so 120 BPM is assumed until the first tempo change.",
            "В начале файла не задан темп, поэтому до первой смены темпа принято 120 ударов в минуту."),
        MeterDefault => words.T(
            "The file has no time signature at its start, so 4/4 is assumed.",
            "В начале файла нет размера, поэтому принят размер 4/4."),
        NoteOffMissing => words.T(
            $"{Notes(count)} had no note-off; each is assumed to last until the end of the piece.",
            $"Нот без снятия (note-off): {count}; считается, что каждая звучит до конца пьесы."),
        SamePitchOverlap => words.T(
            $"{Notes(count)} started while the same key was already sounding; note-offs were paired in order (first on, first off).",
            $"Нот, взятых, пока та же клавиша ещё звучала: {count}; снятия сопоставлены по порядку (первой взята — первой снята)."),
        TempoOutsideFirstTrack => words.T(
            $"{Plural(count, "tempo change", "tempo changes")} found outside the first track were applied to the whole piece.",
            $"Смен темпа вне первой дорожки, применённых ко всей пьесе: {count}."),
        Format0WithManyTracks => words.T(
            "The file says it is type 0 but has more than one track; every track was read.",
            "Файл объявлен как тип 0, но в нём больше одной дорожки; прочитаны все дорожки."),
        HandsInferred => words.T(
            $"The file has both hands on one track, so the hand of {Plural(count, "note was", "notes were")} inferred from pitch and continuity; you can reassign any note.",
            $"В файле обе руки на одной дорожке, поэтому рука выведена по высоте и связности (нот: {count}); любую ноту можно переназначить."),
        ZeroLengthWidened => words.T(
            $"{Notes(count)} ended at the same tick they started and were widened to one tick.",
            $"Нот, закончившихся на том же тике, где начались, и растянутых до одного тика: {count}."),
        TrackDamaged => words.T(
            "The track is damaged; events up to the damage were kept and the rest left out.",
            "Дорожка повреждена: события до повреждения сохранены, остальное пропущено."),
        TrackTruncated => words.T(
            "The track is shorter than its header says; what was there was read.",
            "Дорожка короче, чем сказано в её заголовке; прочитано то, что есть."),
        EndOfTrackMissing => words.T(
            "The track has no end-of-track marker; it was read to the end of its data.",
            "У дорожки нет метки конца; она прочитана до конца данных."),
        TrackCountMismatch => words.T(
            "The header's track count does not match the tracks in the file; the tracks found were read.",
            "Число дорожек в заголовке не совпадает с дорожками в файле; прочитаны найденные дорожки."),
        TrailingBytes => words.T(
            "The file ends with stray bytes after its last track; they were ignored.",
            "После последней дорожки в файле лишние байты; они пропущены."),
        DrumsExcluded => words.T(
            $"{Plural(count, "note on the drum channel (10) was", "notes on the drum channel (10) were")} left out.",
            $"Пропущено нот на канале ударных (10): {count}."),
        NoteOffUnmatched => words.T(
            $"{Plural(count, "note-off had", "note-offs had")} no matching note-on and were left out.",
            $"Пропущено снятий (note-off) без парного нажатия: {count}."),
        ControllersIgnored => words.T(
            $"{Plural(count, "controller message was", "controller messages were")} left out.",
            $"Пропущено сообщений контроллеров: {count}."),
        PitchBendIgnored => words.T(
            $"{Plural(count, "pitch bend message was", "pitch bend messages were")} left out.",
            $"Пропущено сообщений изменения высоты (pitch bend): {count}."),
        AftertouchIgnored => words.T(
            $"{Plural(count, "aftertouch message was", "aftertouch messages were")} left out.",
            $"Пропущено сообщений послекасания (aftertouch): {count}."),
        SysExIgnored => words.T(
            $"{Plural(count, "system exclusive message was", "system exclusive messages were")} left out.",
            $"Пропущено системных сообщений (SysEx): {count}."),
        MetaIgnored => words.T(
            $"{Plural(count, "text, marker or other meta event was", "text, marker or other meta events were")} left out.",
            $"Пропущено текстов, маркеров и других метасобытий: {count}."),
        MetaInvalid => words.T(
            $"{Plural(count, "tempo, time or key signature event had", "tempo, time or key signature events had")} invalid values and were left out.",
            $"Пропущено событий темпа, размера или тональности с недопустимыми значениями: {count}."),
        TempoSuperseded => words.T(
            $"{Plural(count, "tempo change was", "tempo changes were")} replaced by a later one at the same position.",
            $"Заменено более поздними на том же месте смен темпа: {count}."),
        MeterSuperseded => words.T(
            $"{Plural(count, "time signature was", "time signatures were")} replaced by a later one at the same position.",
            $"Заменено более поздними на том же месте размеров: {count}."),
        KeySuperseded => words.T(
            $"{Plural(count, "key signature was", "key signatures were")} replaced by a later one at the same position.",
            $"Заменено более поздними на том же месте тональностей: {count}."),
        UnknownChunk => words.T(
            $"{Plural(count, "chunk of an unknown type was", "chunks of an unknown type were")} skipped.",
            $"Пропущено блоков неизвестного типа: {count}."),
        DataAfterEndOfTrack => words.T(
            "The track has data after its end-of-track marker; it was ignored.",
            "После метки конца дорожки есть данные; они пропущены."),
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown MIDI issue code."),
    };

    private static string Notes(int count) => Plural(count, "note", "notes");

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";
}