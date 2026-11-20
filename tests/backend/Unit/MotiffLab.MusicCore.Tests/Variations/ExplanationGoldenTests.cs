using System.Text;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Tests.Golden;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Variations;

/// <summary>
/// The explanation of every catalogue operation with every kind it offers, and of a step by hand, on passages of two real
/// pieces, in English and in Russian, kept character for character (Golden/*.txt), and on two bars of chords: the template's opening
/// sentence and the shared ones after it, or why the step is unavailable. A change to how a step is explained shows here
/// line by line; with MOTIFF_UPDATE_SNAPSHOTS=1 the golden is written again, when it changes on purpose.
/// </summary>
public class ExplanationGoldenTests
{
    private static ImportedPiece Piece(string name) =>
        PieceImporter.Import(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name))).Piece!;

    /// <summary>A note removed, changed and added by hand: the first note of the passage, and a note after its last.</summary>
    private static IEnumerable<OperationParameters> ByHand(IReadOnlyList<Note> notes)
    {
        Note first = notes.OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Pitch.MidiNumber).First();
        Note last = notes.MaxBy(static note => note.Span.End)!;
        yield return HandEditParameters.Remove(first.Id);
        yield return HandEditParameters.Change(first.Id, new NoteValues(new Pitch(first.Pitch.MidiNumber + 1), first.Span, 90));
        yield return HandEditParameters.Change(first.Id, new NoteValues(first.Pitch, new TickSpan(first.Span.Onset.Offset(120), first.Span.Duration / 2), first.Velocity));
        yield return HandEditParameters.Add("n_added", new NoteValues(last.Pitch, new TickSpan(last.Span.Onset, 240), 70));
    }

    /// <summary>Two bars of 4/4 in C major: a melody of quarters over block triads of half notes in the left hand.</summary>
    private static Note[] Chords()
    {
        int[] melody = [72, 74, 76, 72, 77, 76, 74, 71];
        int[][] triads = [[48, 52, 55], [53, 57, 60], [55, 59, 62], [48, 52, 55]];
        static Note Played(string id, int pitch, long onset, long length, Hand hand) => new()
        {
            Id = id,
            Pitch = new Pitch(pitch),
            Span = new TickSpan(new Tick(onset), length),
            Velocity = 80,
            Track = 0,
            Hand = new Attributed<Hand>(hand, Attribution.Imported),
            Attribution = Attribution.Imported,
        };
        return [
            .. melody.Select((pitch, i) => Played($"m{i + 1}", pitch, i * 480L, 480, Hand.Right)),
            .. triads.SelectMany((triad, i) => triad.Select((pitch, k) => Played($"c{i + 1}{k + 1}", pitch, i * 960L, 960, Hand.Left))),
        ];
    }

    /// <summary>The passage's notes and the context a step runs in, in English.</summary>
    private static (IReadOnlyList<Note> Notes, VariationContext Context) Passage(string name, int firstBar, int lastBar)
    {
        if (name == "chords")
        {
            Note[] chords = Chords();
            return (chords, new VariationContext(TickSpan.FromBounds(Tick.Zero, new Tick(2 * 1920)), new HashSet<KeepProperty>(), Key: new MusicalKey(0, KeyMode.Major), Meter: MeterMap.Default));
        }

        ImportedPiece piece = Piece(name);
        MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, MotifSelector.Bars(piece.Meter, firstBar, lastBar, piece.End), SelectionFilter.All);
        KeySignature? signature = KeySignature.InForce(piece.Keys, selection.Span.Onset);
        MotifAnalysis analysis = MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, signature, accents: [], PhraseBoundaryFinder.Find(piece.Notes, piece.Meter, PhraseMarks.None), Words.English);
        MusicalKey? key = analysis.Key is { Certainty: not FactCertainty.Uncertain } reading ? reading.Key : null;
        return (VariationFacts.MotifNotes(piece.Notes, selection), VariationContext.For(selection, new HashSet<KeepProperty>(), key: key, meter: piece.Meter));
    }

    [Theory]
    [InlineData("fur-elise-original.musicxml", 3, 6)]
    [InlineData("ode-to-joy.musicxml", 1, 2)]
    [InlineData("chords", 1, 2)]
    public void Every_step_is_explained_as_before(string name, int firstBar, int lastBar)
    {
        (IReadOnlyList<Note> notes, VariationContext english) = Passage(name, firstBar, lastBar);
        var text = new StringBuilder();
        IEnumerable<(IVariationOperation Operation, OperationParameters Parameters)> steps = [
            .. VariationOperations.All.SelectMany(operation => ParameterSets.Every(operation).Select(parameters => (operation, parameters))),
            .. ByHand(notes).Select(parameters => ((IVariationOperation)HandEdit.Instance, parameters)),
        ];
        foreach ((IVariationOperation operation, OperationParameters parameters) in steps)
        {
            foreach (VariationContext context in new[] { english, english with { Words = Words.Russian } })
            {
                string said = operation.UnavailableReason(notes, context, parameters) is { } reason
                    ? $"unavailable: {reason}"
                    : operation.Apply(notes, context, parameters).Explanation;
                text.Append(operation.Id).Append(' ').Append(parameters).Append(context.Words.IsRussian ? " ru: " : " en: ").Append(said).Append('\n');
            }
        }

        TextGoldens.AssertTextGolden("Variations", $"{Path.GetFileNameWithoutExtension(name)}-{firstBar}-{lastBar}.txt", text.ToString());
    }
}