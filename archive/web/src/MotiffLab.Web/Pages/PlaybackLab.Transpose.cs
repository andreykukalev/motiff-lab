using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Reference.Handbook;
using MotiffLab.Web.Score;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The piece in another key (version 0.5, #351, «Справочник» behind experimental.harmony): chosen on the circle, it is heard
/// there, from where the selection starts to its end or from the playhead to the end, and its notes are shown there in place
/// of the score, until the piece's own key takes the view back. music-core moves the notes (<see cref="Transposition"/>);
/// nothing is written into the piece, its analysis or its file. While the notes are shown moved, Play plays them moved.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The piece moved to the key chosen, and the notes it was moved from: when they change, the view is dropped.</summary>
    private (TransposedPiece Piece, IReadOnlyList<Note> From, ScoreSource? Score)? _transposed;

    /// <summary>The piece moved, while the notes it was moved from are still the piece's; null when it is shown as it is.</summary>
    private TransposedPiece? Transposed => _transposed is { } moved && ReferenceEquals(moved.From, _notes) ? moved.Piece : null;

    /// <summary>The moved piece's score, while its notes are shown in another key.</summary>
    private ScoreSource? TransposedScore => Transposed is not null ? _transposed?.Score : null;

    /// <summary>The piece's title and the key it is shown in, for the pill over the score.</summary>
    private string TransposedText(TransposedPiece moved) =>
        Text.Format("transpose.shown", ScoreTitle.Of(_title) ?? Text["reference.thePiece"], AssemblyWords.KeyPrepositional(moved.To, Words));

    /// <summary>
    /// The piece in a key, asked for on a key's card: heard there, or its notes shown there; its own key shows it as it is.
    /// A key of the other mode is not a transposition, and the card does not offer it.
    /// </summary>
    private async Task TransposeAsync(PieceInKey request)
    {
        if (_notes is not { } notes || _meter is not { } meter || _score is not { } score || _referencePieceKey is not { } piece || request.Key.Mode != piece.Key.Mode)
        {
            return;
        }

        if (request.Key == piece.Key)
        {
            _transposed = null;
            return;
        }

        TransposedPiece moved = Transposed is { } shown && shown.To == request.Key ? shown : Transposition.Of(notes, [.. NotationKeys()], piece.Key, request.Key);
        if (request.Hear)
        {
            await HearTransposedAsync(moved);
            return;
        }

        GeneratedNotation notation = NotationGenerator.Generate(moved.Notes, meter, moved.Keys, score.End, ScoreTitle.Of(_title), spelling: moved.Spelling);
        _transposed = (moved, notes, ScoreSource.FromNotes(notation));
        _referenceOpen = false;
    }

    /// <summary>Takes the view back to the piece's own key.</summary>
    private async Task UntransposeAsync()
    {
        if (_scaleHeard)
        {
            await StopScaleAsync();
        }

        _transposed = null;
    }

    /// <summary>Play while the notes are shown moved: the moved notes from the selection or the playhead.</summary>
    private Task HearShownTransposedAsync() => Transposed is { } moved ? HearTransposedAsync(moved) : Task.CompletedTask;

    /// <summary>
    /// The moved notes heard once on their own over the piece's timeline: the selection's, or from the playhead to the end
    /// (from the start when it stands at the end).
    /// </summary>
    private async Task HearTransposedAsync(TransposedPiece moved)
    {
        if (_score is not { } score)
        {
            return;
        }

        if (_scaleHeard)
        {
            await StopScaleAsync();
        }

        Tick from = Player.Position < score.End ? Player.Position : Tick.Zero;
        TickSpan span = _selection?.Span ?? TickSpan.FromBounds(from, score.End);
        IReadOnlyList<Note> heard = [.. Voiced([.. moved.Notes.Where(note => span.Contains(note.Span.Onset))])];
        await HearAloneAsync(heard, span);
    }
}