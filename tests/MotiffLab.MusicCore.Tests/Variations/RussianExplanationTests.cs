using System.Text.RegularExpressions;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>
/// What changed and the Explore experiments in Russian (#134): every explanation and every experiment
/// text is written in Russian throughout, and the English stays as it was.
/// </summary>
public partial class RussianExplanationTests
{
    private static ImportedPiece Piece(string name) =>
        PieceImporter.Import(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name))).Piece!;

    public static TheoryData<string> Pieces => ["fur-elise-original.musicxml", "ode-to-joy.musicxml"];

    // A Latin word of two letters or more; note names ("E5", "F♯4") are a letter and an octave.
    [GeneratedRegex(@"\b[A-Za-z]{2,}\b")]
    private static partial Regex LatinWord();

    private static IEnumerable<OperationParameters> ParameterSets(IVariationOperation operation) => operation switch
    {
        RhythmReshape => [
            .. Enum.GetValues<RhythmReshapeKind>().Where(static kind => kind != RhythmReshapeKind.Displace).Select(static kind => new RhythmReshapeParameters(kind)),
            new RhythmReshapeParameters(RhythmReshapeKind.Displace, 1),
            new RhythmReshapeParameters(RhythmReshapeKind.Displace, 2),
        ],
        Articulation => Enum.GetValues<ArticulationKind>().Select(static kind => new ArticulationParameters(kind)),
        MelodicSequence => [new MelodicSequenceParameters(1, 1), new MelodicSequenceParameters(-2, 3), new MelodicSequenceParameters(2, 1, Diatonic: false)],
        AccompanimentPattern => Enum.GetValues<AccompanimentPatternKind>().Select(static kind => new AccompanimentPatternParameters(kind)),
        IntervalReshape => [new IntervalReshapeParameters(1, null), new IntervalReshapeParameters(-1, new IntervalSet([0]))],
        ChangeDirection => [new ChangeDirectionParameters(Diatonic: true, null), new ChangeDirectionParameters(Diatonic: false, null)],
        _ => [],
    };

    private static IEnumerable<MotifSelection> Selections(ImportedPiece piece)
    {
        int bars = piece.Meter.BarAt(piece.End.Offset(-1)).Number;
        for (int first = piece.Meter.BarAt(Tick.Zero).Number; first <= bars; first += 2)
        {
            foreach (int length in new[] { 1, 2, 4 })
            {
                MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, first, Math.Min(first + length - 1, bars), piece.End), SelectionFilter.All);
                if (selection.NoteIds.Count > 0)
                {
                    yield return selection;
                }
            }
        }
    }

    private static void AddLatin(SortedSet<string> found, string where, string text)
    {
        foreach (Match match in LatinWord().Matches(text))
        {
            found.Add($"{where}: {match.Value} in \"{text}\"");
        }
    }

    [Theory]
    [MemberData(nameof(Pieces))]
    public void Every_explanation_reads_in_russian_and_the_english_is_unchanged(string name)
    {
        ImportedPiece piece = Piece(name);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        int explained = 0;
        foreach (MotifSelection selection in Selections(piece))
        {
            IReadOnlyList<Note> notes = VariationFacts.MotifNotes(piece.Notes, selection);
            MusicalKey? key = Context(piece, selection, Words.English).Motif.Key is { Certainty: not FactCertainty.Uncertain } reading ? reading.Key : null;
            VariationContext english = VariationContext.For(selection, new HashSet<KeepProperty>(), key: key, meter: piece.Meter);
            VariationContext russian = english with { Words = Words.Russian };
            foreach (IVariationOperation operation in VariationOperations.All)
            {
                foreach (OperationParameters parameters in ParameterSets(operation))
                {
                    if (operation.UnavailableReason(notes, english, parameters) is not null)
                    {
                        continue;
                    }

                    Variation inEnglish = operation.Apply(notes, english, parameters);
                    Variation inRussian = operation.Apply(notes, russian, parameters);
                    Assert.Equal(inEnglish.Notes, inRussian.Notes);
                    Assert.Equal(inEnglish.Explanation, operation.Apply(notes, english with { Words = default }, parameters).Explanation);
                    AddLatin(found, $"{operation.Id} {parameters}", inRussian.Explanation);
                    explained++;
                }
            }
        }

        Assert.Empty(found);
        Assert.True(explained >= 50, $"Only {explained} explanations ran on {name}.");
    }

    [Theory]
    [MemberData(nameof(Pieces))]
    public void Every_experiment_reads_in_russian(string name)
    {
        ImportedPiece piece = Piece(name);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        int offered = 0;
        foreach (MotifSelection selection in Selections(piece))
        {
            (InvitationContext english, InvitationContext russian) = (Context(piece, selection, Words.English), Context(piece, selection, Words.Russian));
            IReadOnlyList<Experiment> inEnglish = ExperimentCatalogue.Offer(english);
            IReadOnlyList<Experiment> inRussian = ExperimentCatalogue.Offer(russian);
            Assert.Equal(inEnglish.Select(static experiment => (experiment.Id, experiment.Alternatives.Count)), inRussian.Select(static experiment => (experiment.Id, experiment.Alternatives.Count)));

            foreach (Experiment experiment in inRussian)
            {
                string where = $"{experiment.Id} at bar {experiment.Passage.FirstBar}";
                foreach (string text in new[] { experiment.Title, experiment.Change, experiment.Question, experiment.ListeningFocus })
                {
                    AddLatin(found, where, text);
                }

                for (int i = 0; i < experiment.Alternatives.Count; i++)
                {
                    AddLatin(found, where, experiment.Alternatives[i].Label);
                    AddLatin(found, where, experiment.Alternatives[i].Keeps);
                    if (ExperimentCatalogue.Start(experiment, i, russian).Result is { } result)
                    {
                        AddLatin(found, where, result.Explanation);
                    }
                }

                offered++;
            }
        }

        foreach (ExperimentTopic topic in Enum.GetValues<ExperimentTopic>())
        {
            AddLatin(found, topic.ToString(), ExperimentCatalogue.Prompt(topic, Words.Russian));
        }

        Assert.Empty(found);
        Assert.True(offered > 0, $"No experiment was offered on {name}.");
    }

    [Fact]
    public void The_fur_elise_groove_explains_itself_in_russian()
    {
        ImportedPiece piece = Piece("fur-elise-original.musicxml");
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, 0, 1, piece.End), SelectionFilter.All);
        InvitationContext context = Context(piece, selection, Words.Russian);
        Experiment groove = ExperimentCatalogue.Offer(context).Single(static experiment => experiment.Id == ExperimentCatalogue.GrooveId);

        string explanation = ExperimentCatalogue.Start(groove, 0, context).Result!.Explanation;

        Assert.Matches(@"\p{IsCyrillic}", groove.Title);
        Assert.Matches(@"\p{IsCyrillic}", explanation);
        Assert.Contains("такт", explanation, StringComparison.Ordinal);
    }

    private static InvitationContext Context(ImportedPiece piece, MotifSelection selection, Words words)
    {
        KeySignature? key = KeySignature.InForce(piece.Keys, selection.Span.Onset);
        IReadOnlyList<PhraseBoundary> phrases = PhraseBoundaryFinder.Find(piece.Notes, piece.Meter, PhraseMarks.None);
        return new InvitationContext(
            piece.Notes,
            piece.Meter,
            selection,
            MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, key, accents: [], phrases, words),
            StructureAnalyzer.Analyze(piece.Notes, piece.Meter, selection, phrases, words),
            MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, key, CharacterContext.FromPiece(piece), words),
            words);
    }
}