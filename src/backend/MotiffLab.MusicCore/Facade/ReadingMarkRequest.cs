using System.Text.Json;

using MotiffLab.MusicCore.Import.Scan;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The player's marks «что не так» on the working notes when they were read from a PDF or photos («Как напечатано», step 3;
/// within contract 16, calls added; project file 18): check.mark and check.unmark, each answering as check does, and the
/// check answer's "marks". The next reading of the pages uses them (sources.notes.recognise, <see cref="ReadingMarks"/>).
/// </summary>
internal static class ReadingMarkRequest
{
    /// <summary>
    /// check.mark: {"kind": "meter", "key", "clef", "octave", "triplets", "hands" or "badlyRead", "bar", "last" (optional,
    /// the bar itself by default; a meter or key mark has none), "staff" (a clef or octave mark's, from 1), "value" (a meter's
    /// "5/4", a key's sharps or flats "-7" to "7", a clef's "treble" or "bass", an octave's "8va" or "8vb", triplets' "yes"
    /// or "no")}: the mark kept with the working notes as <see cref="ScanOrigin.WithMark"/> keeps it (in the place of one of its kind at that bar and staff, joined with those it overlaps). Refused
    /// ("unavailable") on notes not read from a PDF or photos; "badRequest" for bars outside the piece or a value its kind
    /// cannot say.
    /// </summary>
    public static Project Mark(Project project, ImportedPiece piece, Request request)
    {
        ScanOrigin scan = Scanned(project);
        ReadingMarkKind kind = request.Enum<ReadingMarkKind>("kind") ?? throw FacadeException.BadRequest("The request needs \"kind\".");
        int bar = request.RequiredInt("bar");
        int last = request.Int("last") ?? bar;
        int first = piece.Meter.BarAt(Tick.Zero).Number;
        int end = piece.Meter.BarAt(new Tick(Math.Max(0, piece.End.Value - 1))).Number;
        if (bar < first || last > end)
        {
            throw FacadeException.BadRequest($"The piece has bars {first} to {end}.");
        }

        // A value its kind cannot say is an ArgumentException, which the change answers as "badRequest".
        return project with { Scan = scan.WithMark(new ReadingMark(kind, bar, last, request.Int("staff"), request.Text("value"))) };
    }

    /// <summary>check.unmark: {"kind", "bar", "staff" (a clef or octave mark's)}: that mark taken away; "badRequest" when there is none.</summary>
    public static Project Unmark(Project project, Request request)
    {
        ScanOrigin scan = Scanned(project);
        ReadingMarkKind kind = request.Enum<ReadingMarkKind>("kind") ?? throw FacadeException.BadRequest("The request needs \"kind\".");
        int bar = request.RequiredInt("bar");
        int? staff = request.Int("staff");
        ScanOrigin without = scan.WithoutMark(kind, bar, staff);
        return without.Marks.Count < scan.Marks.Count
            ? project with { Scan = without }
            : throw FacadeException.BadRequest($"There is no such mark at bar {bar}.");
    }

    /// <summary>
    /// "marks" in the check answer: the working notes' marks in bar order, each as the file keeps it ({"kind", "bar", "last",
    /// "staff" and "value" when it has them}) with "text"; empty for notes not read from a PDF or photos.
    /// </summary>
    public static void Write(Utf8JsonWriter json, Project project, Words words)
    {
        json.WriteStartArray("marks");
        foreach (ReadingMark mark in project.Scan?.Marks ?? [])
        {
            json.WriteStartObject();
            ProjectFile.WriteReadingMark(json, mark);
            json.WriteString("text", mark.Text(words));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static ScanOrigin Scanned(Project project) =>
        project.Scan ?? throw FacadeException.Unavailable("Only notes read from a PDF or photos take marks «что не так»; correct other notes instead.");
}