namespace MotiffLab.MusicCore.Analysis;

/// <summary>The accent a score writes on a note.</summary>
public enum AccentKind
{
    /// <summary>The accent mark &gt;.</summary>
    Accent,

    /// <summary>The strong accent (marcato) mark ^.</summary>
    StrongAccent,
}

/// <summary>
/// An accent the source writes on a note (S1-A1). MusicXML can state accents; MIDI cannot, so a
/// MIDI piece has no accent marks rather than an empty list of them.
/// </summary>
/// <param name="NoteId">The shared-model note the mark is written on; for tied notes, the first written note.</param>
public sealed record AccentMark(string NoteId, AccentKind Kind);