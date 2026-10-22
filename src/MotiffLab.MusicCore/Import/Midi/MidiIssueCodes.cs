namespace MotiffLab.MusicCore.Import.Midi;

/// <summary>Stable codes for MIDI import issues, and their plain-English descriptions.</summary>
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
    public const string SustainPedalIgnored = "midi.sustain-pedal-ignored";
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

    public static string Describe(string code, int count) => code switch
    {
        NotesRounded => $"{Notes(count)} had timing rounded to the 480-per-quarter grid, by at most half a tick at each end.",
        MetaRounded => $"{Plural(count, "tempo, time or key signature change", "tempo, time or key signature changes")} rounded to the 480-per-quarter grid.",
        TempoDefault => "The file sets no tempo at its start, so 120 BPM is assumed until the first tempo change.",
        MeterDefault => "The file has no time signature at its start, so 4/4 is assumed.",
        NoteOffMissing => $"{Notes(count)} had no note-off; each is assumed to last until the end of the piece.",
        SamePitchOverlap => $"{Notes(count)} started while the same key was already sounding; note-offs were paired in order (first on, first off).",
        TempoOutsideFirstTrack => $"{Plural(count, "tempo change", "tempo changes")} found outside the first track were applied to the whole piece.",
        Format0WithManyTracks => "The file says it is type 0 but has more than one track; every track was read.",
        ZeroLengthWidened => $"{Notes(count)} ended at the same tick they started and were widened to one tick.",
        TrackDamaged => "The track is damaged; events up to the damage were kept and the rest left out.",
        TrackTruncated => "The track is shorter than its header says; what was there was read.",
        EndOfTrackMissing => "The track has no end-of-track marker; it was read to the end of its data.",
        TrackCountMismatch => "The header's track count does not match the tracks in the file; the tracks found were read.",
        TrailingBytes => "The file ends with stray bytes after its last track; they were ignored.",
        DrumsExcluded => $"{Plural(count, "note on the drum channel (10) was", "notes on the drum channel (10) were")} left out.",
        NoteOffUnmatched => $"{Plural(count, "note-off had", "note-offs had")} no matching note-on and were left out.",
        SustainPedalIgnored => $"{Plural(count, "sustain pedal message was", "sustain pedal messages were")} left out; Stage 1 does not use the pedal yet.",
        ControllersIgnored => $"{Plural(count, "controller message was", "controller messages were")} left out.",
        PitchBendIgnored => $"{Plural(count, "pitch bend message was", "pitch bend messages were")} left out.",
        AftertouchIgnored => $"{Plural(count, "aftertouch message was", "aftertouch messages were")} left out.",
        SysExIgnored => $"{Plural(count, "system exclusive message was", "system exclusive messages were")} left out.",
        MetaIgnored => $"{Plural(count, "text, marker or other meta event was", "text, marker or other meta events were")} left out.",
        MetaInvalid => $"{Plural(count, "tempo, time or key signature event had", "tempo, time or key signature events had")} invalid values and were left out.",
        TempoSuperseded => $"{Plural(count, "tempo change was", "tempo changes were")} replaced by a later one at the same position.",
        MeterSuperseded => $"{Plural(count, "time signature was", "time signatures were")} replaced by a later one at the same position.",
        KeySuperseded => $"{Plural(count, "key signature was", "key signatures were")} replaced by a later one at the same position.",
        UnknownChunk => $"{Plural(count, "chunk of an unknown type was", "chunks of an unknown type were")} skipped.",
        DataAfterEndOfTrack => "The track has data after its end-of-track marker; it was ignored.",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown MIDI issue code."),
    };

    private static string Notes(int count) => Plural(count, "note", "notes");

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";
}