using System.Text.RegularExpressions;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>The facts in Russian (#134): the same facts as in English, written in Russian throughout.</summary>
public partial class RussianFactTests
{
    private static ImportedPiece Piece(string name) =>
        PieceImporter.Import(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name))).Piece!;

    /// <summary>Every fact the Understand panel shows for a selection: motif, structure and character.</summary>
    private static List<MotifFact> Facts(ImportedPiece piece, MotifSelection selection, Words words)
    {
        KeySignature? key = KeySignature.InForce(piece.Keys, selection.Span.Onset);
        IReadOnlyList<PhraseBoundary> phrases = PhraseBoundaryFinder.Find(piece.Notes, piece.Meter, PhraseMarks.None);
        return
        [
            .. MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, key, accents: [], phrases, words).Facts,
            .. StructureAnalyzer.Analyze(piece.Notes, piece.Meter, selection, phrases, words).Facts,
            .. MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, key, CharacterContext.FromPiece(piece), words),
        ];
    }

    /// <summary>Selections of one to four bars from the start of each bar, in every hand, so most sentence shapes come up.</summary>
    public static TheoryData<string> Pieces => ["fur-elise-original.musicxml", "ode-to-joy.musicxml"];

    // A Latin word of two letters or more; note names ("E5", "F♯4") are a letter and an octave.
    [GeneratedRegex(@"\b[A-Za-z]{2,}\b")]
    private static partial Regex LatinWord();

    private static readonly HashSet<string> s_allowed = ["BPM", "MIDI", "velocity"];

    [Theory]
    [MemberData(nameof(Pieces))]
    public void Russian_facts_have_no_english_words(string name)
    {
        ImportedPiece piece = Piece(name);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (MotifSelection selection in Selections(piece))
        {
            foreach (MotifFact fact in Facts(piece, selection, Words.Russian))
            {
                foreach (string text in new[] { fact.Text, fact.Basis }.Concat(fact.Places.Select(place => place.Label)))
                {
                    foreach (Match match in LatinWord().Matches(text))
                    {
                        if (!s_allowed.Contains(match.Value))
                        {
                            found.Add($"{fact.Id}: {match.Value} in \"{text}\"");
                        }
                    }
                }
            }
        }

        Assert.Empty(found);
    }

    [Theory]
    [MemberData(nameof(Pieces))]
    public void Russian_states_the_same_facts_with_the_same_places(string name)
    {
        ImportedPiece piece = Piece(name);
        foreach (MotifSelection selection in Selections(piece))
        {
            List<MotifFact> english = Facts(piece, selection, Words.English);
            List<MotifFact> russian = Facts(piece, selection, Words.Russian);

            Assert.Equal(english.Select(fact => (fact.Id, fact.Lens, fact.Certainty)), russian.Select(fact => (fact.Id, fact.Lens, fact.Certainty)));
            Assert.Equal(
                english.SelectMany(fact => fact.Places.Select(place => (place.Span, string.Join(",", place.NoteIds)))),
                russian.SelectMany(fact => fact.Places.Select(place => (place.Span, string.Join(",", place.NoteIds)))));
        }
    }

    [Fact]
    public void The_fur_elise_motif_reads_in_russian()
    {
        ImportedPiece piece = Piece("fur-elise-original.musicxml");
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, 0, 1, piece.End), SelectionFilter.All);
        Dictionary<string, string> facts = Facts(piece, selection, Words.Russian).ToDictionary(fact => fact.Id, fact => fact.Text);

        Assert.Equal("Тональность: ля минор.", facts["key"]);
        Assert.StartsWith("Интервалы в полутонах: ", facts["melody.intervals"], StringComparison.Ordinal);
        Assert.StartsWith("Темп: ", facts["character.tempo"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3, "малая терция", "малую терцию")]
    [InlineData(7, "чистая квинта", "чистую квинту")]
    [InlineData(6, "тритон", "тритон")]
    [InlineData(12, "октава", "октаву")]
    [InlineData(14, "октава и большая секунда", "октаву и большую секунду")]
    [InlineData(24, "2 октавы", "2 октавы")]
    public void Intervals_are_named_in_russian(int semitones, string name, string by)
    {
        Assert.Equal(name, MotifAnalyzer.IntervalName(semitones, Words.Russian));
        Assert.Equal(by, MotifAnalyzer.IntervalBy(-semitones, Words.Russian));
    }

    [Theory]
    [InlineData(0, KeyMode.Minor, "ля минор")]
    [InlineData(-3, KeyMode.Major, "ми-бемоль мажор")]
    [InlineData(6, KeyMode.Major, "фа-диез мажор")]
    [InlineData(-2, KeyMode.Minor, "соль минор")]
    public void Keys_are_named_in_russian(int fifths, KeyMode mode, string name) =>
        Assert.Equal(name, new MusicalKey(fifths, mode).NameIn(Words.Russian));

    [Theory]
    [InlineData(1, "нота")]
    [InlineData(3, "ноты")]
    [InlineData(5, "нот")]
    [InlineData(11, "нот")]
    [InlineData(21, "нота")]
    [InlineData(22, "ноты")]
    [InlineData(112, "нот")]
    public void Russian_counts_take_the_form_of_their_number(int count, string noun) =>
        Assert.Equal(noun, Words.RussianForm(count, "нота", "ноты", "нот"));

    private static IEnumerable<MotifSelection> Selections(ImportedPiece piece)
    {
        int bars = piece.Meter.BarAt(piece.End.Offset(-1)).Number;
        SelectionFilter[] filters = [SelectionFilter.All, .. piece.Tracks.Select(track => SelectionFilter.ForTrack(track.Index))];
        for (int first = piece.Meter.BarAt(Tick.Zero).Number; first <= bars; first++)
        {
            foreach (int length in new[] { 1, 2, 4 })
            {
                foreach (SelectionFilter filter in filters)
                {
                    MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, first, Math.Min(first + length - 1, bars), piece.End), filter);
                    if (selection.NoteIds.Count > 0)
                    {
                        yield return selection;
                    }
                }
            }
        }
    }
}