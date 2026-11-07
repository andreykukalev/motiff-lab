using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// Piece DNA filled from what the engine measures (testing item 22): the form from the sentences, the ideas found from the
/// openings of sentences and phrases, a chord per bar with the closes, density, register and dynamics.
/// </summary>
public class PieceDnaMapTests
{
    private static ImportedPiece Piece(params string[] path)
    {
        string file = Path.Combine([AppContext.BaseDirectory, .. path]);
        return PieceImporter.Import(Path.GetFileName(file), File.ReadAllBytes(file)).Piece!;
    }

    private static ImportedPiece FurElise() => Piece("Import", "MusicXml", "Fixtures", "FurElise.musicxml");

    private static ImportedPiece OdeToJoy() => Piece("Experiments", "Fixtures", "ode-to-joy.musicxml");

    private static SentenceMap Sentences(ImportedPiece piece, Words words = default) =>
        PhraseLevels.Suggest(piece.Notes, piece.Meter, piece.MusicXml?.PhraseMarks ?? PhraseMarks.None, piece.End, KeySignature.InForce(piece.Keys, Tick.Zero), words, null).Sentences;

    private static PieceDna Dna(ImportedPiece piece, Words words = default) =>
        PieceDna.Build(piece.Notes, piece.Meter, piece.Keys, piece.End, selection: null, structure: null, words, Sentences(piece, words));

    [Fact]
    public void A_period_read_as_one_sentence_shows_its_two_phrases_as_A_and_A_prime()
    {
        PieceDna dna = Dna(OdeToJoy());

        Assert.Equal(DnaStatus.Partial, dna.FormStatus);
        Assert.Equal(["A", "A′"], dna.Form.Select(section => section.Label));
        Assert.Equal([dna.Form[1].Id], dna.Form[0].SharesWith);
        Assert.Equal([dna.Form[0].Id], dna.Form[1].SharesWith);
        Assert.Equal((1, 4), (dna.Form[0].FirstBar, dna.Form[0].LastBar));
        Assert.Equal((5, 8), (dna.Form[1].FirstBar, dna.Form[1].LastBar));
    }

    [Fact]
    public void Sections_that_bring_back_the_opening_take_its_letter_with_a_prime()
    {
        PieceDna dna = Dna(FurElise());

        // The returns of the opening theme take A with primes; the material between takes letters of its own.
        Assert.Equal("A", dna.Form[0].Label);
        Assert.StartsWith("A", dna.Form[^1].Label);
        Assert.NotEqual("A", dna.Form[^1].Label);
        Assert.Contains(dna.Form, section => !section.Label.StartsWith('A'));
        Assert.Contains(dna.Form[^1].Id, dna.Form[0].SharesWith);
        Assert.Equal(dna.Form.OrderBy(section => section.Span.Onset).Select(section => section.Id), dna.Form.Select(section => section.Id));
        Assert.All(dna.Form.Skip(1).Zip(dna.Form), pair => Assert.True(pair.First.Span.Onset >= pair.Second.Span.End, $"{pair.Second.Label} overlaps {pair.First.Label}"));
    }

    [Fact]
    public void The_opening_heard_most_often_is_the_first_idea_and_says_what_its_marks_show()
    {
        PieceDna dna = Dna(FurElise(), Words.Russian);

        Assert.Equal(DnaStatus.Partial, dna.IdeasStatus);
        Assert.InRange(dna.Ideas.Count, 3, IdeaFinder.MaxIdeas);
        DnaIdea first = dna.Ideas[0];
        Assert.False(first.FromSelection);
        Assert.Equal("idea_1", first.Id);
        Assert.StartsWith("Идея 1 · такты 0–1", first.Title);
        Assert.True(first.Occurrences.Count >= 8, $"{first.Occurrences.Count} occurrences");
        Assert.Single(first.Occurrences, occurrence => occurrence.Kind == OccurrenceKind.Original);
        Assert.StartsWith($"Звучит {Words.Russian.Times(first.Occurrences.Count)}, всегда точно.", first.Summary);
        Assert.Contains("Открывает A", first.Summary);

        // Ordered by how often each is heard, with distinct ids, and no idea starts where another is heard.
        Assert.Equal(dna.Ideas.OrderByDescending(idea => idea.Occurrences.Count).Select(idea => idea.Id), dna.Ideas.Select(idea => idea.Id));
        Assert.Equal(dna.Ideas.Count, dna.Ideas.Select(idea => idea.Id).Distinct().Count());
        foreach (DnaIdea idea in dna.Ideas)
        {
            Assert.All(dna.Ideas.Where(other => other != idea), other =>
                Assert.DoesNotContain(other.Occurrences, occurrence => occurrence.Span.Onset == idea.Span.Onset));
        }
    }

    [Fact]
    public void A_transposed_idea_counts_its_places_higher_and_lower()
    {
        DnaIdea? transposed = Dna(FurElise()).Ideas.FirstOrDefault(idea => idea.Occurrences.Any(occurrence => occurrence.Kind == OccurrenceKind.Transposed));

        Assert.NotNull(transposed);
        int higher = transposed.Occurrences.Count(occurrence => occurrence.Transposition > 0);
        Assert.True(higher > 0);
        Assert.Contains($"{higher} higher", transposed.Summary);
        Assert.StartsWith($"Heard {transposed.Occurrences.Count} times: ", transposed.Summary);
    }

