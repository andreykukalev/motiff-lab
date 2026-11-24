using MotiffLab.MusicCore.Import.Vector;

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.PdfFonts;

namespace MotiffLab.MusicCore.Tests.Import.Vector;

/// <summary>
/// The legacy music fonts of Sibelius and Finale (Opus, Petrucci and their family): their characters in Maestro's layout,
/// also at the symbol font's 0xF0xx codes, the right edge of the glyph drawn rather than of its advance, and a tie that
/// starts after a dotted note's dot. Synthetic glyphs, as the reader's own page holds them.
/// </summary>
public class LegacyFontsTests
{
    private static readonly VectorStaff s_staff = new([100, 104, 108, 112, 116], 40, 300);

    [Fact]
    public void A_legacy_font_is_read_by_its_characters_and_by_its_symbol_codes()
    {
        Assert.Equal(MusicGlyph.NoteheadBlack, MusicFonts.ByCodePoint(MusicFonts.Family.Legacy, 0xCF));
        Assert.Equal(MusicGlyph.NoteheadBlack, MusicFonts.ByCodePoint(MusicFonts.Family.Legacy, 0xF0CF));
        Assert.Equal(MusicGlyph.NoteheadHalf, MusicFonts.ByCodePoint(MusicFonts.Family.Legacy, 0xF0FA));
        Assert.Equal(MusicGlyph.ClefF, MusicFonts.ByCodePoint(MusicFonts.Family.Legacy, 0xF03F));
        Assert.Equal(MusicGlyph.AugmentationDot, MusicFonts.ByCodePoint(MusicFonts.Family.Legacy, 0x2122));
        // The symbol codes are a legacy font's only: a SMuFL font's character there is no notehead.
        Assert.Equal(MusicGlyph.Other, MusicFonts.ByCodePoint(MusicFonts.Family.Smufl, 0xF0CF));
    }

    [Fact]
    public void A_legacy_glyph_ends_where_it_is_drawn_and_another_where_its_advance_ends()
    {
        // An Opus notehead: drawn 6.2 pt wide, advancing 7.6 pt.
        Letter head = Letter("", "ABCDEF+Opus", left: 100, drawnRight: 106.2, advanceRight: 107.6);

        Assert.Equal(106.2, VectorPage.Placed(MusicGlyph.NoteheadBlack, MusicFonts.Family.Legacy, head, 842).Right, 3);
        Assert.Equal(107.6, VectorPage.Placed(MusicGlyph.NoteheadBlack, MusicFonts.Family.Smufl, head, 842).Right, 3);
        Assert.Equal(842 - 700, VectorPage.Placed(MusicGlyph.NoteheadBlack, MusicFonts.Family.Legacy, head, 842).Y, 3);
    }

    [Fact]
    public void A_tie_from_a_dotted_note_starts_after_its_dot()
    {
        // Two whole notes on the middle line; the curve below them starts two spaces after the first head, past its dot.
        PlacedGlyph first = new(MusicGlyph.NoteheadWhole, 60, 108, 16, 66), second = new(MusicGlyph.NoteheadWhole, 120, 108, 16, 126);
        PlacedGlyph dot = new(MusicGlyph.AugmentationDot, 67.5, 106, 16, 68.7);
        Curve tie = new(Left: 71.5, Right: 121, Top: 110, Bottom: 113);

        Assert.Equal(first, TiesOf(tie, first, second, dot).Back.GetValueOrDefault(second));
        // The same curve from an undotted note starts too far from it to be its tie.
        Assert.Empty(TiesOf(tie, first, second, dot: null).Back);
    }

    private static TieMap TiesOf(Curve curve, PlacedGlyph first, PlacedGlyph second, PlacedGlyph? dot)
    {
        var page = new VectorPage { Number = 1, Width = 595, Height = 842 };
        List<PlacedGlyph> glyphs = [first, second, .. dot is null ? Array.Empty<PlacedGlyph>() : [dot]];
        List<VectorEvent> events = new Chords(page, s_staff, 0, [], 16).Read(glyphs);
        return Ties.Find([curve], s_staff, [first, second], events, first.X, 300);
    }

    private static Letter Letter(string value, string font, double left, double drawnRight, double advanceRight)
    {
        var drawn = new PdfRectangle(left, 698, drawnRight, 702);
        return new Letter(value, drawn, drawn, new PdfPoint(left, 700), new PdfPoint(advanceRight, 700), advanceRight - left, 1,
            new FontDetails(font, false, 400, false), TextRenderingMode.Fill, RGBColor.Black, RGBColor.Black, 18, 0);
    }
}