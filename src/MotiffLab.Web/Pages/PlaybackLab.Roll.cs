using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Time;
using MotiffLab.Web.Playback;
using MotiffLab.Web.Roll;
using MotiffLab.Web.Understand;

namespace MotiffLab.Web.Pages;

/// <summary>
/// The piano roll and the score pointing at the same note (0018 section 4): a note chosen on the roll shows in cobalt on the
/// score, and a note tapped on the score is chosen on the roll with its name, while the roll is open. The roll spells its
/// names for the key signatures the score is written with, and shows the bars Check flags.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>The notes chosen on the roll, as the score points at them; null for none.</summary>
    private IReadOnlyList<HighlightPlace>? _pointed;

    /// <summary>The note last tapped on the score, for the roll to choose.</summary>
    private NotePoint? _rollPoint;

    /// <summary>The key signatures for the roll, the same list until the piece's keys or the key heard change.</summary>
    private (IReadOnlyList<KeySignature> Keys, MusicalKeyHeard Heard, IReadOnlyList<KeyChange> Changes)? _rollKeys;

    private IReadOnlyList<KeyChange> RollKeys
    {
        get
        {
            var heard = new MusicalKeyHeard(_heardKey?.Fifths);
            if (_rollKeys is { } cached && ReferenceEquals(cached.Keys, _keys) && cached.Heard == heard)
            {
                return cached.Changes;
            }

            IReadOnlyList<KeyChange> changes = [.. NotationKeys()];
            _rollKeys = (_keys, heard, changes);
            return changes;
        }
    }

    /// <summary>The notes chosen on the roll point at the same notes on the score.</summary>
    private void OnRollNotesChosen(IReadOnlyList<string> ids)
    {
        Note[] notes = [.. ids.Select(id => _notes?.FirstOrDefault(note => note.Id == id)).OfType<Note>()];
        _pointed = notes.Length == 0
            ? null
            : [.. notes.Select(note => new HighlightPlace(note.Span.Onset.Value, note.Span.End.Value, [new HighlightNote(note.Id, note.Span.Onset.Value, note.Span.End.Value, note.Pitch.MidiNumber)]))];
    }

    /// <summary>A note tapped on the score is chosen on the roll, when the roll is open.</summary>
    private void OnScoreNotePicked(NotePoint point)
    {
        if (_rollOpen)
        {
            _rollPoint = point;
        }
    }

    /// <summary>
    /// The playhead moved on the roll (Andrei, v0.2): dragged or nudged it only moves, playing on from there while it plays;
    /// a tap plays from there, looping the selection first when nothing loops yet. It stays inside the loop, whose span and
    /// the tempo stay as they were.
    /// </summary>
    private async Task OnPlayheadMovedAsync(RollSeek seek)
    {
        if (seek.Play && !Looping && _selection is { NoteIds.Count: > 0 })
        {
            await ToggleLoopSelectionAsync();
        }

        Tick at = seek.At;
        if (Player.Loop is { } loop && loop.End > loop.Onset)
        {
            at = new Tick(Math.Clamp(at.Value, loop.Onset.Value, loop.End.Value - 1));
        }

        await Player.SeekAsync(at);
        if (seek.Play && Player.State == PlaybackState.Stopped)
        {
            await PlayAsync();
        }
    }

    /// <summary>The key heard in a recording, by its sharps or flats, compared by value.</summary>
    private readonly record struct MusicalKeyHeard(int? Fifths);
}