    [Fact]
    public void The_selection_stays_the_first_idea_and_an_opening_heard_inside_it_is_not_found_again()
    {
        ImportedPiece piece = FurElise();
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, piece.Meter.GetBar(1).Span, SelectionFilter.All);
        StructureAnalysis structure = StructureAnalyzer.Analyze(piece.Notes, piece.Meter, selection, []);

        PieceDna dna = PieceDna.Build(piece.Notes, piece.Meter, piece.Keys, piece.End, selection, structure, default, Sentences(piece));

        Assert.True(dna.Ideas[0].FromSelection);
        Assert.Equal("idea_1", dna.Ideas[0].Id);
        Assert.Equal("idea_2", dna.Ideas[1].Id);
        Assert.StartsWith("Idea 1 · ", dna.Ideas[1].Title);
        Assert.All(dna.Ideas.Skip(1), idea => Assert.DoesNotContain(dna.Ideas[0].Occurrences, place => place.Span.Onset <= idea.Span.Onset && idea.Span.Onset < place.Span.End));
        Assert.True(dna.Ideas.Count <= IdeaFinder.MaxIdeas);
    }

    [Fact]
    public void Without_sentences_the_form_and_found_ideas_are_left_out()
    {
        ImportedPiece piece = OdeToJoy();

        PieceDna dna = PieceDna.Build(piece.Notes, piece.Meter, piece.Keys, piece.End, selection: null, structure: null);

        Assert.Equal(DnaStatus.NotYet, dna.FormStatus);
        Assert.Empty(dna.Form);
        Assert.Equal(DnaStatus.NotYet, dna.IdeasStatus);
        Assert.Empty(dna.Cadences);
    }

    [Fact]
    public void The_harmony_lane_holds_a_chord_per_bar_spelt_in_the_key_with_the_closes_as_dots()
    {
        PieceDna dna = Dna(FurElise());

        Assert.Equal(DnaStatus.Partial, dna.HarmonyStatus);
        Assert.Equal("Am", dna.Harmony.Single(chord => chord.Bar == 2).Symbol);
        Assert.Equal("E", dna.Harmony.Single(chord => chord.Bar == 3).Symbol);
        Assert.Equal(dna.Harmony.Select(chord => chord.Bar).Order(), dna.Harmony.Select(chord => chord.Bar));
        Assert.Equal(dna.Harmony.Count, dna.Harmony.Select(chord => chord.Bar).Distinct().Count());

        Assert.Contains(dna.Cadences, cadence => cadence.Kind == CloseKind.Full && cadence.Bar == dna.LastBar);
        Assert.Contains(dna.Cadences, cadence => cadence.Kind == CloseKind.Half);
        Assert.DoesNotContain(dna.Cadences, cadence => cadence.Kind == CloseKind.None);
    }

    [Fact]
    public void A_single_line_has_no_harmony_lane()
    {
        PieceDna dna = Dna(OdeToJoy());

        Assert.Equal(DnaStatus.NotYet, dna.HarmonyStatus);
        Assert.Empty(dna.Harmony);
    }

    [Fact]
    public void Density_is_notes_per_beat_for_every_bar_and_register_spans_each_bar()
    {
        ImportedPiece piece = OdeToJoy();
        PieceDna dna = Dna(piece);

        Assert.Equal(DnaStatus.Ready, dna.DensityStatus);
        Assert.Equal(Enumerable.Range(dna.FirstBar, dna.LastBar - dna.FirstBar + 1), dna.Density.Select(level => level.Bar));

        // Ode to Joy: four crotchets a bar, then a dotted crotchet, a quaver and a minim at each phrase end.
        Assert.Equal(1, dna.Density.Single(level => level.Bar == 1).Value);
        Assert.Equal(0.75, dna.Density.Single(level => level.Bar == 4).Value);

        DnaRange first = dna.Register.Single(range => range.Bar == 1);
        int[] pitches = [.. piece.Notes.Where(note => piece.Meter.BarAt(note.Span.Onset).Number == 1).Select(note => note.Pitch.MidiNumber)];
        Assert.Equal((pitches.Min(), pitches.Max()), (first.Low, first.High));
    }

    [Fact]
    public void Dynamics_follow_the_velocities_and_are_left_out_when_every_note_is_as_loud()
    {
        PieceDna even = Dna(OdeToJoy());
        Assert.Equal(DnaStatus.NotYet, even.DynamicsStatus);
        Assert.Empty(even.Dynamics);

        ImportedPiece played = Piece("Import", "Transcription", "Fixtures", "transkun-fur-elise.mid");
        PieceDna heard = PieceDna.Build(played.Notes, played.Meter, played.Keys, played.End, null, null);
        Assert.Equal(DnaStatus.Ready, heard.DynamicsStatus);
        Assert.All(heard.Dynamics, level => Assert.InRange(level.Value, 1, 127));
    }

    [Fact]
    public void The_same_piece_gives_the_same_map()
    {
        PieceDna a = Dna(FurElise());
        PieceDna b = Dna(FurElise());

        Assert.Equal(a.Form.Select(section => (section.Label, section.Span)), b.Form.Select(section => (section.Label, section.Span)));
        Assert.Equal(a.Ideas.Select(idea => (idea.Title, idea.Summary)), b.Ideas.Select(idea => (idea.Title, idea.Summary)));
        Assert.Equal(a.Harmony, b.Harmony);
        Assert.Equal(a.Density, b.Density);
    }
}