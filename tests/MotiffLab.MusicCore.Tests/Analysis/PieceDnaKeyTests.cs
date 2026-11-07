using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Import.Transcription;

namespace MotiffLab.MusicCore.Tests.Analysis;

/// <summary>
/// The key lane of Piece DNA (0018 section 9). Testing (2026-11-06): a recording's notes carry no key signature, and the lane
/// said the file gave no key while Insights named one for the same piece; now the lane takes the key that fits all the notes,
/// marked inferred.
/// </summary>
public class PieceDnaKeyTests
{
    private static ImportedPiece FurElise() =>
        PieceImporter.Import("fur-elise-original.musicxml", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Experiments", "Fixtures", "fur-elise-original.musicxml"))).Piece!;

    [Fact]
    public void A_piece_without_a_key_signature_gets_the_key_its_notes_fit_best_marked_inferred()
    {
        ImportedPiece piece = FurElise();

        PieceDna dna = PieceDna.Build(piece.Notes, piece.Meter, keys: [], piece.End, selection: null, structure: null);

        // The reading a transcription's key signature gets (0016), so a recording's lane and its score agree.
        DnaKey key = Assert.Single(dna.Keys);
        Assert.NotNull(Transcribed.KeyOf(piece.Notes));
        Assert.Equal(Transcribed.KeyOf(piece.Notes), key.Key);
        Assert.True(key.KeyInferred);
        Assert.False(key.ModeInferred);
        Assert.True(key.Inferred);
        Assert.Equal(dna.FirstBar, key.FirstBar);
        Assert.Equal(dna.LastBar, key.LastBar);
        Assert.Equal(DnaStatus.Partial, dna.KeysStatus);
    }

    [Fact]
    public void A_piece_with_a_key_signature_keeps_the_file_s_key_not_inferred()
    {
        ImportedPiece piece = FurElise();

        PieceDna dna = PieceDna.Build(piece.Notes, piece.Meter, piece.Keys, piece.End, selection: null, structure: null);

        Assert.All(dna.Keys, key => Assert.False(key.KeyInferred));
        Assert.Equal(0, dna.Keys[0].Key.Fifths);
    }
}