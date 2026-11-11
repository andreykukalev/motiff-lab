using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// One source of structure (#347; decision record 0027, section 5): the phrase brackets, the Contents and Piece DNA read
/// the same <see cref="PieceStructure"/>, so the sections of the form, their letters and their bars are the same wherever
/// they are shown.
/// </summary>
public class PieceStructureTests
{
    /// <summary>Für Elise and the two golden projects of version 12.</summary>
    public static TheoryData<string> Pieces => new()
    {
        Path.Combine("Import", "MusicXml", "Fixtures", "FurElise.musicxml"),
        Path.Combine("Golden", "v12-paterlini-all-my-joy.motiff.json"),
        Path.Combine("Golden", "v12-einaudi-santiago.motiff.json"),
    };

    /// <summary>
    /// Where each phrase of Für Elise starts, by hand, as bar and sixteenth in the bar numbers of the file (bar 0 is the
    /// pickup; the endings and the repeat each count a bar). The phrases the ear hears: the opening's antecedent and
    /// consequent; the second ending, the answering phrase and the link back; the return; the F major episode; its C major
    /// figure played twice; the E–D♯ link; the second refrain; the episode over the A pedal, antecedent and consequent;
    /// the dominant chord, the arpeggios and the chromatic descent; the last refrain.
    /// </summary>
    private static readonly (int Bar, int Sixteenth)[] s_furEliseStarts =
    [
        (0, 0), (4, 4), (9, 0), (15, 4), (19, 4), (24, 0), (32, 0), (36, 0), (39, 4), (43, 4), (47, 0),
        (53, 4), (57, 4), (61, 0), (69, 0), (78, 0), (84, 0), (87, 4), (91, 0), (97, 4), (101, 4),
    ];

    [Theory]
    [MemberData(nameof(Pieces))]
    public void The_contents_and_piece_dna_show_the_same_sections_letters_and_bars(string path)
    {
        var facade = new MusicCoreFacade();
        JsonElement opened = Parse(facade.Open(Path.GetFileName(path), File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, path)), """{"language":"en","id":"p_structure"}"""));
        Assert.False(opened.TryGetProperty("error", out _), opened.GetRawText());
        int piece = opened.GetProperty("piece").GetInt32();

        // The map's form lane, the sections the iPad Contents lists.
        (string Label, double Onset, double End, int FirstBar, int LastBar)[] form = Sections(Parse(facade.Call(piece, "dna", null)));
        Assert.NotEmpty(form);

        // A selection asks the map for its own ideas but leaves the form of the piece as it is.
        Parse(facade.Call(piece, "select", $$$"""{"span":{"onset":{{{form[0].Onset}}},"end":{{{form[0].End}}}}}"""));
        Assert.Equal(form, Sections(Parse(facade.Call(piece, "dna", null))));

        // Add Structure bookmarks, from the Contents or from the map: one per section, its letter and its bars.
        JsonElement[] filed = [.. Parse(facade.Call(piece, "bookmark.form", """{"now":"2026-11-11T10:00:00Z"}""")).GetProperty("bookmarks").EnumerateArray()
            .Where(bookmark => bookmark.GetProperty("category").GetString() == "structure")];
        Assert.Equal(
            form.Select(section => ($"Section {section.Label}, {(section.FirstBar == section.LastBar ? $"bar {section.FirstBar}" : $"bars {section.FirstBar}–{section.LastBar}")}", section.Onset, section.End)),
            filed.Select(bookmark => (bookmark.GetProperty("title").GetString()!, bookmark.GetProperty("span").GetProperty("onset").GetDouble(), bookmark.GetProperty("span").GetProperty("end").GetDouble())));
        facade.Close(piece);
    }

    [Fact]
    public void Piece_dna_shows_the_sections_the_structure_holds()
    {
        ImportedPiece piece = FurElise();
        PieceStructure structure = Structure(piece);

        PieceDna dna = PieceDna.Build(piece.Notes, piece.Meter, piece.Keys, piece.End, selection: null, structure: null, default, structure);

        // The web Contents lists structure.Sections; the map shows the same list, not one of its own.
        Assert.Same(structure.Sections, dna.Form);
        Assert.Equal(structure.Closes, structure.Levels.Sentences.Closes);
        Assert.Same(structure.Motifs, structure.Motifs);
        Assert.Same(structure, structure.With(structure.Levels));
    }

    [Fact]
    public void The_phrases_of_fur_elise_start_where_the_ear_hears_them_start()
    {
        ImportedPiece piece = FurElise();
        PhraseMap phrases = Structure(piece).Levels.Phrases;

        Tick[] expected = [.. s_furEliseStarts.Select(start => new Tick(piece.Meter.GetBar(start.Bar).Span.Onset.Value + (start.Sixteenth * (CanonicalTime.TicksPerQuarter / 4))))];
        Assert.Equal(expected, phrases.Phrases.Select(phrase => phrase.Start?.At ?? piece.Meter.GetBar(0).Span.Onset));
    }

    [Fact]
    public void The_structure_is_the_same_every_time_it_is_read()
    {
        ImportedPiece piece = FurElise();
        PieceStructure first = Structure(piece);
        PieceStructure second = Structure(piece);

        Assert.Equal(first.Levels.Phrases.Phrases.Select(phrase => phrase.Span), second.Levels.Phrases.Phrases.Select(phrase => phrase.Span));
        Assert.Equal(first.Sections, second.Sections, SectionComparer.Instance);
        Assert.Equal(first.Motifs.Select(idea => idea.Span), second.Motifs.Select(idea => idea.Span));
    }

    private static ImportedPiece FurElise()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml");
        return PieceImporter.Import(Path.GetFileName(file), File.ReadAllBytes(file)).Piece!;
    }

    private static PieceStructure Structure(ImportedPiece piece) =>
        PieceStructure.Of(piece.Notes, piece.Meter, PhraseLevels.Suggest(piece.Notes, piece.Meter, piece.MusicXml?.PhraseMarks ?? PhraseMarks.None, piece.End, KeySignature.InForce(piece.Keys, Tick.Zero), default, null));

    private static JsonElement Parse(string answer) => JsonDocument.Parse(answer).RootElement.Clone();

    private static (string Label, double Onset, double End, int FirstBar, int LastBar)[] Sections(JsonElement dna) =>
        [.. dna.GetProperty("form").GetProperty("sections").EnumerateArray().Select(section =>
        {
            JsonElement passage = section.GetProperty("passage");
            return (section.GetProperty("label").GetString()!, passage.GetProperty("onset").GetDouble(), passage.GetProperty("end").GetDouble(), passage.GetProperty("firstBar").GetInt32(), passage.GetProperty("lastBar").GetInt32());
        })];

    private sealed class SectionComparer : IEqualityComparer<DnaSection>
    {
        public static readonly SectionComparer Instance = new();

        public bool Equals(DnaSection? x, DnaSection? y) =>
            x is not null && y is not null && x.Id == y.Id && x.Label == y.Label && x.Span == y.Span && x.SharesWith.SequenceEqual(y.SharesWith);

        public int GetHashCode(DnaSection obj) => HashCode.Combine(obj.Id, obj.Label, obj.Span);
    }
}