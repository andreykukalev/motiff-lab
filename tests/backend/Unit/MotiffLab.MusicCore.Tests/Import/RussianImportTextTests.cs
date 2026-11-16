using System.Reflection;

using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Midi;
using MotiffLab.MusicCore.Import.MusicXml;
using MotiffLab.MusicCore.Import.Transcription;

namespace MotiffLab.MusicCore.Tests.Import;

/// <summary>Everything an import writes for the player, in Russian as well as English (decision record 0018, preparation item 13).</summary>
public class RussianImportTextTests
{
    private static string Fixture(params string[] path) => Path.Combine([AppContext.BaseDirectory, "Import", .. path]);

    private static IEnumerable<string> Codes(Type codes) => codes
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!);

    private static bool IsRussian(string text) => text.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я');

    public static TheoryData<string> MidiCodes() => [.. Codes(typeof(MidiIssueCodes))];

    public static TheoryData<string> MusicXmlCodes() => [.. Codes(typeof(MusicXmlIssueCodes))];

    [Theory]
    [MemberData(nameof(MidiCodes))]
    public void Every_midi_issue_is_written_in_russian(string code)
    {
        foreach (int count in new[] { 1, 3, 5, 21 })
        {
            string russian = MidiIssueCodes.Describe(code, count, Words.Russian);
            Assert.True(IsRussian(russian), russian);
            Assert.NotEqual(MidiIssueCodes.Describe(code, count), russian);
        }
    }

    [Theory]
    [MemberData(nameof(MusicXmlCodes))]
    public void Every_musicxml_issue_is_written_in_russian(string code)
    {
        foreach (int count in new[] { 1, 3, 5, 21 })
        {
            string russian = MusicXmlIssueCodes.Describe(code, count, Words.Russian);
            Assert.True(IsRussian(russian), russian);
            Assert.NotEqual(MusicXmlIssueCodes.Describe(code, count), russian);
        }
    }

    [Fact]
    public void An_imported_report_carries_both_languages_and_names_the_track_in_russian()
    {
        ImportedPiece piece = PieceImporter.Import("piano.mid", File.ReadAllBytes(Fixture("Midi", "Fixtures", "piano-single-track.mid"))).Piece!;

        Assert.NotEmpty(piece.Report.Issues);
        Assert.All(piece.Report.Issues, issue =>
        {
            Assert.Equal(issue.Message, issue.Text(Words.English));
            Assert.True(IsRussian(issue.Text(Words.Russian)), issue.Text(Words.Russian));
            Assert.Equal(issue.Track is not null, issue.Text(Words.Russian).StartsWith("Дорожка ", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void The_bars_worked_out_for_a_transcription_are_described_in_russian()
    {
        ImportedPiece heard = PieceImporter.Import("take.mid", File.ReadAllBytes(Fixture("Transcription", "Fixtures", "transkun-fur-elise.mid"))).Piece!;
        BarGrid grid = BarGrid.Find(heard.Midi!)!;
        ImportIssue issue = grid.Apply(heard).Report.Issues.Single(issue => issue.Code == BarGrid.IssueCode);

        Assert.Equal(grid.Describe(), issue.Message);
        Assert.Equal(grid.Describe(Words.Russian), issue.Text(Words.Russian));
        Assert.StartsWith("Такты (3/8) и темп", issue.Text(Words.Russian), StringComparison.Ordinal);
        Assert.Contains("после затакта в 2 шестнадцатые", issue.Text(Words.Russian), StringComparison.Ordinal);
    }

    [Fact]
    public void A_played_file_is_described_in_russian_with_its_figures()
    {
        MidiImport import = MidiImporter.Import(File.ReadAllBytes(Fixture("Midi", "Fixtures", "performance-click.mid")));
        PerformanceTiming timing = PerformanceTiming.Measure(import.Notes, import.Meter);

        Assert.Equal(
            "Этот файл сыгран, а не записан нотами: 42% начал нот отстоят больше чем на 10 тиков от сеток шестнадцатых и восьмых триолей, и от сетки шестнадцатых они отстоят в среднем на 11 тиков. "
            + "Игра следует темпу файла, поэтому «Восстановить ритм» притянет её к шестнадцатым: к этой сетке она ближе всего.",
            timing.Describe(Words.Russian));
    }

    [Theory]
    [InlineData(new byte[0], "Этот файл пуст.")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, "Похоже, это PDF.")]
    public void A_rejected_file_says_why_in_russian(byte[] data, string russian)
    {
        ImportRejection rejection = PieceImporter.Import("file", data).Rejection!;

        Assert.Equal(russian, rejection.Text(Words.Russian));
        Assert.True(IsRussian(rejection.TipIn(Words.Russian)));
        Assert.Equal(rejection.Message, rejection.Text(Words.English));
        Assert.Equal(rejection.Tip, rejection.TipIn(Words.English));
    }
}