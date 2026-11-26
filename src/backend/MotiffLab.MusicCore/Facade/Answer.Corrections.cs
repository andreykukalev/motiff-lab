using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Facade.Sessions;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>The corrections of a bar and the layer they are laid with (S1-I7, contract 4; the layer of version 0.5, within contract 12).</summary>
internal static partial class Answer
{
    /// <summary>
    /// A bar's corrections (<see cref="BarCorrections"/>): its notes as they sound, each with where it is, how long, how loud
    /// and whether the player corrected it; the imported notes taken out anywhere; how many corrections the log holds; the
    /// draft; and the layer. <paramref name="added"/> is the id of the note just added, answered as "added".
    /// </summary>
    public static void Corrections(Utf8JsonWriter json, BarCorrections corrections, string? added, MeterMap meter, Words words)
    {
        if (added is not null)
        {
            json.WriteString("added", added);
        }

        json.WriteNumber("bar", corrections.Bar.Number);
        Span(json, "span", corrections.Bar.Span);
        json.WriteStartArray("notes");
        foreach (CorrectionNote correction in corrections.Notes)
        {
            Note note = correction.Note;
            json.WriteStartObject();
            json.WriteString("id", note.Id);
            json.WriteNumber("pitch", note.Pitch.MidiNumber);
            json.WriteString("name", note.Pitch.ToString());
            CorrectionPlace(json, note.Span.Onset, meter, words);
            json.WriteString("length", VariationFacts.Length(note.Span.Duration, words));
            json.WriteNumber("velocity", note.Velocity);
            json.WriteBoolean("edited", correction.Edited);
            json.WriteBoolean("taken", correction.Taken);
            json.WriteBoolean("canEarlier", correction.CanEarlier);
            json.WriteBoolean("canShorter", correction.CanShorter);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("removed");
        foreach (Note note in corrections.Removed)
        {
            json.WriteStartObject();
            json.WriteString("id", note.Id);
            json.WriteString("name", note.Pitch.ToString());
            CorrectionPlace(json, note.Span.Onset, meter, words);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("count", corrections.Count);
        json.WriteBoolean("editing", corrections.Editing);
        json.WriteBoolean("canUndo", corrections.CanUndo);
        json.WriteBoolean("changed", corrections.Changed);
        Layer(json, corrections.Layer, words);
    }

    /// <summary>The layer and what became of each correction: "layer", "applied" (how many), "moved" and "unfit" with their notes and bars; nothing when strict (null).</summary>
    public static void Layer(Utf8JsonWriter json, LaidCorrections? laid, Words words)
    {
        if (laid is null)
        {
            return;
        }

        json.WriteString("layer", laid.Off ? "off" : "on");
        json.WriteBoolean("editsOff", laid.EditsOff);
        json.WriteNumber("applied", laid.Applied);
        json.WriteStartArray("moved");
        foreach (MisfitCorrection moved in laid.Moved)
        {
            json.WriteStartObject();
            json.WriteString("noteId", moved.NoteId);
            json.WriteString("to", moved.To);
            json.WriteNumber("bar", moved.Bar);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("unfit");
        foreach (MisfitCorrection unfit in laid.Unfit)
        {
            json.WriteStartObject();
            json.WriteString("noteId", unfit.NoteId);
            json.WriteNumber("bar", unfit.Bar);
            json.WriteString("reason", unfit.Reason switch
            {
                EditMisfit.Missing => "missing",
                EditMisfit.IdTaken => "idTaken",
                _ => "changed",
            });
            json.WriteString("text", MisfitText(unfit.Reason, words));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        JsonWrite.Texts(json, "hidden", laid.Hidden);
    }

    /// <summary>Where a note starts: its bar and beat, and the rest of the beat in words when it starts off the beat.</summary>
    private static void CorrectionPlace(Utf8JsonWriter json, Tick onset, MeterMap meter, Words words)
    {
        MetricPosition position = meter.ToPosition(onset);
        json.WriteNumber("onset", onset.Value);
        json.WriteNumber("atBar", position.Bar);
        json.WriteNumber("beat", position.Beat);
        if (position.BeatOffset == 0)
        {
            json.WriteNull("offset");
        }
        else
        {
            json.WriteString("offset", VariationFacts.Length(position.BeatOffset, words));
        }
    }

    private static string MisfitText(EditMisfit reason, Words words) => reason switch
    {
        EditMisfit.Missing => words.T("The note is not there", "Ноты нет"),
        EditMisfit.IdTaken => words.T("A note with this id is already there", "Нота с таким номером уже есть"),
        _ => words.T("The note is different now", "Нота уже другая"),
    };
}