using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>Where the facts point: every place of a motif, structure or character fact holds notes of the piece.</summary>
public class FactPlaceTests
{
    private static ImportedPiece Piece(string name) =>
        PieceImporter.Import(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", name))).Piece!;

    [Fact]
    public void Every_place_on_the_pilot_pieces_holds_notes_of_the_piece_in_time_order()
    {
        foreach ((string name, int first, int last) in new[] { ("fur-elise-original.musicxml", 0, 1), ("fur-elise-original.musicxml", 2, 4), ("ode-to-joy.musicxml", 1, 4) })
        {
            ImportedPiece piece = Piece(name);
            TickSpan span = TickSpan.FromBounds(piece.Meter.GetBar(first).Span.Onset, piece.Meter.GetBar(last).Span.End);
            MotifSelection selection = MotifSelector.Select(piece.Notes, piece.Meter, span, SelectionFilter.All);
            KeySignature? key = KeySignature.InForce(piece.Keys, selection.Span.Onset);
            var byId = piece.Notes.ToDictionary(note => note.Id, StringComparer.Ordinal);
            FactPlace[] places =
            [
                .. MotifAnalyzer.Analyze(piece.Notes, piece.Meter, selection, key, phrases: []).Facts
                    .Concat(StructureAnalyzer.Analyze(piece.Notes, piece.Meter, selection, []).Facts)
                    .Concat(MotifAnalyzer.AnalyzeCharacter(piece.Notes, piece.Meter, selection, key, CharacterContext.FromPiece(piece)))
                    .SelectMany(fact => fact.Places),
            ];

            Assert.NotEmpty(places);
            Assert.All(places, place =>
            {
                Note[] notes = [.. place.NoteIds.Select(id => byId[id])];
                Assert.All(notes, note => Assert.True(place.Span.Contains(note.Span.Onset)));
                Assert.Equal(notes.OrderBy(note => note.Span.Onset).Select(note => note.Id), place.NoteIds);
            });
        }
    }
}