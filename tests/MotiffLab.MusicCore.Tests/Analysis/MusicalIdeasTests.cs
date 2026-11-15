using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// The ideas of Piece DNA by what they are in the music (#582 item 15, the experiment «Идеи по смыслу»): on real pieces of
/// the test data, which cards come out and the line each says on why it was chosen, from what the engine measured.
/// </summary>
public class MusicalIdeasTests
{
    private static ImportedPiece FurElise() => PieceDnaMapTests.FurElise();

    private static ImportedPiece OdeToJoy() => PieceDnaMapTests.OdeToJoy();

    private static ImportedPiece Promenade() => PieceDnaMapTests.Piece("Import", "Scan", "Fixtures", "copisteria-promenade-page-1.musicxml");

    private static PieceStructure Structure(ImportedPiece piece, Words words) => PieceDnaMapTests.Structure(piece, words);

    private static PieceDna Dna(ImportedPiece piece, Words words = default, bool musical = true) =>
        PieceDna.Build(piece.Notes, piece.Meter, piece.Keys, piece.End, selection: null, structure: null, words, Structure(piece, words), musical);

    [Fact]
    public void Ode_to_Joy_is_one_theme_its_two_phrases_a_question_and_its_answer()
    {
        PieceDna dna = Dna(OdeToJoy());

        DnaIdea theme = Assert.Single(dna.Ideas);
        Assert.Equal(IdeaKind.Theme, theme.Kind);
        Assert.Equal("idea_1", theme.Id);
        Assert.Equal("Theme · bars 1–8", theme.Title);
        Assert.Equal("The first two phrases begin alike; the first ends on a half close, the second on a full one, as a question and its answer.", theme.Summary);
        Assert.Equal(OccurrenceKind.Original, Assert.Single(theme.Occurrences).Kind);
    }

    [Fact]
    public void Fur_Elise_gives_its_theme_the_contrast_of_B_a_motif_of_C_and_the_highest_point()
    {
        PieceDna dna = Dna(FurElise());

        Assert.Equal([IdeaKind.Theme, IdeaKind.Contrast, IdeaKind.Motif, IdeaKind.Peak], dna.Ideas.Select(idea => idea.Kind));
        Assert.Equal(["idea_1", "idea_2", "idea_3", "idea_4"], dna.Ideas.Select(idea => idea.Id));

        // The theme: a, then a again closing on the tonic; its opening returns in B, C and A′.
        DnaIdea theme = dna.Ideas[0];
        Assert.Equal("Theme · bars 0–7", theme.Title);
        Assert.StartsWith("The first two phrases begin alike; the first ends on a half close, the second on a full one", theme.Summary);
        int returns = theme.Occurrences.Count - 1;
        Assert.EndsWith($" Its opening comes back {returns} times.", theme.Summary);
        Assert.All(theme.Occurrences.Skip(1), place => Assert.Equal(OccurrenceKind.Exact, place.Kind));

        // B's first phrase, which climbs to the E above the theme's top.
        DnaIdea contrast = dna.Ideas[1];
        Assert.Equal("Contrast · B · bars 9–15", contrast.Title);
        Assert.Equal("Section B begins here, with material not heard in A: reaching an octave higher.", contrast.Summary);
        Assert.Equal(dna.Form.Single(section => section.Label == "B").Span.Onset, contrast.Span.Onset);

        // The figure of C heard twice, outside the theme's places.
        DnaIdea motif = dna.Ideas[2];
        Assert.Equal("Motif · bar 32", motif.Title);
        Assert.Equal("Heard twice, always exactly. Only in C.", motif.Summary);

        // The arpeggio that climbs to the top E before the return.
        DnaIdea peak = dna.Ideas[3];
        Assert.StartsWith("Highest point · bars 78–", peak.Title);
        Assert.Equal("Holds the highest note of the piece, in bar 81, 2 octaves above anything in the theme.", peak.Summary);
        int top = FurElise().Notes.Max(note => note.Pitch.MidiNumber);
        Assert.Contains(FurElise().Notes.Where(note => note.Pitch.MidiNumber == top), note => peak.NoteIds.Contains(note.Id));

        // No two cards share bars here.
        Assert.All(dna.Ideas, idea => Assert.DoesNotContain(dna.Ideas, other => other != idea && other.Span.Onset < idea.Span.End && idea.Span.Onset < other.Span.End));
    }

    [Fact]
    public void Fur_Elise_in_Russian_says_the_same_in_Russian()
    {
        PieceDna dna = Dna(FurElise(), Words.Russian);

        Assert.Equal("Тема · такты 0–7", dna.Ideas[0].Title);
        Assert.StartsWith("Первые две фразы начинаются одинаково; первая кончается половинным кадансом, вторая полным", dna.Ideas[0].Summary);
        Assert.Equal("Здесь начинается раздел B, с материалом, которого не было в A: поднимается на октаву выше.", dna.Ideas[1].Summary);
        Assert.Equal("Мотив · такт 32", dna.Ideas[2].Title);
        Assert.Equal("Здесь самая высокая нота пьесы, в такте 81, на 2 октавы выше всего в теме.", dna.Ideas[3].Summary);
    }

