namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>The notation symbols the vector reader understands (decision record 0029), named as SMuFL names them.</summary>
public enum MusicGlyph
{
    /// <summary>A glyph of a music font the reader does not use: a text mark, an ornament, a pedal sign.</summary>
    Other,
    NoteheadBlack,
    NoteheadHalf,
    NoteheadWhole,
    NoteheadDoubleWhole,
    ClefG,
    ClefG8vb,
    ClefG8va,
    ClefF,
    ClefF8vb,
    ClefC,
    AccidentalSharp,
    AccidentalFlat,
    AccidentalNatural,
    AccidentalDoubleSharp,
    AccidentalDoubleFlat,
    AugmentationDot,
    Flag8thUp,
    Flag8thDown,
    Flag16thUp,
    Flag16thDown,
    Flag32ndUp,
    Flag32ndDown,
    Flag64thUp,
    Flag64thDown,
    RestDoubleWhole,
    RestWhole,
    RestHalf,
    RestQuarter,
    Rest8th,
    Rest16th,
    Rest32nd,
    Rest64th,
    TimeSig0,
    TimeSig1,
    TimeSig2,
    TimeSig3,
    TimeSig4,
    TimeSig5,
    TimeSig6,
    TimeSig7,
    TimeSig8,
    TimeSig9,
    TimeSigCommon,
    TimeSigCut,
}

/// <summary>What the reader needs to know of each symbol.</summary>
internal static class MusicGlyphs
{
    public static bool IsNotehead(this MusicGlyph glyph) => glyph is MusicGlyph.NoteheadBlack or MusicGlyph.NoteheadHalf or MusicGlyph.NoteheadWhole or MusicGlyph.NoteheadDoubleWhole;

    public static bool IsClef(this MusicGlyph glyph) => glyph is >= MusicGlyph.ClefG and <= MusicGlyph.ClefC;

    public static bool IsAccidental(this MusicGlyph glyph) => glyph is >= MusicGlyph.AccidentalSharp and <= MusicGlyph.AccidentalDoubleFlat;

    public static bool IsFlag(this MusicGlyph glyph) => glyph is >= MusicGlyph.Flag8thUp and <= MusicGlyph.Flag64thDown;

    public static bool IsRest(this MusicGlyph glyph) => glyph is >= MusicGlyph.RestDoubleWhole and <= MusicGlyph.Rest64th;

    public static bool IsDigit(this MusicGlyph glyph) => glyph is >= MusicGlyph.TimeSig0 and <= MusicGlyph.TimeSig9;

    public static int Digit(this MusicGlyph glyph) => glyph - MusicGlyph.TimeSig0;

    /// <summary>The beams a flag stands for: 1 for an eighth's, 4 for a 64th's.</summary>
    public static int FlagCount(this MusicGlyph glyph) => ((glyph - MusicGlyph.Flag8thUp) / 2) + 1;

    /// <summary>How long a notehead lasts before flags, beams and dots, in ticks (480 a quarter).</summary>
    public static int HeadTicks(this MusicGlyph glyph) => glyph switch
    {
        MusicGlyph.NoteheadDoubleWhole => 3840,
        MusicGlyph.NoteheadWhole => 1920,
        MusicGlyph.NoteheadHalf => 960,
        _ => 480,
    };

    /// <summary>How long a rest lasts, in ticks (480 a quarter); a whole rest that fills its bar is sized by the bar.</summary>
    public static int RestTicks(this MusicGlyph glyph) => glyph switch
    {
        MusicGlyph.RestDoubleWhole => 3840,
        MusicGlyph.RestWhole => 1920,
        MusicGlyph.RestHalf => 960,
        MusicGlyph.RestQuarter => 480,
        MusicGlyph.Rest8th => 240,
        MusicGlyph.Rest16th => 120,
        MusicGlyph.Rest32nd => 60,
        _ => 30,
    };

    /// <summary>The alteration an accidental writes, in semitones.</summary>
    public static int Alter(this MusicGlyph glyph) => glyph switch
    {
        MusicGlyph.AccidentalSharp => 1,
        MusicGlyph.AccidentalFlat => -1,
        MusicGlyph.AccidentalDoubleSharp => 2,
        MusicGlyph.AccidentalDoubleFlat => -2,
        _ => 0,
    };
}