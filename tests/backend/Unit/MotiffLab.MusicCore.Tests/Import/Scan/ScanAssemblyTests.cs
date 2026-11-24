using System.Text.Json;

using MotiffLab.MusicCore.Import.Scan;

namespace MotiffLab.MusicCore.Tests.Import.Scan;

/// <summary>
/// music-core's own assembly of a staff of a bar from the detector's symbols (ADR 0035, section 5, step 3; v1.3, c-1b),
/// on synthetic evidence: one system of two staves, a staff space of 10 pixels, each bar 100 pixels wide.
/// </summary>
public class ScanAssemblyTests
{
    private const double Space = 10;

    /// <summary>A page's evidence of one system of two staves, with <paramref name="bars"/> bars and the symbols given.</summary>
    internal static ScanEvidence Evidence(int bars, params object[] symbols) => ScanEvidence.Parse(EvidenceJson(bars, symbols));

    /// <summary>The evidence of <see cref="Evidence"/> as the scan server writes it.</summary>
    internal static string EvidenceJson(int bars, params object[] symbols) => JsonSerializer.Serialize(new
    {
        evidence = 1,
        page = 1,
        detector = new { name = "copisteria", version = "test" },
        image = new { width = 1000, height = 400, dpi = 300, rotated = 0, deskewed = 0 },
        systems = new[]
        {
            new
            {
                box = new[] { 0.0, 80, bars * 100.0, 220 },
                staves = new[] { Staff(1, 100), Staff(2, 200) },
                bars = Enumerable.Range(0, bars).Select(bar => new { x = new[] { bar * 100.0, (bar * 100.0) + 100 } }),
            },
        },
        symbols,
    });

    private static object Staff(int staff, double middle) =>
        new { staff, lines = new[] { middle - 20, middle - 10, middle, middle + 10, middle + 20 }, space = Space, barLines = Array.Empty<object>() };

    /// <summary>The height of a staff position: the staves' middle lines at 100 and 200, half a space a position.</summary>
    private static double Height(int staff, int position) => (staff * 100.0) - (position * Space / 2);

    /// <summary>A notehead in a bar at <paramref name="x"/> pixels from the bar's start.</summary>
    internal static object Head(int bar, int staff, double x, int position, string duration = "quarter", int dots = 0, double confidence = 0.9)
    {
        double left = (bar * 100.0) + x, y = Height(staff, position);
        string glyph = duration switch { "whole" => "noteheadWhole", "half" => "noteheadHalf", _ => "noteheadBlack" };
        return new { system = 0, bar, staff, @class = glyph, x = left, y, box = new[] { left - 5, y - 4, left + 5, y + 4 }, confidence, position, duration, dots, grace = false };
    }

    /// <summary>Another symbol (a rest, an accidental, a dot, a triplet mark, a clef with its line, a key signature with its fifths).</summary>
    private static object Mark(int bar, int staff, string glyph, double x, int position = 0, int? line = null, int? fifths = null)
    {
        double left = (bar * 100.0) + x, y = Height(staff, position);
        return new { system = 0, bar, staff, @class = glyph, x = left, y, box = new[] { left - 3, y - 3, left + 3, y + 3 }, confidence = 0.9, line, fifths };
    }

    private static IReadOnlyList<(long Onset, int Pitch, long Length)>? Assemble(StaffKey start, params object[] symbols) =>
        ScanAssembly.Staff(Evidence(1, symbols).Bars[0], 1, start, [1920]);

    [Fact]
    public void A_column_starts_when_the_earliest_note_still_sounding_ends()
    {
        IReadOnlyList<(long, int, long)>? notes = Assemble(StaffKey.Treble, Head(0, 1, 10, -2, "half"), Head(0, 1, 10, 0), Head(0, 1, 30, 1), Head(0, 1, 50, 2, "half"));

        Assert.Equal([(0L, 67, 960L), (0L, 71, 480L), (480L, 72, 480L), (960L, 74, 960L)], notes!);
    }

    [Fact]
    public void A_second_s_heads_side_by_side_are_one_column_and_a_head_s_own_dots_count()
    {
        IReadOnlyList<(long, int, long)>? notes = Assemble(StaffKey.Treble, Head(0, 1, 10, 0), Head(0, 1, 17, 1), Head(0, 1, 40, 2, "half", dots: 1));

        Assert.Equal([(0L, 71, 480L), (0L, 72, 480L), (480L, 74, 1440L)], notes!);
    }