    [Fact]
    public void Promenade_gives_a_denser_contrast_and_a_motif_heard_higher()
    {
        PieceDna dna = Dna(Promenade());

        Assert.Equal([IdeaKind.Theme, IdeaKind.Contrast, IdeaKind.Motif, IdeaKind.Peak], dna.Ideas.Select(idea => idea.Kind));
        Assert.Equal("The piece's first phrase, ending on a full close.", dna.Ideas[0].Summary);
        Assert.Equal("Section B begins here, with material not heard in A: denser (3.3 notes a beat against 1).", dna.Ideas[1].Summary);
        DnaIdea motif = dna.Ideas[2];
        Assert.Equal("Motif · bar 7", motif.Title);
        Assert.Equal("Heard 3 times: 1 exactly and 2 higher.", motif.Summary);
        Assert.Equal(2, motif.Occurrences.Count(place => place.Kind == OccurrenceKind.Transposed));
        Assert.StartsWith("Holds the highest note of the piece, in bar 13, 14 semitones above", dna.Ideas[3].Summary);
    }

    [Fact]
    public void A_bar_heard_three_times_in_a_row_a_tone_lower_each_time_is_a_sequence()
    {
        // A two-bar opening, then a bar that falls by a tone twice in a row, then a close of long notes.
        (int Pitch, long Quarters)[] opening = [(60, 1), (64, 1), (67, 1), (72, 1), (71, 2), (67, 2)];
        int[] cell = [76, 74, 72, 71];
        (int, long)[] sequence = [.. Enumerable.Range(0, 3).SelectMany(time => cell.Select(pitch => (pitch - (2 * time), 1L)))];
        (int, long)[] close = [(67, 2), (65, 2), (64, 4)];
        Note[] notes = Notes([.. opening, .. sequence, .. close]);
        Tick end = new(notes.Max(note => note.Span.End.Value));
        PieceStructure structure = PieceStructure.Of(notes, MeterMap.Default, PhraseLevels.Suggest(notes, MeterMap.Default, PhraseMarks.None, end, null));

        PieceDna dna = PieceDna.Build(notes, MeterMap.Default, [], end, null, null, default, structure, musicalIdeas: true);

        DnaIdea found = Assert.Single(dna.Ideas, idea => idea.Kind == IdeaKind.Sequence);
        Assert.Equal("Sequence · bars 3–5", found.Title);
        Assert.Equal("One bar heard 3 times in a row, each time 2 semitones lower: a sequence.", found.Summary);
        Assert.Equal([0, -2, -4], found.Occurrences.Select(place => place.Transposition));
        Assert.Equal([OccurrenceKind.Original, OccurrenceKind.Transposed, OccurrenceKind.Transposed], found.Occurrences.Select(place => place.Kind));
    }

    [Fact]
    public void A_figure_heard_again_an_octave_higher_is_no_sequence()
    {
        // Für Elise, bars 79 to 81: the arpeggio climbs by octaves; the highest point holds it instead.
        Assert.DoesNotContain(Dna(FurElise()).Ideas, idea => idea.Kind == IdeaKind.Sequence);
    }

    [Fact]
    public void The_melody_is_the_right_hands_when_the_notes_carry_both_hands()
    {
        ImportedPiece piece = FurElise();

        Note[] line = MusicalIdeas.MelodyLine(piece.Notes);

        Assert.All(line, note => Assert.Equal(Hand.Right, note.Hand?.Value));
        Assert.Equal(StructureAnalyzer.Line(OdeToJoy().Notes), MusicalIdeas.MelodyLine(OdeToJoy().Notes));
    }

    [Fact]
    public void The_selection_stays_first_and_no_card_starts_where_it_is_heard()
    {
        ImportedPiece piece = FurElise();
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, piece.Meter.GetBar(1).Span, SelectionFilter.All);
        StructureAnalysis structure = StructureAnalyzer.Analyze(piece.Notes, piece.Meter, selection, []);

        PieceDna dna = PieceDna.Build(piece.Notes, piece.Meter, piece.Keys, piece.End, selection, structure, default, Structure(piece, default), musicalIdeas: true);

        Assert.Equal(IdeaKind.Selection, dna.Ideas[0].Kind);
        Assert.Equal("idea_1", dna.Ideas[0].Id);
        Assert.Equal(Enumerable.Range(1, dna.Ideas.Count).Select(number => $"idea_{number}"), dna.Ideas.Select(idea => idea.Id));
        Assert.DoesNotContain(dna.Ideas, idea => idea.Kind == IdeaKind.Theme);
        Assert.All(dna.Ideas.Skip(1), idea => Assert.DoesNotContain(dna.Ideas[0].Occurrences, place => place.Span.Onset < idea.Span.End && idea.Span.Onset < place.Span.End));
        Assert.True(dna.Ideas.Count <= MusicalIdeas.MaxIdeas + 1);
    }

    [Fact]
    public void Off_the_map_keeps_the_openings_heard_most_often()
    {
        PieceDna dna = Dna(FurElise(), musical: false);

        Assert.All(dna.Ideas, idea => Assert.Equal(IdeaKind.Found, idea.Kind));
        Assert.StartsWith("Idea 1 · ", dna.Ideas[0].Title);
    }

    [Fact]
    public void The_same_piece_gives_the_same_ideas()
    {
        Assert.Equal(
            Dna(FurElise()).Ideas.Select(idea => (idea.Id, idea.Title, idea.Summary, idea.Span)),
            Dna(FurElise()).Ideas.Select(idea => (idea.Id, idea.Title, idea.Summary, idea.Span)));
    }

    private static Note[] Notes((int Pitch, long Quarters)[] line)
    {
        const long quarter = 480;
        long at = 0;
        var notes = new List<Note>();
        foreach ((int pitch, long quarters) in line)
        {
            notes.Add(new Note
            {
                Id = $"n{notes.Count}",
                Pitch = new Pitch(pitch),
                Span = new TickSpan(new Tick(at), quarters * quarter),
                Velocity = 80,
                Track = 0,
                Attribution = Attribution.Imported,
            });
            at += quarters * quarter;
        }

        return [.. notes];
    }
}