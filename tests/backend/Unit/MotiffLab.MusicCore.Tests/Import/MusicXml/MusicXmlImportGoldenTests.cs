using System.Text;

using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Tests.Golden;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Import.MusicXml;

/// <summary>
/// What the importer reads from real scores, kept character for character (Golden/*.txt): every note, source note,
/// grace note, rest, slur, repeat, bar line, text, measure, tempo, time and key signature, the bars of the meter and the
/// report. A change to how a score is read shows here line by line; with MOTIFF_UPDATE_SNAPSHOTS=1 the goldens are
/// written again, when it changes on purpose.
/// </summary>
public class MusicXmlImportGoldenTests
{
    [Theory]
    [InlineData("MusicXml", "grand-staff.musicxml")]
    [InlineData("MusicXml", "grand-staff.mxl")]
    [InlineData("MusicXml", "importer-grand-staff.musicxml")]
    [InlineData("MusicXml", "importer-septuplets.musicxml")]
    [InlineData("MusicXml", "importer-two-parts.musicxml")]
    [InlineData("MusicXml", "phrases-repeats.musicxml")]
    [InlineData("MusicXml", "septuplets.musicxml")]
    [InlineData("MusicXml", "ties-triplets-pickup.musicxml")]
    [InlineData("Scan", "homr-meter-change-mid-page.musicxml")]
    [InlineData("Scan", "homr-grace-eighths-4-4.musicxml")]
    public void A_score_is_read_as_before(string folder, string fixture)
    {
        MusicXmlImport import = MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", folder, "Fixtures", fixture)));

        AssertIs(fixture, Describe(import));
    }

    [Theory]
    [InlineData("fur-elise-original.musicxml")]
    [InlineData("ode-to-joy.musicxml")]
    public void An_experiment_s_score_is_read_as_before(string fixture)
    {
        MusicXmlImport import = MusicXmlImporter.Import(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", fixture)));

        AssertIs(fixture, Describe(import));
    }

    /// <summary>
    /// That <paramref name="written"/> is the golden (<see cref="TextGoldens.AssertTextGolden"/>). A compressed score reads
    /// as the same score uncompressed: one golden for both, written by the uncompressed one.
    /// </summary>
    private static void AssertIs(string fixture, string written) => TextGoldens.AssertTextGolden(
        Path.Combine("Import", "MusicXml"), Path.GetFileNameWithoutExtension(fixture) + ".txt", written, writes: !fixture.EndsWith(".mxl", StringComparison.Ordinal));

    /// <summary>Everything the import holds, a section a kind and a line an item.</summary>
    private static string Describe(MusicXmlImport import)
    {
        var text = new StringBuilder();
        void Section<T>(string name, IEnumerable<T> items)
        {
            text.Append("## ").Append(name).Append('\n');
            foreach (T item in items)
            {
                text.Append(item).Append('\n');
            }
        }

        text.Append("version: ").Append(import.Version).Append('\n');
        text.Append("end: ").Append(import.End).Append('\n');
        text.Append("pickup: ").Append(import.Meter.PickupDuration).Append('\n');
        ImportReport report = import.Report;
        text.Append("report: ").Append(report.Format).Append(", ").Append(report.TrackCount).Append(" tracks, ")
            .Append(report.NoteCount).Append(" notes, ").Append(report.MeasureCount).Append(" measures\n");
        Section("issues", report.Issues);
        Section("parts", import.Parts);
        Section("bars", import.Meter.BarsBefore(import.End));
        Section("measures", import.Measures);
        Section("time signatures", import.TimeSignatures);
        Section("key signatures", import.KeySignatures);
        Section("tempos", import.Tempos);
        Section("repeats", import.Repeats);
        Section("bar lines", import.Barlines);
        Section("texts", import.Texts);
        Section("slurs", import.Slurs);
        Section("rests", import.Rests);
        Section("grace notes", import.GraceNotes);
        Section("source notes", import.SourceNotes);
        Section("written notes", import.WrittenNotes);
        Section("spaces", import.Spaces);
        Section("notes", import.Notes);
        return text.ToString();
    }
}