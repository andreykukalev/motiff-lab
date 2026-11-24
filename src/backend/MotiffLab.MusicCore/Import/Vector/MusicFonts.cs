using System.Collections.Frozen;

namespace MotiffLab.MusicCore.Import.Vector;

/// <summary>
/// The music fonts the vector reader knows (decision record 0029), each with how its glyphs are named: SMuFL fonts by
/// their standard code points, MuseScore 2's MScore by its own private-use code points (its font's glyphnames.json),
/// LilyPond's Emmentaler by the glyph names its embedded font keeps, the legacy fonts of Sibelius and Finale by the
/// characters of Maestro's layout, which they share. A font of another notation program is not known: its page is read by
/// the scan server instead.
/// </summary>
internal static class MusicFonts
{
    /// <summary>How a font's glyphs are told apart.</summary>
    public enum Family
    {
        /// <summary>Not a music font: text, lyrics, titles.</summary>
        None,

        /// <summary>A music font the reader cannot read yet (Sibelius's percussion font Tamburo, for example).</summary>
        Unknown,
        Smufl,
        MScore,
        Emmentaler,

        /// <summary>A legacy font of Sibelius or Finale (Opus, Petrucci, Maestro and their family), in Maestro's layout.</summary>
        Legacy,

        /// <summary>
        /// A legacy font's companion of special symbols (Opus Special, Helsinki Special), not in Maestro's layout: only its
        /// augmentation dot is read, so none of its characters can change a pitch or a meter.
        /// </summary>
        LegacySpecial,
    }

    private static readonly string[] s_smufl = ["Bravura", "Leland", "Petaluma", "FinaleMaestro", "FinaleBroadway", "FinaleJazz", "FinaleAsh", "FinaleEngraver", "FinaleLegacy", "MuseJazz", "Sebastian", "Gootville", "Leipzig"];

    private static readonly string[] s_legacy = ["Petrucci", "Maestro", "Opus", "Helsinki", "Engraver", "Jazz", "Reprise", "Inkpen", "Broadway", "Sonata", "Golden", "November"];

    private static readonly string[] s_unknownMusic = ["Tamburo"];

    // A legacy font's companions of chord symbols, figured bass, function symbols and percussion print letters and digits
    // where Maestro's layout has clefs, meters and accidentals: not read, as Tamburo.
    private static readonly string[] s_legacyCompanions = ["Chord", "Figured", "Function", "Percussion"];

    /// <summary>The family of a PDF font, from its base name without the subset prefix ("ABCDEF+MScoreRegular").</summary>
    public static Family Of(string fontName)
    {
        string name = fontName.Contains('+', StringComparison.Ordinal) ? fontName[(fontName.IndexOf('+', StringComparison.Ordinal) + 1)..] : fontName;
        string squeezed = name.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);

        // A font's text companion ("MScoreText", "Bravura Text") draws tempo marks and dynamics inside words: not read.
        if (squeezed.Contains("Text", StringComparison.OrdinalIgnoreCase) || squeezed.Contains("Brace", StringComparison.OrdinalIgnoreCase))
        {
            return Family.None;
        }

        if (squeezed.StartsWith("Emmentaler", StringComparison.OrdinalIgnoreCase))
        {
            return Family.Emmentaler;
        }

        if (squeezed.StartsWith("MScore", StringComparison.OrdinalIgnoreCase))
        {
            return Family.MScore;
        }

        if (s_smufl.Any(family => squeezed.StartsWith(family, StringComparison.OrdinalIgnoreCase)))
        {
            return Family.Smufl;
        }

        if (s_unknownMusic.Any(family => squeezed.Contains(family, StringComparison.OrdinalIgnoreCase)))
        {
            return Family.Unknown;
        }

        if (!s_legacy.Any(family => squeezed.Contains(family, StringComparison.OrdinalIgnoreCase)))
        {
            return Family.None;
        }

        if (s_legacyCompanions.Any(companion => squeezed.Contains(companion, StringComparison.OrdinalIgnoreCase)))
        {
            return Family.Unknown;
        }

