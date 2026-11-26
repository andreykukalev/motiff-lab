using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

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

    /// <summary>How the corrections are laid over the notes the piece is open on.</summary>
    internal EditsLayer Layer => _layer;

    /// <summary>
    /// For Notes or a draft opened only to be shown (sources.notes.open, drafts.open): the piece they were opened from and
    /// what is shown; null for a piece open to work on.
    /// </summary>
    internal ShownView? ShownFrom { get; private set; }

    /// <summary>
    /// True for Notes or a draft opened only to be shown: the facade answers the calls that show them and refuses every
    /// other (<see cref="MusicCoreFacade"/>).
    /// </summary>
    internal bool ReadOnly => ShownFrom is not null;

    /// <summary>This piece, open only to be shown from now on, as <paramref name="shown"/> says.</summary>
    internal OpenPiece AsReadOnly(ShownView shown)
    {
        ShownFrom = shown;
        return this;
    }

    /// <summary>The corrections laid one by one over the notes, for the log they were laid from.</summary>
    private (NoteEditLog Log, EditsApplied Applied)? _layered;

    /// <summary>The ids of the motifs, variations and ideas left out when the piece opened, because they no longer fit the notes.</summary>
    private IReadOnlyList<string> _hidden = [];

    /// <summary>
    /// The layer named by an open request's "edits": "on" lays the corrections one by one, "off" leaves them out, and "piece"
    /// lays them one by one unless the piece's file says they are off (<see cref="Open"/>); none is strict.
    /// </summary>
    public static EditsLayer LayerOf(string? edits) => edits switch
    {
        null => EditsLayer.Strict,
        "on" => EditsLayer.Lenient,
        "off" => EditsLayer.Off,
        "piece" => EditsLayer.Lenient,
        _ => throw FacadeException.BadRequest("\"edits\" is \"on\", \"off\" or \"piece\"."),
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
            Selection.SaveBeside(State);
        }

        Selection.HideLeftOut(State, _hidden);
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
            SavedVariation? working = Selection.WorkingVariation;
            _layer = layer;
            // The piece remembers it in its file (version 0.5), so it opens so again on any device.
            Project = Project with { EditsOff = layer == EditsLayer.Off };
            RefreshNotes();
            Selection.FollowLayer(State, working);
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
                Selection.FollowNotes(State);
            }
        }

        return CorrectionsAnswer(LayerBar(request));
    }

    /// <summary>The bar a layer call answers for: "bar" when sent, else the first bar.</summary>
    private Bar LayerBar(Request request) => request.Has("bar") ? CorrectionBar(request) : Piece.Meter.GetBar(Piece.Meter.FirstBarNumber);

    /// <summary>
    /// Refuses a correction while the layer is off: the facade asks it before every name starting "corrections" but those
    /// flagged <see cref="CallFlags.WhileCorrectionsOff"/> ("corrections", "corrections.layer", "corrections.drop").
    /// </summary>
    internal void RefuseWhileOff()
    {
        if (_layer == EditsLayer.Off)
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
        json.WriteBoolean("editsOff", Project.EditsOff);
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
        JsonWrite.Texts(json, "hidden", _hidden);
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