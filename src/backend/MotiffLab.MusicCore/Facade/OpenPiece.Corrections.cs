using System.Text.Json;

using MotiffLab.MusicCore.Assistant;
using MotiffLab.MusicCore.Assistant.Conversation;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The JSON adapter of the corrections session (<see cref="CorrectionsSession"/>, audit W1.7): correcting the imported
/// notes (S1-I7; contract 4), for the iPad app's Fix notes in the Check drawer and the note editor on the roll (testing
/// item 15), and «Мои правки: вкл / выкл» (version 0.5, within contract 12). Each call's request read into typed values in
/// the order it always was, the session asked, the log it makes kept in the project, and the bar's corrections written as
/// the answer. Every call answers a bar, read before anything changes. The session's state is changed only by the session;
/// this file holds no state.
/// </summary>
/// <remarks>
/// The app opens a piece with <c>"edits": "on"</c> or <c>"off"</c> (without it, strictly, as before) and switches with
/// <c>corrections.layer</c>; <c>corrections.drop</c> takes out corrections that no longer fit. The edit log is never
/// changed by the layer: only the player's drop or a correction changes it. While the layer is off the other corrections
/// calls are unavailable, and saving keeps the motifs and variations as they were, so nothing is written from notes the
/// corrections would change. Notes taken from a performance (0022) are not corrected here.
/// </remarks>
internal sealed partial class OpenPiece
{
    internal static partial class Calls
    {
        /// <summary>The corrections and their layer.</summary>
        public static readonly FacadeCall<OpenPiece>[] Corrections =
        [
            new("corrections", static (piece, request) => piece.CorrectionsAnswer(piece.CorrectionBar(request)), CallFlags.WhileCorrectionsOff | CallFlags.OpenFirst),
            new("corrections.layer", static (piece, request) => piece.SwitchLayer(request), CallFlags.WhileCorrectionsOff),
            new("corrections.drop", static (piece, request) => piece.DropUnfit(request), CallFlags.WhileCorrectionsOff),
            new("corrections.begin", static (piece, request) => piece.BeginDraft(request)),
            new("corrections.save", static (piece, request) => piece.EndDraft(request, keep: true)),
            new("corrections.discard", static (piece, request) => piece.EndDraft(request, keep: false)),
            new("corrections.reset", static (piece, request) => piece.Corrected(piece.CorrectionBar(request), piece.Corrections.Reset(piece.State))),
            new("corrections.add", static (piece, request) => piece.AddNote(request)),
            new("corrections.acceptDraft", static (piece, request) => piece.AcceptDraft(request)),
            new("corrections.undoDraft", static (piece, request) => piece.UndoDraft(request)),
            new("corrections.variant", static (piece, request) => piece.TakeVariant(request)),
            new("corrections.set", static (piece, request) => piece.SetNote(request)),
            new("corrections.change", static (piece, request) => piece.MoveNote(request)),
            new("corrections.remove", static (piece, request) => piece.RemoveNote(request)),
            new("corrections.revert", static (piece, request) => piece.Corrected(piece.CorrectionBar(request), piece.Corrections.Revert(piece.State, request.Required("id")))),
            new("corrections.undo", static (piece, request) => piece.Corrected(piece.CorrectionBar(request), piece.Corrections.Undo(piece.State))),
        ];
    }

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

    /// <summary>What a piece opened with a layer starts from: the working motif's variation that could not be shown.</summary>
    private void StartLayer(OpenedProject opened)
    {
        Corrections.Start(opened.Layer, opened.Hidden);
        if (opened.Layer == EditsLayer.Strict)
        {
            return;
        }

        Selection.HideLeftOut(State, Corrections.Hidden);
    }

    /// <summary>"pitch" semitones, "onset" and "length" in sixteenths, "velocity" in tens: the note "id" moved by those steps.</summary>
    private string MoveNote(Request request)
    {
        Bar bar = CorrectionBar(request);
        Note note = Corrections.Correctable(State, request.Required("id"));
        int pitch = request.Int("pitch") ?? 0;
        int onset = request.Int("onset") ?? 0;
        int length = request.Int("length") ?? 0;
        int velocity = request.Int("velocity") ?? 0;
        return Corrected(bar, Corrections.Move(State, note, pitch, onset, length, velocity));
    }

