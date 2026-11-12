using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// «Мои правки: вкл / выкл» (version 0.5, within contract 12): the corrections laid over the notes one by one, or not at
/// all. The app opens a piece with <c>"edits": "on"</c> or <c>"off"</c> (without it, strictly, as before) and switches
/// with <c>corrections.layer</c>; <c>corrections.drop</c> takes out corrections that no longer fit. The edit log is never
/// changed by the layer: only the player's drop or a correction changes it. While the layer is off the other corrections
/// calls are unavailable, so no correction is written against the notes as imported, and saving keeps the motifs and
/// variations as they were, so nothing is written from notes the corrections would change.
/// </summary>
internal sealed partial class OpenPiece
{
    private EditsLayer _layer = EditsLayer.Strict;

    /// <summary>The corrections laid one by one over the notes, for the log they were laid from.</summary>
    private (NoteEditLog Log, EditsApplied Applied)? _layered;

    /// <summary>
    /// The working motif's variation that could not be made again over the notes shown (the layer off, or a correction that
    /// no longer fits): saving keeps it until the player picks another passage, idea or chip.
    /// </summary>
    private SavedVariation? _hiddenVariation;

    /// <summary>The ids of the motifs, variations and ideas left out when the piece opened, because they no longer fit the notes.</summary>
    private IReadOnlyList<string> _hidden = [];

    /// <summary>The layer named by an open request's "edits": "on" lays the corrections one by one, "off" leaves them out; none is strict.</summary>
    public static EditsLayer LayerOf(string? edits) => edits switch
    {
        null => EditsLayer.Strict,
        "on" => EditsLayer.Lenient,
        "off" => EditsLayer.Off,
        _ => throw FacadeException.BadRequest("\"edits\" is \"on\" or \"off\"."),
    };

    /// <summary>The corrections laid one by one over the imported notes, read again only when the log changed.</summary>
    private EditsApplied Layered
    {
        get
        {
            if (_layered is { } cached && ReferenceEquals(cached.Log, Project.Edits))
            {
                return cached.Applied;
            }

            EditsApplied applied = Project.Edits.ApplyEach(Piece.Notes);
            _layered = (Project.Edits, applied);
            return applied;
        }
    }

    /// <summary>The imported notes with the corrections as the layer lays them.</summary>
    private IReadOnlyList<Note> Corrected() => _layer switch
    {
        EditsLayer.Off => Piece.Notes,
        EditsLayer.Lenient => Layered.Notes,
        _ => Project.Edits.Apply(Piece.Notes),
    };

    /// <summary>What a piece opened with a layer starts from: the motif to work on and the variation that could not be shown.</summary>
    private void StartLayer(OpenedProject opened)
    {
        _layer = opened.Layer;
        _hidden = opened.Hidden;
        if (opened.Layer == EditsLayer.Strict)
        {
            return;
        }

        if (opened.Motifs.Count == 0 && Project.Motifs.Count > 0)
        {
            // Every motif is hidden: a passage picked now is saved beside them, never over one.
            int number = 1;
            while (Project.Motifs.Any(motif => motif.Id == $"m_{number:D4}"))
            {
                number++;
            }

            MotifId = $"m_{number:D4}";
        }

        _hiddenVariation = Chain is null ? Project.Variations.FirstOrDefault(variation => variation.MotifId == MotifId && _hidden.Contains(variation.Id)) : null;
    }

    /// <summary>"on": true lays the corrections one by one, false leaves them out; the answer is the corrections of "bar" (else the first bar).</summary>
    private string SwitchLayer(Request request)
    {
        bool on = request.Bool("on") ?? throw FacadeException.BadRequest("Send \"on\": true or false.");
        if (_draftFrom is not null)
        {
            throw FacadeException.Unavailable(Words.T("Save or discard the note editing first.", "Сначала сохраните или отмените правку нот."));
        }

        EditsLayer layer = on ? EditsLayer.Lenient : EditsLayer.Off;
        if (layer != _layer)
        {
            SavedVariation? working = Chain is { Result: not null } chain ? SavedVariation.Create(MotifId.Replace("m_", "v_", StringComparison.Ordinal), MotifId, chain) : _hiddenVariation;
            _layer = layer;
            RefreshNotes();
            Reselect();
            Chain = null;
            _chip = null;
            _hiddenVariation = null;
            if (working is not null && Selection is { } selection)
            {
                try
                {
                    Chain = OpenedProject.Restore(working, selection, Notes);
                }
                catch (ProjectFileException)
                {
                    _hiddenVariation = working;
                }
            }
        }

        return CorrectionsAnswer(LayerBar(request));
    }

