using MotiffLab.MusicCore.Facade;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Invitations;
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

    /// <summary>Bars 1 to 4 selected and the first chip their motif offers tried.</summary>
    private static SelectionSession Tried(OpenPiece piece)
    {
        SelectionSession session = piece.Selection;
        session.Select(piece.State, Bars(piece, 1, 4), MotifSelectionKind.Range);
        SelectionUnderstanding understood = session.Understand(piece.State);
        session.Try(piece.State, understood.Context.Motif.Facts.SelectMany(understood.Chips).Select(offered => offered.Id).First());
        return session;
    }

    /// <summary>The piece as <paramref name="state"/> with the session's motifs and variations saved into its project.</summary>
    private static SessionPiece SavedBy(SelectionSession session, SessionPiece state)
    {
        (IReadOnlyList<SavedMotif> motifs, IReadOnlyList<SavedVariation> variations) = session.Saved(state, layerOff: false);
        return state with { Project = state.Project with { Motifs = motifs, Variations = variations } };
    }

    [Fact]
    public void A_chip_offered_by_what_was_understood_becomes_the_chain_and_is_saved_with_the_working_motif()
    {
        OpenPiece piece = Example();
        SelectionSession session = piece.Selection;
        session.Select(piece.State, Bars(piece, 1, 4), MotifSelectionKind.Range);

        SelectionUnderstanding understood = session.Understand(piece.State);
        Assert.Same(understood.Context, session.Understood(piece.State));
        string chip = understood.Context.Motif.Facts.Concat(understood.Context.Structure.Facts)
            .SelectMany(understood.Chips).Select(offered => offered.Id).First();
        VariationChain chain = session.Try(piece.State, chip);

        Assert.Same(chain, session.Variation());
        Assert.Equal(chip, session.Chip?.Id);
        (IReadOnlyList<SavedMotif> motifs, IReadOnlyList<SavedVariation> variations) = session.Saved(piece.State, layerOff: false);
        Assert.Equal("m_0001", motifs[0].Id);
        Assert.Equal("v_0001", variations[0].Id);
        Assert.Equal("m_0001", variations[0].MotifId);
    }

    [Fact]
    public void A_new_selection_drops_the_chain_and_what_was_understood()
    {
        OpenPiece piece = Example();
        SelectionSession session = Tried(piece);
        InvitationContext before = session.Understood(piece.State);

        session.Select(piece.State, Bars(piece, 5, 8), MotifSelectionKind.Range);

        Assert.Null(session.Chain);
        Assert.Null(session.Chip);
        Assert.NotSame(before, session.Understood(piece.State));
        Assert.Equal("noVariation", Assert.Throws<FacadeException>(session.Variation).Code);
    }

    [Fact]
    public void Another_passage_after_a_save_is_saved_beside_the_working_motif_and_the_same_passage_again_is_not()
    {
        // #821: the working motif keeps its variations in the file once another passage is chosen.
        OpenPiece piece = Example();
        SelectionSession session = Tried(piece);
        SessionPiece saved = SavedBy(session, piece.State);

        session.Select(saved, Bars(piece, 1, 4), MotifSelectionKind.Range);
        Assert.Equal("m_0001", session.MotifId);
        session.Select(saved, Bars(piece, 5, 8), MotifSelectionKind.Range);
        Assert.Equal("m_0002", session.MotifId);

        (IReadOnlyList<SavedMotif> motifs, IReadOnlyList<SavedVariation> variations) = session.Saved(saved, layerOff: false);
        Assert.Equal(["m_0002", "m_0001"], motifs.Select(motif => motif.Id));
        Assert.Same(saved.Project.Motifs[0], motifs[1]);
        Assert.Equal(saved.Project.Variations, variations);
    }

    [Fact]
    public void With_no_passage_left_to_save_the_working_motif_stays_while_another_variation_of_it_does()
    {
        // #821: the correction took every note of the passage; the variation shown went with its chain, the other stays, and
        // so does the motif it cannot open without.
        OpenPiece piece = Example();
        SelectionSession session = Tried(piece);
        SessionPiece saved = SavedBy(session, piece.State);
        SavedVariation other = saved.Project.Variations[0] with { Id = "v_0002" };
        saved = saved with { Project = saved.Project with { Variations = [.. saved.Project.Variations, other] } };

        session.FollowNotes(saved with { Notes = [] });

        (IReadOnlyList<SavedMotif> motifs, IReadOnlyList<SavedVariation> variations) = session.Saved(saved, layerOff: false);
        Assert.Equal(saved.Project.Motifs, motifs);
        Assert.Equal([other], variations);
    }

    [Fact]
    public void A_passage_of_time_is_kept_but_not_understood_and_not_saved_as_a_motif()
    {
        OpenPiece piece = Example();
        SelectionSession session = piece.Selection;
        session.Select(piece.State, Bars(piece, 1, 2) with { NoteIds = [] }, MotifSelectionKind.Range);

        Assert.Equal("unavailable", Assert.Throws<FacadeException>(() => session.Understood(piece.State)).Code);
        Assert.DoesNotContain(session.Saved(piece.State, layerOff: false).Motifs, motif => motif.Id == session.MotifId);
    }

    [Fact]
    public void What_was_understood_is_read_again_when_the_notes_or_the_Check_flags_change()
    {
        OpenPiece piece = Example();
        SelectionSession session = piece.Selection;
        session.Select(piece.State, Bars(piece, 1, 4), MotifSelectionKind.Range);
        SessionPiece state = piece.State;
        InvitationContext first = session.Understood(state);

        Assert.Same(first, session.Understood(state));
        SessionPiece flagged = state with { Project = state.Project with { Check = [new CheckFlag(2, CheckSource.Scan, CheckReason.BarTooLong, Resolved: false)] } };
        InvitationContext checkedAgain = session.Understood(flagged);
        Assert.NotSame(first, checkedAgain);
        Assert.NotSame(checkedAgain, session.Understood(flagged with { Notes = [.. state.Notes] }));
    }

    [Fact]
    public void Following_the_notes_keeps_the_passage_while_notes_are_left_and_drops_the_chain()
    {
        OpenPiece piece = Example();
        SelectionSession session = Tried(piece);
        MotifSelection before = session.Required();

        session.FollowNotes(piece.State);

        Assert.Equal(before.Span, session.Current?.Span);
        Assert.Null(session.Chain);
        Assert.Null(session.Chip);
        session.FollowNotes(piece.State with { Notes = [] });
        Assert.Null(session.Current);
    }

    [Fact]
    public void Switching_the_layer_makes_the_working_variation_again_or_keeps_it_hidden_when_it_no_longer_fits()
    {
        OpenPiece piece = Example();
        SelectionSession session = Tried(piece);
        SavedVariation working = session.WorkingVariation!;

        session.FollowLayer(piece.State, working);
        Assert.NotNull(session.Chain);
        Assert.Null(session.HiddenVariation);

        SavedVariation unfit = working with { Steps = [new SavedStep("no.such.operation", working.Steps[0].Parameters)] };
        session.FollowLayer(piece.State, unfit);
        Assert.Null(session.Chain);
        Assert.Same(unfit, session.HiddenVariation);
        Assert.Same(unfit, session.WorkingVariation);
    }

    [Fact]
    public void An_idea_brought_back_has_no_chip_of_an_earlier_try()
    {
        OpenPiece piece = Example();
        SelectionSession session = Tried(piece);
        VariationChain idea = session.Variation();

        session.Open(piece.State, Bars(piece, 1, 4), idea);

        Assert.Same(idea, session.Chain);
        Assert.Null(session.Chip);
    }
}