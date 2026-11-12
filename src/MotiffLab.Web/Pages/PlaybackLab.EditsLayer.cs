using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.Web.Workspace;

namespace MotiffLab.Web.Pages;

/// <summary>
/// «Мои правки: вкл / выкл» (an experiment, Settings · Experimental, version 0.5): the player's corrections laid over the
/// notes one by one, or left out to see the notes as recognised. music-core lays them (<see cref="NoteEditLog.ApplyEach"/>)
/// and says which no longer fit; the page shows the switch with the counts and the list, each with «Убрать». Whether the
/// corrections are off is kept in the project ("editsOff"), so the piece opens so on every device; what earlier builds kept
/// on this device by the piece's id (<see cref="MotiffLab.Web.Projects.Preferences"/>) still turns them off. The edit log changes only when the player takes a correction out or makes one. While they are off the
/// notes cannot be corrected, and saving keeps the motifs and variations as they were.
/// </summary>
public partial class PlaybackLab
{
    /// <summary>How the corrections are laid over the notes: strictly while the experiment is off.</summary>
    private EditsLayer _editsLayer = EditsLayer.Strict;

    /// <summary>The corrections laid one by one, for the log and notes they were laid from.</summary>
    private (NoteEditLog Log, IReadOnlyList<Note> Imported, EditsApplied Applied)? _layeredEdits;

    /// <summary>How many motifs, variations and ideas were left out when the piece opened, because they no longer fit the notes.</summary>
    private int _hiddenOnOpen;

    private bool EditsOff => _editsLayer == EditsLayer.Off;

    /// <summary>The imported notes with <paramref name="edits"/> as the layer lays them.</summary>
    private IReadOnlyList<Note> Corrected(NoteEditLog edits) => _editsLayer switch
    {
        EditsLayer.Off => _imported,
        EditsLayer.Lenient => LayeredOf(edits).Notes,
        _ => edits.Apply(_imported),
    };

    private EditsApplied LayeredOf(NoteEditLog edits)
    {
        if (_layeredEdits is { } cached && ReferenceEquals(cached.Log, edits) && ReferenceEquals(cached.Imported, _imported))
        {
            return cached.Applied;
        }

        EditsApplied applied = edits.ApplyEach(_imported);
        _layeredEdits = (edits, _imported, applied);
        return applied;
    }

    /// <summary>A piece opened from the device: the layer it was opened with, and a new motif's id when every saved one is hidden.</summary>
    private void StartEditsLayer(OpenedProject? opened)
    {
        _editsLayer = opened?.Layer ?? EditsLayer.Strict;
        _layeredEdits = null;
        _hiddenOnOpen = opened?.Hidden.Count ?? 0;
        if (opened is { Layer: not EditsLayer.Strict, Motifs.Count: 0, Project.Motifs.Count: > 0 } all)
        {
            // A passage picked now is saved beside the hidden motifs, never over one.
            int number = 1;
            while (all.Project.Motifs.Any(motif => motif.Id == $"m_{number:D4}"))
            {
                number++;
            }

            _motifId = $"m_{number:D4}";
        }
    }

    /// <summary>What the switch shows: none while the experiment is off or the piece has no corrections.</summary>
    private EditsLayerView? EditsLayerShown
    {
        get
        {
            if (_editsLayer == EditsLayer.Strict || _edits.IsEmpty || _meter is null)
            {
                return null;
            }

            EditsApplied applied = LayeredOf(_edits);
            EditsLayerRow[] unfit = [.. applied.Unfit.Select(edit => new EditsLayerRow(
                edit.Edit.NoteId,
                _meter.BarAt((edit.Edit.Before ?? edit.Edit.After)!.Value.Span.Onset).Number,
                (edit.Edit.Before ?? edit.Edit.After)!.Value.Pitch.ToString(),
                edit.Reason))];
            return new EditsLayerView(!EditsOff, _edits.Edits.Count, applied.Moved.Count, unfit, _hiddenOnOpen);
        }
    }

    /// <summary>The corrections on or off for this piece: saved first, then saved in the project, and the piece opens again with them so.</summary>
    private async Task SwitchEditsLayerAsync(bool on)
    {
        if (_project is not { } project || NoteEditing || on == !EditsOff)
        {
            return;
        }

        await SaveProjectAsync();
        // The piece's file says so (version 0.5, "editsOff"), so it opens so on every device; the device's own word, kept
        // by builds before the file did, is cleared.
        _project = (_project ?? project) with { EditsOff = !on };
        CurrentPiece.Saved(_project);
        await Projects.SaveAsync(_project);
        await Preferences.SetEditsOffAsync(project.Id, false);
        await CurrentPiece.OpenSavedAsync(project.Id);
    }

    /// <summary>A correction that no longer fits taken out of the piece's edit log, at the player's word.</summary>
    private Task DropUnfitEditAsync(string noteId) =>
        NoteEditing ? Task.CompletedTask : ApplyEditsAsync(_edits.DropUnfit(_imported, [noteId]));
}