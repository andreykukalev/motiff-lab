using Microsoft.AspNetCore.Components;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Import;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Workspace;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Several pieces in a score or recording, an experiment behind Settings · Experimental (version 0.5; it replaced the
/// automatic bookmarks of 2026-11-11 and pieces in a file of 0.4): the Contents review where the pieces of a long score or
/// recording begin. music-core proposes the boundaries (<see cref="SectionFinder.FindPieces"/>: in a recording a pause
/// alone is not one), the player removes, moves, adds and names them (<see cref="FoundSections"/>), and Confirm keeps them
/// as the pieces layer, ordinary bookmarks in the category "pieces" (<see cref="PieceLayer"/>), in one save. Nothing is
/// saved before Confirm. A recording added with «В записи несколько пьес» arrives with the number of pieces
/// (<c>&amp;pieces=N</c>), which is kept nowhere.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The experiment's switch in Settings · Experimental, off by default and kept on the device.</summary>
    private const string PiecesSetting = PartPlace.Setting;

    /// <summary>The number of pieces the player gave when adding the recording, for the first review only.</summary>
    [SupplyParameterFromQuery(Name = "pieces")]
    public int? PiecesHint { get; set; }

    /// <summary>The piece as imported, which the proposals read its bar lines, texts, keys and tempo from.</summary>
    private ImportedPiece? _sectionSource;

    /// <summary>The pieces under review, with the player's changes; null while the review is closed or not read yet.</summary>
    private FoundSections? _review;

    /// <summary>Whether the player closed the review (confirmed or cancelled): it does not open again by itself.</summary>
    private bool _reviewClosed;

    /// <summary>The number of pieces the review was last found with; null to let music-core decide.</summary>
    private int? _reviewCount;

    /// <summary>The recording's clock, for each piece's time; read once a piece.</summary>
    private (ImportedPiece Piece, HeardClock? Clock)? _heardClock;

    private bool PiecesOn => SettingsStore.IsOn(PiecesSetting);

    /// <summary>The pieces the project keeps, in score order.</summary>
    private IReadOnlyList<LayerPiece> KeptPieces => _project is { } project && _meter is { } meter ? PieceLayer.Read(project, meter) : [];

    /// <summary>
    /// The pieces under review while the experiment is on: opened by itself, from music-core's proposals, while the piece
    /// keeps no pieces yet and the player has not closed it; null otherwise.
    /// </summary>
    private FoundSections? PiecesUnderReview
    {
        get
        {
            if (!PiecesOn || _sectionSource is null || _notes is null || _score is null)
            {
                return null;
            }

            if (_review is null && !_reviewClosed && KeptPieces.Count == 0)
            {
                _review = FindPieces(_reviewCount);
            }

            return _review;
        }
    }

    /// <summary>The bar the selection starts at, for «Новая пьеса отсюда».</summary>
    private int? SelectionStartBar => _selection is { } selection && _meter is { } meter ? meter.BarAt(selection.Span.Onset).Number : null;

    /// <summary>A new piece: its own proposals, read when the Contents first ask for them, with the number given on adding it.</summary>
    private void ReadSectionsOf(ImportedPiece piece)
    {
        _sectionSource = piece;
        _review = null;
        _reviewClosed = false;
        _heardClock = null;
        _reviewCount = PiecesHint is >= 1 ? PiecesHint : null;
        if (_reviewCount is not null && PiecesOn)
        {
            _contentsOpen = true;
        }
    }

    private FoundSections FindPieces(int? count) =>
        SectionFinder.FindPieces(SectionEvidence.Of(_sectionSource!, _notes!, _score!.Tempos), count);

    private void FindPiecesAgain(int? count)
    {
        if (_sectionSource is null || _notes is null || _score is null)
        {
            return;
        }

        _reviewCount = count;
        _review = FindPieces(count);
    }

    private void ReviewKeptPieces()
    {
        if (_project is { } project && _meter is { } meter && _score is { } score)
        {
            _review = PieceLayer.Review(project, meter, score.End);
        }
    }

    private void ChangePieces(Func<FoundSections, FoundSections> change)
    {
        if (_review is { } review)
        {
            _review = change(review);
        }
    }

    private void CancelPiecesReview()
    {
        _review = null;
        _reviewClosed = true;
    }

    /// <summary>A piece tapped: the score goes there and selects it, as a bookmark does.</summary>
    private Task GoToPieceAsync(TickSpan span)
    {
        _bookmarkDraft = null;
        return SelectAsync(span, note: null);
    }

    /// <summary>Confirm: the pieces as reviewed become the pieces layer, replacing the one kept before, in one save.</summary>
    private async Task ConfirmPiecesAsync()
    {
        if (_review is not { } review || _meter is not { } meter || _score is not { } score)
        {
            return;
        }

        PieceStart[] starts = [.. review.Items.Select(piece => new PieceStart(piece.PieceTitleIn(Words), piece.FirstBar))];
        await SaveMarksAsync(project => PieceLayer.Save(project, starts, meter, score.End, Words, DateTimeOffset.Now), Text.Count("pieces.kept", starts.Length));
        _review = null;
        _reviewClosed = true;
    }

    /// <summary>A piece's bars in words, with its stretch of a recording (music-core's <see cref="HeardCut.StretchOf"/>) or its pages.</summary>
    private string PiecePlace(TickSpan span, int first, int last)
    {
        string bars = BarsOf(first, last);
        if (HeardClockOf() is { } clock && _sectionSource is { } piece)
        {
            (long from, long to) = HeardCut.StretchOf(piece, clock, span);
            return $"{bars} · {PartPlace.Times(from, to)}";
        }

        if (_project is { } project && _sectionSource is { } source && PieceParts.PagesOf(project, source) is { } pages)
        {
            int firstPage = pages.PageOf(span.Onset), lastPage = pages.PageOf(new Tick(span.End.Value - 1));
            string place = PartPlace.Of(Text, PartOf.OnPages(PieceParts.OriginalId, firstPage, lastPage));
            return $"{bars} · {place}";
        }

        return bars;
    }

    /// <summary>A layer piece's stretch of the recording heard, in milliseconds; null for notes not heard.</summary>
    private (long From, long To)? PieceStretch(TickSpan span) =>
        HeardClockOf() is { } clock && _sectionSource is { } piece ? HeardCut.StretchOf(piece, clock, span) : null;

    private HeardClock? HeardClockOf()
    {
        if (_project is not { Transcription: not null } project || _sectionSource is not { } piece)
        {
            return null;
        }

        if (_heardClock is not { } kept || !ReferenceEquals(kept.Piece, piece))
        {
            _heardClock = (piece, HeardClock.Of(project, piece));
        }

        return _heardClock.Value.Clock;
    }
}