    /// <summary>"noteIds": the corrections of those notes that no longer fit are taken out of the log; the others stay.</summary>
    private string DropUnfit(Request request)
    {
        IReadOnlyList<string> ids = request.Texts("noteIds");
        if (ids.Count == 0)
        {
            throw FacadeException.BadRequest("Send \"noteIds\", the notes whose corrections no longer fit.");
        }

        NoteEditLog edits = Project.Edits.DropUnfit(Piece.Notes, ids);
        if (!edits.Equals(Project.Edits))
        {
            if (_draftFrom is not null)
            {
                _draftSteps.Push(Project.Edits);
            }

            Project = Project with { Edits = edits };
            IReadOnlyList<Note> before = Notes;
            RefreshNotes();
            if (!before.SequenceEqual(Notes))
            {
                Reselect();
                Chain = null;
                _chip = null;
            }
        }

        return CorrectionsAnswer(LayerBar(request));
    }

    /// <summary>The selection's passage over the notes as they are now; none when no note is left there.</summary>
    private void Reselect()
    {
        if (Selection is { } selection)
        {
            MotifSelection again = MotifSelector.Select(Notes, Piece.Meter, selection.Span, selection.Filter, MotifSelection.MaxStructureBars);
            Selection = again.NoteIds.Count > 0 ? again : null;
        }
    }

    /// <summary>The bar a layer call answers for: "bar" when sent, else the first bar.</summary>
    private Bar LayerBar(Request request) => request.Has("bar") ? CorrectionBar(request) : Piece.Meter.GetBar(Piece.Meter.FirstBarNumber);

    /// <summary>The corrections calls answer only "corrections", "corrections.layer" and "corrections.drop" while the layer is off.</summary>
    private void RefuseWhileOff(string name)
    {
        if (_layer == EditsLayer.Off && name is not ("corrections" or "corrections.layer" or "corrections.drop"))
        {
            throw FacadeException.Unavailable(Words.T("Corrections are off.", "Правки выключены."));
        }
    }

    /// <summary>The notes a correction of <paramref name="noteId"/> was laid on: it, and the notes whose corrections were moved onto it.</summary>
    private IEnumerable<string> RevertedFor(string noteId) => _layer == EditsLayer.Strict ? [noteId] : Layered.NamedFor(noteId);

    /// <summary>The notes the corrections now change, remove or add.</summary>
    private HashSet<string> EditedNow() => _layer switch
    {
        EditsLayer.Strict => [.. Project.Edits.EditedIds],
        EditsLayer.Off => [],
        _ => [.. Layered.EditedIds(Project.Edits)],
    };

    /// <summary>The layer and what became of each correction: "layer", "applied" (how many), "moved" and "unfit" with their notes and bars; nothing when strict.</summary>
    private void LayerFields(Utf8JsonWriter json)
    {
        if (_layer == EditsLayer.Strict)
        {
            return;
        }

        EditsApplied applied = Layered;
        json.WriteString("layer", _layer == EditsLayer.Off ? "off" : "on");
        json.WriteNumber("applied", applied.Applied.Count);
        json.WriteStartArray("moved");
        foreach (MovedEdit moved in applied.Moved)
        {
            json.WriteStartObject();
            json.WriteString("noteId", moved.Edit.NoteId);
            json.WriteString("to", moved.NoteId);
            json.WriteNumber("bar", BarOfEdit(moved.Edit));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("unfit");
        foreach (UnfitEdit unfit in applied.Unfit)
        {
            json.WriteStartObject();
            json.WriteString("noteId", unfit.Edit.NoteId);
            json.WriteNumber("bar", BarOfEdit(unfit.Edit));
            json.WriteString("reason", unfit.Reason switch
            {
                EditMisfit.Missing => "missing",
                EditMisfit.IdTaken => "idTaken",
                _ => "changed",
            });
            json.WriteString("text", MisfitText(unfit.Reason));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("hidden");
        foreach (string id in _hidden)
        {
            json.WriteStringValue(id);
        }

        json.WriteEndArray();
    }

    private string MisfitText(EditMisfit reason) => reason switch
    {
        EditMisfit.Missing => Words.T("The note is not there", "Ноты нет"),
        EditMisfit.IdTaken => Words.T("A note with this id is already there", "Нота с таким номером уже есть"),
        _ => Words.T("The note is different now", "Нота уже другая"),
    };

    /// <summary>The bar a correction is in: where the note it found started, or where an added note starts.</summary>
    private int BarOfEdit(NoteEdit edit)
    {
        Tick onset = (edit.Before ?? edit.After)?.Span.Onset ?? Tick.Zero;
        return Piece.Meter.BarAt(onset).Number;
    }
}