    /// <summary>The note "id" as sent: "pitch" (MIDI), "onset" and "length" in ticks, each unchanged when not sent.</summary>
    private string SetNote(Request request)
    {
        Bar bar = CorrectionBar(request);
        Note note = Corrections.Correctable(State, request.Required("id"));
        int? pitch = request.Int("pitch");
        long? onset = request.Long("onset");
        long? length = request.Long("length");
        return Corrected(bar, Corrections.Set(State, note, pitch, onset, length));
    }

    /// <summary>The note "id" taken out.</summary>
    private string RemoveNote(Request request)
    {
        Bar bar = CorrectionBar(request);
        Note note = Corrections.Correctable(State, request.Required("id"));
        return Corrected(bar, Corrections.Remove(State, note));
    }

    /// <summary>A note added where "onset" (ticks) says, "pitch" (MIDI) and "length" (ticks) long; the track and hand of the nearest note unless sent.</summary>
    private string AddNote(Request request)
    {
        int pitch = request.Int("pitch") ?? throw FacadeException.BadRequest("Send \"pitch\".");
        long onset = request.Long("onset") ?? throw FacadeException.BadRequest("Send \"onset\", in ticks.");
        long length = request.Long("length") ?? CanonicalTime.TicksPerQuarter / 2;
        if (pitch is < 0 or > Pitch.MaxMidiNumber || onset < 0 || length <= 0)
        {
            throw FacadeException.BadRequest("\"pitch\" runs from 0 to 127, \"onset\" from 0 and \"length\" from 1.");
        }

        int? velocity = request.Int("velocity");
        int? track = request.Int("track");
        Hand? hand = request.Text("hand") switch
        {
            "left" => Hand.Left,
            "right" => Hand.Right,
            _ => null,
        };
        Bar bar = CorrectionBar(request);
        NoteEditLog? edits = Corrections.Add(State, pitch, onset, length, velocity, track, hand, out string? added);
        return Corrected(bar, edits, added);
    }

    /// <summary>
    /// corrections.acceptDraft {"bar", "tool", "parameters", "index"} (version 0.6.1, #582 item 17.3, the iPad's Accept of the
    /// assistant's draft): the draft is made again from the call that made it (tools are pure) over the notes as they are, and
    /// its edits join the corrections. Refused when the draft no longer fits the notes or touches a note taken from a version.
    /// The model never calls this: only the player's Accept. corrections.undoDraft takes the whole draft back at once.
    /// </summary>
    private string AcceptDraft(Request request)
    {
        JsonElement parameters = request.Element("parameters") ?? throw FacadeException.BadRequest("Send \"parameters\", the tool's.");
        var call = new SessionDraft("d1", request.Required("tool"), parameters, request.Int("index") ?? 0, DraftState.Proposed);
        AssistantDraft draft = CorrectionsSession.Acceptable(State, call.Make(Assistant));
        Bar bar = CorrectionBar(request);
        return Corrected(bar, Corrections.Accept(State, draft));
    }

    /// <summary>
    /// corrections.variant {"bar", "staves": [{"part", "staff", "notes": [{"onset" (ticks in the bar), "pitch", "length"}]}]}
    /// (v1.3 i-2, ADR 0035 section 6): another engine's reading of a scan's bar to check, as scan.join answered it, chosen by
    /// the player (<see cref="CorrectionsSession.TakeVariant"/>). Only that bar changes: a note of the variant starting
    /// outside it is refused, so no bar is ever fixed from another.
    /// </summary>
    private string TakeVariant(Request request)
    {
        Bar bar = CorrectionBar(request);
        VariantStaff[] staves = [.. request.Elements("staves").Select(staff => VariantStaffOf(Request.Of(staff), bar))];
        if (staves.Length == 0)
        {
            throw FacadeException.BadRequest("Send \"staves\", the variant's.");
        }

        return Corrected(bar, Corrections.TakeVariant(State, bar, staves));
    }

    /// <summary>One staff of a variant: "part" (from 1), "staff" (from 1) and its "notes" in the bar's ticks.</summary>
    private static VariantStaff VariantStaffOf(Request staff, Bar bar)
    {
        int part = staff.Int("part") ?? throw FacadeException.BadRequest("Each staff needs \"part\", from 1.");
        int number = staff.Int("staff") ?? throw FacadeException.BadRequest("Each staff needs \"staff\", from 1.");
        return new VariantStaff(part - 1, number, [.. staff.Elements("notes").Select(note => VariantNoteOf(Request.Of(note), bar))]);
    }

