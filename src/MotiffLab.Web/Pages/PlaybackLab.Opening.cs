using Microsoft.JSInterop;

using MotiffLab.MusicCore.Analysis;

namespace MotiffLab.Web.Pages;

/// <summary>
/// Opening a long piece (#419): the score is engraved and on screen before the piece is read at its levels (its phrases,
/// sentences and structure), which takes seconds on a concert's worth of notes. What needs the levels before then reads
/// them first, so they are the same whenever they are read.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>How long opening waits for the score to show before reading the levels anyway.</summary>
    private static readonly TimeSpan EngravingWait = TimeSpan.FromSeconds(10);

    /// <summary>The player's edits the levels are still to be read with; null once they are read.</summary>
    private LevelEdits? _levelsToRead;

    /// <summary>Done when the score being opened is on screen (ScoreView raises Engraved).</summary>
    private TaskCompletionSource? _engraving;

    /// <summary>A new piece: its levels are read later, with no edits unless the project has its own.</summary>
    private void ReadLevelsLater()
    {
        _pieceStructure = null;
        _phrases = null;
        _sentences = null;
        _levelsToRead = LevelEdits.None;
    }

    /// <summary>The project's own edits for the levels: kept for the reading to come, or read with now if they were read.</summary>
    private void ReadLevelsWith(LevelEdits edits)
    {
        if (_levelsToRead is not null)
        {
            _levelsToRead = edits;
        }
        else
        {
            SuggestPhrases(edits);
        }
    }

    /// <summary>Reads the levels now when they are still to be read.</summary>
    private void EnsureLevels()
    {
        if (_levelsToRead is { } edits)
        {
            SuggestPhrases(edits);
        }
    }

    /// <summary>The next score shown is waited for (<see cref="ScoreShownAsync"/>).</summary>
    private void ExpectEngraving() => _engraving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Waits until the score expected is on screen, or a while when none comes.</summary>
    private async Task ScoreShownAsync()
    {
        if (_engraving is not { } engraving)
        {
            return;
        }

        StateHasChanged();
        await Task.WhenAny(engraving.Task, Task.Delay(EngravingWait));
        _engraving = null;
    }

    private void OnEngraved() => _engraving?.TrySetResult();

    /// <summary>Starts loading the engraver while the piece is read, so the score does not wait for it.</summary>
    private async Task WarmEngraverAsync()
    {
        try
        {
            await using IJSObjectReference module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/score/score-view.js");
            await module.InvokeVoidAsync("warmEngraver");
        }
        catch (JSException)
        {
            // The score loads it when it is first engraved.
        }
    }
}