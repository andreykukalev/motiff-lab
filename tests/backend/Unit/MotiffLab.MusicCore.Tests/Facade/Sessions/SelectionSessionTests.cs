using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Tests.Facade.Sessions;

/// <summary>
/// The selection session typed (audit W1.7): selected, understood, a chip tried and saved, with no request and no JSON.
/// What the iPad reads of the same calls is pinned by tests/contracts/facade-answers (FacadeAnswersContractTests).
/// </summary>
public class SelectionSessionTests
{
    private static OpenPiece Example() => OpenPiece.Import(
        "p_session",
        "FurElise.musicxml",
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Import", "MusicXml", "Fixtures", "FurElise.musicxml")),
        Words.English,
        new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero));

    private static MotifSelection Bars(OpenPiece piece, int first, int last) =>
        MotifSelector.Select(piece.Notes, piece.Piece.Meter, MotifSelector.Bars(piece.Piece.Meter, first, last, piece.Piece.End), SelectionFilter.All);

    [Fact]
    public void A_chip_offered_by_what_was_understood_becomes_the_chain_and_is_saved_with_the_working_motif()
    {
        OpenPiece piece = Example();
        SelectionSession session = piece.Selection;
        session.Select(Bars(piece, 1, 4), MotifSelectionKind.Range);

        SelectionUnderstanding understood = session.Understand(piece.State);
        Assert.Same(understood.Context, session.Understood(piece.State));
        string chip = understood.Context.Motif.Facts.Concat(understood.Context.Structure.Facts)
            .SelectMany(understood.Chips).Select(offered => offered.Id).First();
        VariationChain chain = session.Try(piece.State, chip);

        Assert.Same(chain, session.Variation());
        Assert.Equal(chip, session.Chip?.Id);
        (IReadOnlyList<SavedMotif> motifs, IReadOnlyList<SavedVariation> variations) = session.Saved(piece.Project, piece.Notes, layerOff: false);
        Assert.Equal("m_0001", motifs[0].Id);
        Assert.Equal("v_0001", variations[0].Id);
        Assert.Equal("m_0001", variations[0].MotifId);
    }

    [Fact]
    public void A_new_selection_drops_the_chain_and_what_was_understood()
    {
        OpenPiece piece = Example();
        SelectionSession session = piece.Selection;
        session.Select(Bars(piece, 1, 4), MotifSelectionKind.Range);
        SelectionUnderstanding before = session.Understand(piece.State);
        string chip = before.Context.Motif.Facts.SelectMany(before.Chips).Select(offered => offered.Id).First();
        session.Try(piece.State, chip);

        session.Select(Bars(piece, 5, 8), MotifSelectionKind.Range);

        Assert.Null(session.Chain);
        Assert.Null(session.Chip);
        Assert.NotSame(before.Context, session.Understood(piece.State));
        Assert.Equal("noVariation", Assert.Throws<FacadeException>(session.Variation).Code);
    }

    [Fact]
    public void A_passage_of_time_is_kept_but_not_understood_and_not_saved_as_a_motif()
    {
        OpenPiece piece = Example();
        SelectionSession session = piece.Selection;
        session.Select(Bars(piece, 1, 2) with { NoteIds = [] }, MotifSelectionKind.Range);

        Assert.Equal("unavailable", Assert.Throws<FacadeException>(() => session.Understood(piece.State)).Code);
        Assert.DoesNotContain(session.Saved(piece.Project, piece.Notes, layerOff: false).Motifs, motif => motif.Id == session.MotifId);
    }
}