    private static NoteValues VariantNoteOf(Request note, Bar bar)
    {
        long onset = bar.Span.Onset.Value + (note.Long("onset") ?? throw FacadeException.BadRequest("Each note needs \"onset\", in ticks in the bar."));
        int pitch = note.Int("pitch") ?? throw FacadeException.BadRequest("Each note needs \"pitch\".");
        long length = note.Long("length") ?? throw FacadeException.BadRequest("Each note needs \"length\", in ticks.");
        return bar.Span.Contains(new Tick(onset)) && pitch is >= 0 and <= Pitch.MaxMidiNumber && length > 0 && length <= long.MaxValue - onset
            ? new NoteValues(new Pitch(pitch), new TickSpan(new Tick(onset), length), Note.MinVelocity)
            : throw FacadeException.BadRequest("A note of the variant starts outside its bar, or its pitch or length is out of range.");
    }

    private string UndoDraft(Request request)
    {
        _ = Corrections.AcceptedBefore(State);
        Bar bar = CorrectionBar(request);
        return Corrected(bar, Corrections.UndoDraft(State));
    }

    private string BeginDraft(Request request)
    {
        Bar bar = CorrectionBar(request);
        Corrections.Begin(State);
        return CorrectionsAnswer(bar);
    }

    private string EndDraft(Request request, bool keep)
    {
        _ = Corrections.DraftBegun();
        Bar bar = CorrectionBar(request);
        return Corrected(bar, Corrections.End(State, keep));
    }

    /// <summary>"on": true lays the corrections one by one, false leaves them out; the answer is the corrections of "bar" (else the first bar).</summary>
    private string SwitchLayer(Request request)
    {
        bool on = request.Bool("on") ?? throw FacadeException.BadRequest("Send \"on\": true or false.");
        Corrections.RefuseWhileEditing(Words);
        Bar bar = LayerBar(request);
        if (Corrections.Switch(on))
        {
            SavedVariation? working = Selection.WorkingVariation;
            // The piece remembers it in its file (version 0.5), so it opens so again on any device.
            Project = Project with { EditsOff = Corrections.Layer == EditsLayer.Off };
            RefreshNotes();
            Selection.FollowLayer(State, working);
        }

        return CorrectionsAnswer(bar);
    }

    /// <summary>"noteIds": the corrections of those notes that no longer fit are taken out of the log; the others stay.</summary>
    private string DropUnfit(Request request)
    {
        IReadOnlyList<string> ids = request.Texts("noteIds");
        if (ids.Count == 0)
        {
            throw FacadeException.BadRequest("Send \"noteIds\", the notes whose corrections no longer fit.");
        }

        Bar bar = LayerBar(request);
        if (Corrections.DropUnfit(State, ids) is { } edits)
        {
            Project = Project with { Edits = edits };
            RefreshNotesFollowingSelection();
        }

        return CorrectionsAnswer(bar);
    }

    /// <summary>
    /// The corrections the session made (<paramref name="edits"/>, null when nothing changed) kept in the project, the notes
    /// read again, and the selection kept over them (a variation in progress was made from the old ones); then the bar's
    /// corrections, with the note just <paramref name="added"/>.
    /// </summary>
    private string Corrected(Bar bar, NoteEditLog? edits, string? added = null)
    {
        if (edits is not null)
        {
            Project = Project with { Edits = edits };
            RefreshNotes();
            Selection.FollowNotes(State);
        }

        return CorrectionsAnswer(bar, added);
    }

    /// <summary>The bar's corrections (<see cref="CorrectionsSession.Bar"/>) as the answer.</summary>
    private string CorrectionsAnswer(Bar bar, string? added = null) =>
        Answer.Write(json => Answer.Corrections(json, Corrections.Bar(State, bar), added, Piece.Meter, Words));

    private Bar CorrectionBar(Request request)
    {
        int number = request.Int("bar") ?? throw FacadeException.BadRequest("Send \"bar\".");
        Bar bar;
        try
        {
            bar = Piece.Meter.GetBar(number);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw FacadeException.BadRequest($"There is no bar {number}.");
        }

        return bar.Span.Onset < Piece.End ? bar : throw FacadeException.BadRequest($"There is no bar {number}.");
    }

    /// <summary>The bar a layer call answers for: "bar" when sent, else the first bar.</summary>
    private Bar LayerBar(Request request) => request.Has("bar") ? CorrectionBar(request) : Piece.Meter.GetBar(Piece.Meter.FirstBarNumber);
}