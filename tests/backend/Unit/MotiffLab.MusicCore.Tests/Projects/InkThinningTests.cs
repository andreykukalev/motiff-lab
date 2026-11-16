using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Projects;

/// <summary>Stroke points thinned before the marks keep them (v0.4): the same shape to a twentieth of a staff space, in fewer bytes.</summary>
public class InkThinningTests
{
    private static InkStroke Stroke(string id, IEnumerable<(double X, double Y, double Pressure)> points, double pressure = 0.5) =>
        new(id, InkTool.Pen, InkColour.Graphite, 0.16, Tick.Zero, 0.25, 1.5, [.. points.SelectMany(static point => new[] { point.X, point.Y, point.Pressure })], pressure);

    private static double Round(double value) => Math.Round(value * 1000) / 1000;

    /// <summary>
    /// A curve as the web app hands one in: a point every 0.075 staff spaces (0.75 px at 10 px a space), rounded to three
    /// places as pencil-marks.ts keeps them, its pressure easing up and down.
    /// </summary>
    private static InkStroke Written(string id = "k1")
    {
        const double step = 0.075;
        IEnumerable<(double, double, double)> points = Enumerable.Range(1, 120)
            .Select(static i => i * step)
            .Select(static t => (Round(t), Round(0.8 * Math.Sin(t)), Round(0.5 + (0.15 * Math.Sin(t / 3)))));
        return Stroke(id, points);
    }

    private static List<(double X, double Y, double Pressure)> Line(InkStroke stroke) =>
    [
        (0, 0, stroke.Pressure),
        .. stroke.Points.Chunk(3).Select(static point => (point[0], point[1], point[2])),
    ];

    private static double DistanceToLine(List<(double X, double Y, double Pressure)> line, (double X, double Y, double Pressure) point) =>
        line.Zip(line.Skip(1)).Min(segment =>
        {
            ((double ax, double ay, _), (double bx, double by, _)) = segment;
            double dx = bx - ax;
            double dy = by - ay;
            double length = (dx * dx) + (dy * dy);
            double t = length == 0 ? 0 : Math.Clamp((((point.X - ax) * dx) + ((point.Y - ay) * dy)) / length, 0, 1);
            return Math.Sqrt(Math.Pow(ax + (t * dx) - point.X, 2) + Math.Pow(ay + (t * dy) - point.Y, 2));
        });

    [Fact]
    public void A_curve_keeps_its_shape_to_a_twentieth_of_a_staff_space_in_a_third_of_the_bytes_or_less()
    {
        InkStroke written = Written();
        InkStroke thinned = written.Thinned();

        int before = ProjectFile.WriteInk([written]).Length;
        int after = ProjectFile.WriteInk([thinned]).Length;
        Assert.True(before >= 3 * after, $"{before} bytes thinned to {after}");

        List<(double X, double Y, double Pressure)> line = Line(thinned);
        Assert.All(Line(written), point => Assert.True(DistanceToLine(line, point) <= InkThinning.Tolerance + 1e-9, $"{point} is off the line"));
        // The first point and the last stay; every point kept is one written, as it was.
        Assert.Equal(written.Points.TakeLast(3), thinned.Points.TakeLast(3));
        Assert.All(thinned.Points.Chunk(3), point => Assert.Contains(written.Points.Chunk(3), each => each.SequenceEqual(point)));
        Assert.Equal((written.Id, written.Bar, written.U, written.V, written.Pressure, written.Size), (thinned.Id, thinned.Bar, thinned.U, thinned.V, thinned.Pressure, thinned.Size));
    }

    [Fact]
    public void Thinning_gives_the_same_points_every_time_and_leaves_a_thinned_stroke_as_it_is()
    {
        InkStroke once = Written().Thinned();
        Assert.Equal(once, Written().Thinned());
        Assert.Equal(once, once.Thinned());
        Assert.Same(once, once.Thinned());
    }

    [Fact]
    public void A_straight_line_keeps_its_ends_and_a_corner_a_turn_back_or_a_firmer_press_keep_their_point()
    {
        InkStroke straight = Stroke("s", Enumerable.Range(1, 50).Select(static i => (i * 0.1, i * 0.05, 0.5)));
        Assert.Equal([5.0, 2.5, 0.5], straight.Thinned().Points);

        InkStroke corner = Stroke("c", [(0.5, 0.01, 0.5), (1, 0, 0.5), (1, 0.5, 0.5), (1.01, 1, 0.5)]);
        Assert.Equal([1.0, 0, 0.5, 1.01, 1, 0.5], corner.Thinned().Points);

        // Out along a line and back: the far end is kept, though it lies on the line through both ends.
        InkStroke back = Stroke("b", [(1, 0, 0.5), (2, 0, 0.5), (1, 0, 0.5), (0.5, 0, 0.5)]);
        Assert.Equal([2.0, 0, 0.5, 0.5, 0, 0.5], back.Thinned().Points);

        // Pressed harder in the middle of a straight line: the pen is thicker there, so the point stays, with the points
        // either side that shape the press.
        InkStroke pressed = Stroke("p", [(1, 0, 0.5), (2, 0, 0.9), (3, 0, 0.5), (4, 0, 0.5)]);
        Assert.Same(pressed, pressed.Thinned());
        // A pressure that only eases along the line is drawn the same without the points between.
        InkStroke easing = Stroke("e", [(1, 0, 0.6), (2, 0, 0.7), (3, 0, 0.8)]);
        Assert.Equal([3.0, 0, 0.8], easing.Thinned().Points);

        // A dot and a stroke of two points have nothing to leave out.
        InkStroke dot = Stroke("d", []);
        Assert.Same(dot, dot.Thinned());
        InkStroke two = Stroke("t", [(1, 1, 0.5)]);
        Assert.Same(two, two.Thinned());
    }

    [Fact]
    public void The_marks_keep_strokes_thinned_however_they_come_in_and_a_file_reads_back_as_it_was_written()
    {
        InkStroke written = Written("k1");
        InkStroke other = Written("k2");

        // The iPad adds a stroke at a time through the facade (ink.add); the web hands back the whole list after a change.
        Marks added = Marks.Empty.AddInk([written]).AddInk([other]);
        Marks handedBack = Marks.Empty.WithInk([written, other]);
        Assert.Equal(added, handedBack);
        Assert.All(added.Ink, stroke => Assert.Equal(written.Thinned().Points, stroke.Points));

        // A file another build wrote with every point opens with every point, and is thinned only when the ink changes.
        byte[] source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "importer-grand-staff.musicxml"));
        Project project = Project.Create("p_test", "minuet.musicxml", source, DateTimeOffset.UnixEpoch) with { Marks = Marks.Empty.WithInkAsWritten([written]) };
        Project read = ProjectFile.Read(ProjectFile.Write(project));
        Assert.Equal(written.Points, Assert.Single(read.Marks.Ink).Points);
        Assert.Equal(ProjectFile.Write(project), ProjectFile.Write(read));
        Assert.Equal(written.Thinned().Points, read.ChangeMarks(marks => marks.AddInk([other])).Marks.Ink[0].Points);
    }
}