        return squeezed.Contains("Special", StringComparison.OrdinalIgnoreCase) ? Family.LegacySpecial : Family.Legacy;
    }

    /// <summary>
    /// The symbol of a code point in a SMuFL font, MuseScore 2's MScore or a legacy font; <see cref="MusicGlyph.Other"/> for
    /// one not read. A legacy font embedded as a symbol font gives its characters at 0xF000 and up (0xF0CF for 0xCF).
    /// </summary>
    public static MusicGlyph ByCodePoint(Family family, int codePoint) => family switch
    {
        Family.MScore => s_mscore.GetValueOrDefault(codePoint, MusicGlyph.Other),
        Family.Legacy => s_legacyCodes.GetValueOrDefault(codePoint is >= 0xF020 and <= 0xF0FF ? codePoint - 0xF000 : codePoint, MusicGlyph.Other),
        Family.LegacySpecial => ByCodePoint(Family.Legacy, codePoint) is MusicGlyph.AugmentationDot ? MusicGlyph.AugmentationDot : MusicGlyph.Other,
        _ => s_smuflCodes.GetValueOrDefault(codePoint, MusicGlyph.Other),
    };

    /// <summary>Whether a family is a legacy font of Sibelius or Finale, whose advance runs wider than its glyphs.</summary>
    public static bool IsLegacy(this Family family) => family is Family.Legacy or Family.LegacySpecial;

    /// <summary>The symbol of an Emmentaler glyph name, as LilyPond names it ("noteheads.s2"); <see cref="MusicGlyph.Other"/> for one not read.</summary>
    public static MusicGlyph ByEmmentalerName(string name) => s_emmentaler.GetValueOrDefault(name, MusicGlyph.Other);

    private static readonly FrozenDictionary<int, MusicGlyph> s_smuflCodes = new Dictionary<int, MusicGlyph>
    {
        [0xE050] = MusicGlyph.ClefG,
        [0xE052] = MusicGlyph.ClefG8vb,
        [0xE053] = MusicGlyph.ClefG8va,
        [0xE05C] = MusicGlyph.ClefC,
        [0xE062] = MusicGlyph.ClefF,
        [0xE064] = MusicGlyph.ClefF8vb,
        [0xE080] = MusicGlyph.TimeSig0,
        [0xE081] = MusicGlyph.TimeSig1,
        [0xE082] = MusicGlyph.TimeSig2,
        [0xE083] = MusicGlyph.TimeSig3,
        [0xE084] = MusicGlyph.TimeSig4,
        [0xE085] = MusicGlyph.TimeSig5,
        [0xE086] = MusicGlyph.TimeSig6,
        [0xE087] = MusicGlyph.TimeSig7,
        [0xE088] = MusicGlyph.TimeSig8,
        [0xE089] = MusicGlyph.TimeSig9,
        [0xE08A] = MusicGlyph.TimeSigCommon,
        [0xE08B] = MusicGlyph.TimeSigCut,
        [0xE0A0] = MusicGlyph.NoteheadDoubleWhole,
        [0xE0A2] = MusicGlyph.NoteheadWhole,
        [0xE0A3] = MusicGlyph.NoteheadHalf,
        [0xE0A4] = MusicGlyph.NoteheadBlack,
        [0xE1E7] = MusicGlyph.AugmentationDot,
        [0xE240] = MusicGlyph.Flag8thUp,
        [0xE241] = MusicGlyph.Flag8thDown,
        [0xE242] = MusicGlyph.Flag16thUp,
        [0xE243] = MusicGlyph.Flag16thDown,
        [0xE244] = MusicGlyph.Flag32ndUp,
        [0xE245] = MusicGlyph.Flag32ndDown,
        [0xE246] = MusicGlyph.Flag64thUp,
        [0xE247] = MusicGlyph.Flag64thDown,
        [0xE260] = MusicGlyph.AccidentalFlat,
        [0xE261] = MusicGlyph.AccidentalNatural,
        [0xE262] = MusicGlyph.AccidentalSharp,
        [0xE263] = MusicGlyph.AccidentalDoubleSharp,
        [0xE264] = MusicGlyph.AccidentalDoubleFlat,
        [0xE4E2] = MusicGlyph.RestDoubleWhole,
        [0xE4E3] = MusicGlyph.RestWhole,
        [0xE4E4] = MusicGlyph.RestHalf,
        [0xE4E5] = MusicGlyph.RestQuarter,
        [0xE4E6] = MusicGlyph.Rest8th,
        [0xE4E7] = MusicGlyph.Rest16th,
        [0xE4E8] = MusicGlyph.Rest32nd,
        [0xE4E9] = MusicGlyph.Rest64th,
        [0xE4F4] = MusicGlyph.RestWhole,
        [0xE4F5] = MusicGlyph.RestHalf,
    }.ToFrozenDictionary();

    // MuseScore 2.x's MScore font (fonts/mscore/glyphnames.json of MuseScore 2.2.1): SMuFL names at its own code points; the
    // time signature digits are plain ASCII digits.
    private static readonly FrozenDictionary<int, MusicGlyph> s_mscore = new Dictionary<int, MusicGlyph>
    {
        [0xE19E] = MusicGlyph.ClefG,
        [0xE1D7] = MusicGlyph.ClefG8vb,
        [0xE1D8] = MusicGlyph.ClefG8va,
        [0xE19A] = MusicGlyph.ClefC,
        [0xE19C] = MusicGlyph.ClefF,
        [0xE1DB] = MusicGlyph.ClefF8vb,
        [0x30] = MusicGlyph.TimeSig0,
        [0x31] = MusicGlyph.TimeSig1,
        [0x32] = MusicGlyph.TimeSig2,
        [0x33] = MusicGlyph.TimeSig3,
        [0x34] = MusicGlyph.TimeSig4,
        [0x35] = MusicGlyph.TimeSig5,
        [0x36] = MusicGlyph.TimeSig6,
        [0x37] = MusicGlyph.TimeSig7,
        [0x38] = MusicGlyph.TimeSig8,
        [0x39] = MusicGlyph.TimeSig9,
        [0xE09A] = MusicGlyph.TimeSigCommon,
        [0xE09B] = MusicGlyph.TimeSigCut,
        [0xE12A] = MusicGlyph.NoteheadDoubleWhole,
        [0xE12B] = MusicGlyph.NoteheadWhole,
        [0xE12C] = MusicGlyph.NoteheadHalf,
        [0xE12D] = MusicGlyph.NoteheadBlack,
        [0xE127] = MusicGlyph.AugmentationDot,
        [0xE190] = MusicGlyph.Flag8thUp,
        [0xE194] = MusicGlyph.Flag8thDown,
        [0xE191] = MusicGlyph.Flag16thUp,
        [0xE197] = MusicGlyph.Flag16thDown,
        [0xE192] = MusicGlyph.Flag32ndUp,
        [0xE198] = MusicGlyph.Flag32ndDown,
        [0xE193] = MusicGlyph.Flag64thUp,
        [0xE199] = MusicGlyph.Flag64thDown,
        [0xE114] = MusicGlyph.AccidentalFlat,
        [0xE113] = MusicGlyph.AccidentalNatural,
        [0xE10E] = MusicGlyph.AccidentalSharp,
        [0xE11C] = MusicGlyph.AccidentalDoubleSharp,
        [0xE11A] = MusicGlyph.AccidentalDoubleFlat,
        [0xE106] = MusicGlyph.RestDoubleWhole,
        [0xE100] = MusicGlyph.RestWhole,
        [0xE101] = MusicGlyph.RestHalf,
        [0xE107] = MusicGlyph.RestQuarter,
        [0xE109] = MusicGlyph.Rest8th,
        [0xE10A] = MusicGlyph.Rest16th,
        [0xE10B] = MusicGlyph.Rest32nd,
        [0xE10C] = MusicGlyph.Rest64th,
        [0xE102] = MusicGlyph.RestWhole,
        [0xE103] = MusicGlyph.RestHalf,
    }.ToFrozenDictionary();

    // The legacy fonts of Sibelius and Finale (Opus, Petrucci, Maestro, ...), by the characters of Maestro's layout they
    // share; the dot of Opus Special comes as the trademark sign.
    private static readonly FrozenDictionary<int, MusicGlyph> s_legacyCodes = new Dictionary<int, MusicGlyph>
    {
        ['&'] = MusicGlyph.ClefG,
        ['V'] = MusicGlyph.ClefG8vb,
        ['B'] = MusicGlyph.ClefC,
        ['?'] = MusicGlyph.ClefF,
        ['0'] = MusicGlyph.TimeSig0,
        ['1'] = MusicGlyph.TimeSig1,
        ['2'] = MusicGlyph.TimeSig2,
        ['3'] = MusicGlyph.TimeSig3,
        ['4'] = MusicGlyph.TimeSig4,
        ['5'] = MusicGlyph.TimeSig5,
        ['6'] = MusicGlyph.TimeSig6,
        ['7'] = MusicGlyph.TimeSig7,
        ['8'] = MusicGlyph.TimeSig8,
        ['9'] = MusicGlyph.TimeSig9,
        ['c'] = MusicGlyph.TimeSigCommon,
        ['C'] = MusicGlyph.TimeSigCut,
        ['W'] = MusicGlyph.NoteheadDoubleWhole,
        ['w'] = MusicGlyph.NoteheadWhole,
        [0xFA] = MusicGlyph.NoteheadHalf,
        [0xCF] = MusicGlyph.NoteheadBlack,
        ['.'] = MusicGlyph.AugmentationDot,
        ['k'] = MusicGlyph.AugmentationDot,
        [0x2122] = MusicGlyph.AugmentationDot,
        ['J'] = MusicGlyph.Flag8thUp,
        ['j'] = MusicGlyph.Flag8thDown,
        ['R'] = MusicGlyph.Flag16thUp,
        ['r'] = MusicGlyph.Flag16thDown,
        ['b'] = MusicGlyph.AccidentalFlat,
        ['n'] = MusicGlyph.AccidentalNatural,
        ['#'] = MusicGlyph.AccidentalSharp,
        [0xDC] = MusicGlyph.AccidentalDoubleSharp,
        [0xBA] = MusicGlyph.AccidentalDoubleFlat,
        [0xB7] = MusicGlyph.RestWhole,
        [0xE3] = MusicGlyph.RestHalf,
        [0xCE] = MusicGlyph.RestQuarter,
        [0xE4] = MusicGlyph.Rest8th,
        [0xC5] = MusicGlyph.Rest16th,
        [0xA8] = MusicGlyph.Rest32nd,
    }.ToFrozenDictionary();

    // LilyPond's Emmentaler, by the glyph names its subset keeps in the PDF.
    private static readonly FrozenDictionary<string, MusicGlyph> s_emmentaler = new Dictionary<string, MusicGlyph>(StringComparer.Ordinal)
    {
        ["noteheads.sM1"] = MusicGlyph.NoteheadDoubleWhole,
        ["noteheads.s0"] = MusicGlyph.NoteheadWhole,
        ["noteheads.s1"] = MusicGlyph.NoteheadHalf,
        ["noteheads.s2"] = MusicGlyph.NoteheadBlack,
        ["clefs.G"] = MusicGlyph.ClefG,
        ["clefs.G_change"] = MusicGlyph.ClefG,
        ["clefs.F"] = MusicGlyph.ClefF,
        ["clefs.F_change"] = MusicGlyph.ClefF,
        ["clefs.C"] = MusicGlyph.ClefC,
        ["clefs.C_change"] = MusicGlyph.ClefC,
        ["accidentals.sharp"] = MusicGlyph.AccidentalSharp,
        ["accidentals.flat"] = MusicGlyph.AccidentalFlat,
        ["accidentals.natural"] = MusicGlyph.AccidentalNatural,
        ["accidentals.doublesharp"] = MusicGlyph.AccidentalDoubleSharp,
        ["accidentals.flatflat"] = MusicGlyph.AccidentalDoubleFlat,
        ["dots.dot"] = MusicGlyph.AugmentationDot,
        ["flags.u3"] = MusicGlyph.Flag8thUp,
        ["flags.d3"] = MusicGlyph.Flag8thDown,
        ["flags.u4"] = MusicGlyph.Flag16thUp,
        ["flags.d4"] = MusicGlyph.Flag16thDown,
        ["flags.u5"] = MusicGlyph.Flag32ndUp,
        ["flags.d5"] = MusicGlyph.Flag32ndDown,
        ["flags.u6"] = MusicGlyph.Flag64thUp,
        ["flags.d6"] = MusicGlyph.Flag64thDown,
        ["rests.M1"] = MusicGlyph.RestDoubleWhole,
        ["rests.0"] = MusicGlyph.RestWhole,
        ["rests.0o"] = MusicGlyph.RestWhole,
        ["rests.1"] = MusicGlyph.RestHalf,
        ["rests.1o"] = MusicGlyph.RestHalf,
        ["rests.2"] = MusicGlyph.RestQuarter,
        ["rests.3"] = MusicGlyph.Rest8th,
        ["rests.4"] = MusicGlyph.Rest16th,
        ["rests.5"] = MusicGlyph.Rest32nd,
        ["rests.6"] = MusicGlyph.Rest64th,
        ["zero"] = MusicGlyph.TimeSig0,
        ["one"] = MusicGlyph.TimeSig1,
        ["two"] = MusicGlyph.TimeSig2,
        ["three"] = MusicGlyph.TimeSig3,
        ["four"] = MusicGlyph.TimeSig4,
        ["five"] = MusicGlyph.TimeSig5,
        ["six"] = MusicGlyph.TimeSig6,
        ["seven"] = MusicGlyph.TimeSig7,
        ["eight"] = MusicGlyph.TimeSig8,
        ["nine"] = MusicGlyph.TimeSig9,
        ["timesig.C44"] = MusicGlyph.TimeSigCommon,
        ["timesig.C22"] = MusicGlyph.TimeSigCut,
    }.ToFrozenDictionary();
}