    [Fact]
    public void An_augmentation_dot_just_right_of_a_head_lengthens_it_by_half()
    {
        IReadOnlyList<(long, int, long)>? notes = Assemble(StaffKey.Treble, Head(0, 1, 10, 0, "half"), Mark(0, 1, "augmentationDot", 20), Head(0, 1, 40, 1));

        Assert.Equal([(0L, 71, 1440L), (1440L, 72, 480L)], notes!);
    }

    [Fact]
    public void A_triplet_mark_takes_the_three_nearest_columns_of_one_value()
    {
        IReadOnlyList<(long, int, long)>? notes = Assemble(StaffKey.Treble,
            Head(0, 1, 10, 0, "eighth"), Head(0, 1, 20, 2, "eighth"), Head(0, 1, 30, 4, "eighth"), Mark(0, 1, "tuplet3", 20, position: 8), Head(0, 1, 50, 1), Head(0, 1, 70, 3, "half"));

        Assert.Equal([(0L, 71, 160L), (160L, 74, 160L), (320L, 77, 160L), (480L, 72, 480L), (960L, 76, 960L)], notes!);
    }

    [Fact]
    public void An_accidental_holds_for_its_step_until_the_bar_line_and_the_key_alters_the_others()
    {
        var gMajor = new StaffKey(StaffKey.Treble.Middle, 1);

        IReadOnlyList<(long, int, long)>? notes = Assemble(gMajor,
            Mark(0, 1, "accidentalNatural", 12, position: 4), Head(0, 1, 20, 4), Head(0, 1, 40, 4), Head(0, 1, 60, -3), Head(0, 1, 80, 1));

        Assert.Equal([77, 77, 66, 72], notes!.Select(note => note.Item2));
    }

    [Fact]
    public void A_clef_and_a_key_drawn_in_the_bar_hold_from_where_they_stand()
    {
        IReadOnlyList<(long, int, long)>? notes = Assemble(StaffKey.Treble,
            Mark(0, 1, "fClef", 3, position: 2, line: 4), Mark(0, 1, "keySignature", 6, position: 2, fifths: -1), Head(0, 1, 20, -2, "whole"));

        Assert.Equal([(0L, 46, 1920L)], notes!);
    }

    [Fact]
    public void A_whole_rest_lasts_the_bar_and_a_staff_that_does_not_fill_it_is_not_assembled()
    {
        Assert.Empty(ScanAssembly.Staff(Evidence(1, Mark(0, 1, "restWhole", 50)).Bars[0], 1, StaffKey.Treble, [1440])!);
        Assert.Equal([(0L, 71, 960L), (480L, 72, 1440L)], Assemble(StaffKey.Treble, Mark(0, 1, "restQuarter", 10), Head(0, 1, 10, 0, "half"), Head(0, 1, 30, 1, "half", dots: 1))!);
        Assert.Null(Assemble(StaffKey.Treble, Head(0, 1, 10, 0), Head(0, 1, 30, 1), Head(0, 1, 50, 2)));
        Assert.Null(Assemble(StaffKey.Treble));
        Assert.Null(ScanAssembly.Staff(Evidence(1, Head(0, 1, 10, 0, "whole")).Bars[0], 3, StaffKey.Treble, [1920]));
    }

    [Fact]
    public void A_head_found_twice_is_taken_once_and_an_unsure_one_not_at_all()
    {
        IReadOnlyList<(long, int, long)>? notes = Assemble(StaffKey.Treble,
            Head(0, 1, 10, 0, "half", confidence: 0.8), Head(0, 1, 11, 0, "half"), Head(0, 1, 40, 1, "half"), Head(0, 1, 60, 2, confidence: 0.3));

        Assert.Equal([(0L, 71, 960L), (960L, 72, 960L)], notes!);
    }

    [Fact]
    public void Evidence_of_another_version_or_shape_is_refused()
    {
        Assert.Throws<ArgumentException>(() => ScanEvidence.Parse("""{"evidence": 2, "systems": [], "symbols": []}"""));
        Assert.Throws<ArgumentException>(() => ScanEvidence.Parse("""{"evidence": 1, "systems": [{"staves": []}], "symbols": []}"""));
        Assert.Throws<ArgumentException>(() => ScanEvidence.Parse("[1"));
        Assert.Single(ScanEvidence.Parse("""{"evidence": 1, "systems": [{"staves": [], "bars": [{"x": [0, 1]}]}], "symbols": [{"system": 4, "bar": 0}]}""").Bars);
    }
}