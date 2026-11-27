using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Import.MusicXml;

// What a part reader collects from a part, in document order (PartReader), before the parts are aligned.

/// <summary>A sounding note as written, before ties are merged: its place in the part, pitch, staff, voice, marks and how it is written.</summary>
internal sealed record PartNote(
    int MeasureIndex,
    int NoteIndex,
    QuarterFraction Offset,
    QuarterFraction Duration,
    int Pitch,
    int? Velocity,
    int Staff,
    string Voice,
    bool TieStart,
    bool TieStop,
    AccentKind? Accent,
    MusicXmlWriting Writing);

/// <summary>A grace note, at the cursor before the note it ornaments.</summary>
internal sealed record PartGraceNote(int MeasureIndex, int NoteIndex, QuarterFraction Offset, int Pitch, int Staff, string Voice, bool IsSlashed);

/// <summary>A rest, or a whole-measure rest, and how it is written.</summary>
internal sealed record PartRest(int MeasureIndex, int NoteIndex, QuarterFraction Offset, QuarterFraction Duration, int Staff, string Voice, bool IsMeasureRest, MusicXmlWriting Writing);

/// <summary>A <c>&lt;forward&gt;</c> that names its voice: time the voice skips without a sign.</summary>
internal sealed record PartSpace(int MeasureIndex, QuarterFraction Offset, QuarterFraction Duration, int Staff, string Voice);

/// <summary>A slur start or stop on a note, at the note's onset.</summary>
internal sealed record PartSlurMark(int MeasureIndex, QuarterFraction Offset, int Number, bool IsStart);

/// <summary>A repeat sign on the left (start) or right (end) bar line of a measure.</summary>
internal sealed record PartRepeat(int MeasureIndex, bool IsAtEnd, MusicXmlRepeatDirection Direction);

/// <summary>A bar line drawn other than single (its <c>&lt;bar-style&gt;</c>), on the left or right of a measure.</summary>
internal sealed record PartBarline(int MeasureIndex, bool IsAtEnd, string Style, bool IsRepeat);

/// <summary>The text of a <c>&lt;words&gt;</c> or <c>&lt;rehearsal&gt;</c> direction, at the cursor.</summary>
internal sealed record PartText(int MeasureIndex, QuarterFraction Offset, string Text, bool IsRehearsal, bool IsBold);

/// <summary>
/// A measure of a part: its number as written, whether it is implicit (a pickup), how long its content lasts, and whether a
/// scan merge marked it as one the engines disagree on (<see cref="Pieces.MusicXmlImport.EnginesDisagreeMark"/>).
/// </summary>
internal sealed record MeasureInfo(string? Number, bool IsImplicit, QuarterFraction Length, bool EnginesDisagree)
{
    /// <summary>Whether a scan merge marked it as read again from another engine (<see cref="Pieces.MusicXmlImport.ReadAgainMark"/>).</summary>
    public bool ReadAgain { get; init; }
}