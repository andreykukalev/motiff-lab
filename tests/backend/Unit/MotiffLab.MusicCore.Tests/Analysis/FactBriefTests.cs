using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// Short statements in the Insights stream (assessment items 16–20): a fact that lists what it counts has a one-line
/// brief that points at the notes, and keeps the listing in its text for the Basis.
/// </summary>
public class FactBriefTests
{
    private static ImportedPiece Piece(string name) =>
        PieceImporter.Import(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name))).Piece!;

    private static Dictionary<string, MotifFact> FurEliseOpening(Words words)
    {
        ImportedPiece piece = Piece("fur-elise-original.musicxml");
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, 0, 4, piece.End), SelectionFilter.All);
        KeySignature? key = KeySignature.InForce(piece.Keys, selection.Span.Onset);
        IReadOnlyList<PhraseBoundary> phrases = PhraseBoundaryFinder.Find(piece.Notes, piece.Meter, PhraseMarks.None);
        List<MotifFact> all =
        [
            .. MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, key, accents: [], phrases, words).Facts,
            .. MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, key, CharacterContext.FromPiece(piece), words),
            .. HarmonyAnalyzer.Analyze(piece.Notes, piece.Meter, selection, new MusicalKey(0, KeyMode.Minor), words).Facts,
        ];
        return all.ToDictionary(fact => fact.Id);
    }

    [Fact]
    public void Listing_facts_state_what_they_count_in_one_line()
    {
        Dictionary<string, MotifFact> facts = FurEliseOpening(Words.English);

        MotifFact line = facts["melody.line"];
        Assert.Matches(@"^\d+ notes in the top line\.$", line.Brief);
        Assert.Contains("E5 D♯5 E5", line.Text, StringComparison.Ordinal);
        Assert.Equal(line.Brief, line.Claim);

        MotifFact intervals = facts["melody.intervals"];
        Assert.DoesNotContain("semitones", intervals.Brief, StringComparison.Ordinal);
        Assert.EndsWith(intervals.Brief!, intervals.Text, StringComparison.Ordinal);

        Assert.Matches(@"^Every beat splits in two\.$|^Most beats split in two \(\d+ of \d+\)\.$", facts["rhythm.grouping"].Brief);
        Assert.Matches(@"^\d+ notes? outside the scale of A minor\.$", facts["melody.degrees"].Brief);
        Assert.Matches(@"^\d+ chords: [A-G]", facts["harmony.chords"].Brief);
        Assert.DoesNotContain("from bar", facts["harmony.chords"].Brief, StringComparison.Ordinal);
        Assert.Matches(@"^(No syncopation|\d+ syncopations?)\.$", facts["rhythm.syncopation"].Brief);
    }

    [Fact]
    public void A_fact_already_one_line_has_no_brief()
    {
        Dictionary<string, MotifFact> facts = FurEliseOpening(Words.English);

        Assert.Null(facts["key"].Brief);
        Assert.Null(facts["melody.range"].Brief);
        Assert.Equal(facts["key"].Text, facts["key"].Claim);
    }

    [Fact]
    public void Briefs_read_in_russian()
    {
        Dictionary<string, MotifFact> facts = FurEliseOpening(Words.Russian);

        Assert.Matches(@"^\d+ нот[аы]? в верхнем голосе\.$", facts["melody.line"].Brief);
        Assert.Matches(@"^(Синкоп нет|\d+ синкоп[аы]?)\.$", facts["rhythm.syncopation"].Brief);
        Assert.Matches(@"^\d+ нот[аы]? вне звукоряда тональности ля минор\.$", facts["melody.degrees"].Brief);
    }
}