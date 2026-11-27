using System.Text.RegularExpressions;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
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
        int unavailable = 0;
        foreach (MotifSelection selection in Selections(piece))
        {
            IReadOnlyList<Note> notes = VariationFacts.MotifNotes(piece.Notes, selection);
            MusicalKey? key = Context(piece, selection, Words.English).Motif.Key is { Certainty: not FactCertainty.Uncertain } reading ? reading.Key : null;
            VariationContext english = VariationContext.For(selection, new HashSet<KeepProperty>(), key: key, meter: piece.Meter);
            VariationContext russian = english with { Words = Words.Russian };
            foreach (IVariationOperation operation in VariationOperations.All)
            {
                foreach (OperationParameters parameters in operation is RhythmReshape or Articulation or MelodicSequence or AccompanimentPattern or IntervalReshape or ChangeDirection ? ParameterSets.Every(operation) : [])
                {
                    if (operation.UnavailableReason(notes, english, parameters) is not null)
                    {
                        AddLatin(found, $"{operation.Id} {parameters} unavailable", operation.UnavailableReason(notes, russian, parameters)!);
                        unavailable++;
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
        Assert.True(unavailable > 0, $"No step was unavailable on {name}.");
    }

    [Fact]
    public void The_keep_table_reads_in_russian()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        KeepProperty[] properties = Enum.GetValues<KeepProperty>();
        foreach (KeepRow row in KeepTable.Rows)
        {
            foreach (KeepProperty first in properties)
            {
                foreach (KeepProperty second in properties)
                {
                    var keep = new HashSet<KeepProperty> { first, second };
                    Assert.Equal(KeepTable.DisabledReason(row.OperationId, keep) is null, KeepTable.DisabledReason(row.OperationId, keep, Words.Russian) is null);
                    if (KeepTable.DisabledReason(row.OperationId, keep, Words.Russian) is { } reason)
                    {
                        AddLatin(found, row.OperationId, reason);
                    }
                }
            }

            AddLatin(found, row.OperationId, row.NameIn(Words.Russian));
            foreach (KeepFootnote footnote in row.Footnotes.Values)
            {
                AddLatin(found, $"{row.OperationId} footnote {footnote.Number}", footnote.TextIn(Words.Russian));
            }
        }

        foreach (KeepPreset preset in KeepTable.Presets)
        {
            AddLatin(found, preset.Id, preset.RussianLabel);
        }

        Assert.Empty(found);
        Assert.Equal(
            "«Изменение ритма» меняет, когда начинаются ноты и сколько они длятся, поэтому эту операцию нельзя применить, пока в «Сохранить» отмечено: ритм.",
            KeepTable.DisabledReason("V1", new HashSet<KeepProperty> { KeepProperty.Rhythm }, Words.Russian));
        Assert.Equal(
            "Звуковысотные классы мелодии остаются; её октава может измениться.",
            KeepTable.MelodyPitchClasses.TextIn(Words.Russian));
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