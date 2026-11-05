using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// Correcting the imported notes (S1-I7; contract 4), for the iPad app's Fix notes in the Check drawer as the web app's
/// note corrections: the notes of a bar, a note a step higher, lower, earlier, later, shorter, longer, softer or louder,
/// taken out, put back as imported, and the last correction undone. Each correction is a step in the project's edit log;
/// the imported notes never change. Notes taken from a performance (0022) are not corrected here.
/// </summary>
internal sealed partial class OpenPiece
{
    /// <summary>A step in time: a sixteenth, as in the web app.</summary>
    private const long CorrectionStep = CanonicalTime.TicksPerQuarter / 4;

    private const int VelocityStep = 10;

    private string CallOnCorrections(string name, Request request) => name switch
    {
        "corrections" => CorrectionsAnswer(CorrectionBar(request)),
        "corrections.change" => Correct(request, edits => edits.Change(Piece.Notes, CorrectableNote(request).Id, Changed(CorrectableNote(request), request))),
        "corrections.remove" => Correct(request, edits => edits.Remove(Piece.Notes, CorrectableNote(request).Id)),
        "corrections.revert" => Correct(request, edits => edits.Revert(request.Required("id"))),
        "corrections.undo" => Correct(request, edits => edits.IsEmpty ? throw FacadeException.Unavailable("There is no correction to undo.") : edits.Undo()),
        _ => throw new FacadeException("unknownCall", $"There is no call \"{name}\"."),
    };

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

    /// <summary>The note named by "id" as it sounds now, refused when it came from a performance.</summary>
    private Note CorrectableNote(Request request)
    {
        string id = request.Required("id");
        if (PerformanceTake.IsTaken(id))
        {
            throw FacadeException.Unavailable("This note comes from a performance: restore the bar to correct the print.");
        }

        return Printed.FirstOrDefault(note => note.Id == id) ?? throw new FacadeException("notFound", $"There is no note {id}.");
    }

    /// <summary>The note's values moved by the steps asked for: "pitch" semitones, "onset" and "length" in sixteenths, "velocity" in tens.</summary>
    private static NoteValues Changed(Note note, Request request)
    {
        int pitch = request.Int("pitch") ?? 0;
        long onset = (request.Int("onset") ?? 0) * CorrectionStep;
        long length = (request.Int("length") ?? 0) * CorrectionStep;
        int velocity = (request.Int("velocity") ?? 0) * VelocityStep;
        return new(
            new Pitch(Math.Clamp(note.Pitch.MidiNumber + pitch, 0, Pitch.MaxMidiNumber)),
            new TickSpan(new Tick(Math.Max(0, note.Span.Onset.Value + onset)), Math.Max(1, note.Span.Duration + length)),
            Math.Clamp(note.Velocity + velocity, Note.MinVelocity, Note.MaxVelocity));
    }

    private string Correct(Request request, Func<NoteEditLog, NoteEditLog> change)
    {
        Bar bar = CorrectionBar(request);
        NoteEditLog edits;
        try
        {
            edits = change(Project.Edits);
            edits.Apply(Piece.Notes);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw FacadeException.BadRequest(exception.Message);
        }

        if (!edits.Equals(Project.Edits))
        {
            Project = Project with { Edits = edits };
            RefreshNotes();
            // The selection keeps its passage over the corrected notes; a variation in progress was made from the old ones.
            if (Selection is { } selection)
            {
                MotifSelection again = MotifSelector.Select(Notes, Piece.Meter, selection.Span, selection.Filter, MotifSelection.MaxStructureBars);
                Selection = again.NoteIds.Count > 0 ? again : null;
            }

            Chain = null;
            _trial = null;
            _chip = null;
        }

        return CorrectionsAnswer(bar);
    }

    /// <summary>
    /// The bar's notes as they sound, each with where it is, how long, how loud and whether the player corrected it; the
    /// imported notes taken out anywhere, to put back; and how many corrections the log holds.
    /// </summary>
    private string CorrectionsAnswer(Bar bar) => Answer.Write(json =>
    {
        HashSet<string> edited = [.. Project.Edits.EditedIds];
        json.WriteNumber("bar", bar.Number);
        Answer.Span(json, "span", bar.Span);
        json.WriteStartArray("notes");
        foreach (Note note in Notes.Where(note => note.Span.Onset >= bar.Span.Onset && note.Span.Onset < bar.Span.End))
        {
            bool taken = PerformanceTake.IsTaken(note.Id);
            json.WriteStartObject();
            json.WriteString("id", note.Id);
            json.WriteNumber("pitch", note.Pitch.MidiNumber);
            json.WriteString("name", note.Pitch.ToString());
            CorrectionPlace(json, note.Span.Onset);
            json.WriteString("length", VariationFacts.Length(note.Span.Duration, Words));
            json.WriteNumber("velocity", note.Velocity);
            json.WriteBoolean("edited", edited.Contains(note.Id));
            json.WriteBoolean("taken", taken);
            json.WriteBoolean("canEarlier", !taken && note.Span.Onset.Value >= CorrectionStep);
            json.WriteBoolean("canShorter", !taken && note.Span.Duration > CorrectionStep);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        HashSet<string> kept = [.. Printed.Select(note => note.Id)];
        json.WriteStartArray("removed");
        foreach (Note note in Piece.Notes.Where(note => edited.Contains(note.Id) && !kept.Contains(note.Id)))
        {
            json.WriteStartObject();
            json.WriteString("id", note.Id);
            json.WriteString("name", note.Pitch.ToString());
            CorrectionPlace(json, note.Span.Onset);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("count", Project.Edits.Edits.Count);
    });

    /// <summary>Where a note starts: its bar and beat, and the rest of the beat in words when it starts off the beat.</summary>
    private void CorrectionPlace(Utf8JsonWriter json, Tick onset)
    {
        MetricPosition position = Piece.Meter.ToPosition(onset);
        json.WriteNumber("onset", onset.Value);
        json.WriteNumber("atBar", position.Bar);
        json.WriteNumber("beat", position.Beat);
        if (position.BeatOffset == 0)
        {
            json.WriteNull("offset");
        }
        else
        {
            json.WriteString("offset", VariationFacts.Length(position.BeatOffset, Words));
        }
    }
}