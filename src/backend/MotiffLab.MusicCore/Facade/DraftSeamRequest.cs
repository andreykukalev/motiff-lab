using System.Text.Json;

using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>
/// The calls of a draft's seams (decision record 0034, section 5; the iPad's step 7, the seam card): the seams with what
/// differs and what is offered, «② в соль мажор», «Темп как в ①» and «Оставить». Each call that changes the draft answers
/// the sources (<see cref="SourceRequest.Layers"/>) with "draft", "place" and "step"; drafts.undo takes a step back.
/// </summary>
internal static class DraftSeamRequest
{
    /// <summary>
    /// The seams of the draft "draft": "draft" and "seams" [{"before", "after" (the places' ids), "at" (the draft's tick
    /// where "after" starts), "lastBar", "firstBar" (the draft's own numbers either side), "listen" {"onset", "end"} (the
    /// bar before and the bar after, for «▶ Послушать стык» with the draft view's playback), "left" (kept as it is),
    /// "changes" (of key, meter, tempo: what differs), "sides" [before, after] each {"place", "number" (from 1), "variant"
    /// (its base), "source" (pdfNotes, fileNotes, recording, otherPiece), "key" ({"fifths", "mode" (major, minor or null),
    /// "name"}, null when its source states none), "meter" {"beats", "beatType", "label"}, "tempo"
    /// {"perMinute" (quarters), "source" (notated, inferred, player, default or assumed, as saved), "beat" (quarter or
    /// dottedQuarter, the meter's) and "beatsPerMinute" as playback's "tempo" answers them, "mark" ("= 92", "≈ 92" when
    /// inferred, "= 60" of dotted quarters in 6/8, null when assumed, as the score marks it)}}, and "offers" [{"kind"
    /// (transpose, tempo), "place", "semitones" or "quarterNotesPerMinute", "label"}] for the place after the seam}] in order.
    /// Since version 1.2 (the plan's answers 2 and 3): "changes" may name "register", "register" {"direction" (higher or
    /// lower: the mean pitch of the place after it an octave or more from the place before it), "label" («② на октаву
    /// выше»)} or null, with the offer of a "transpose" by −12 or 12 («② на октаву ниже», spelling kept); and "bridge"
    /// {"place" (after it), "candidates" (2 or 3), "label" («Связка · 3 кандидата»)} where the keys differ, for
    /// drafts.bridge, else null. A bridge place is offered nothing at the seam before it.
    /// </summary>
    public static string Seams(OpenPiece owner, Request request)
    {
        string id = request.Required("draft");
        Draft draft = owner.Project.FindDraft(id) ?? throw FacadeException.BadRequest($"There is no draft {id}.");
        return Answer.Write(json =>
        {
            json.WriteString("draft", draft.Id);
            json.WriteStartArray("seams");
            foreach (DraftSeam seam in DraftSeams.Of(draft))
            {
                Seam(json, seam, owner.Words);
            }

            json.WriteEndArray();
        });
    }

    /// <summary>«② в соль мажор»: the place "place" of the draft "draft" moved by "semitones" (1 to 24 either way), its keys with it, "now" its time.</summary>
    public static string Transpose(OpenPiece owner, Request request) =>
        DraftTakeRequest.Stepped(owner, request, (draft, place, now) => DraftSeams.Transpose(draft, place, request.RequiredInt("semitones"), now));

    /// <summary>«Темп как в ①»: the place played at "quarterNotesPerMinute" (20 to 300) from its start.</summary>
    public static string Tempo(OpenPiece owner, Request request) =>
        DraftTakeRequest.Stepped(owner, request, (draft, place, now) => DraftSeams.Tempo(draft, place, request.RequiredInt("quarterNotesPerMinute"), now));

    /// <summary>«Оставить»: the seam before the place "place" kept as it is, remembered.</summary>
    public static string Leave(OpenPiece owner, Request request) =>
        DraftTakeRequest.Stepped(owner, request, (draft, place, now) => DraftSeams.Leave(draft, place, now));

    private static void Seam(Utf8JsonWriter json, DraftSeam seam, Words words)
    {
        json.WriteStartObject();
        json.WriteString("before", seam.Before.Place);
        json.WriteString("after", seam.After.Place);
        json.WriteNumber("at", seam.At.Value);
        json.WriteNumber("lastBar", seam.LastBar);
        json.WriteNumber("firstBar", seam.FirstBar);
        json.WriteStartObject("listen");
        json.WriteNumber("onset", seam.Listen.Onset.Value);
        json.WriteNumber("end", seam.Listen.End.Value);
        json.WriteEndObject();
        json.WriteBoolean("left", seam.Left);
        (bool Changes, string Name)[] changes = [(seam.KeyChanges, "key"), (seam.MeterChanges, "meter"), (seam.TempoChanges, "tempo"), (seam.Register != 0, "register")];
        JsonWrite.Texts(json, "changes", changes.Where(static change => change.Changes).Select(static change => change.Name));
        Register(json, seam, words);
        json.WriteStartArray("sides");
        Side(json, seam.Before, words);
        Side(json, seam.After, words);
        json.WriteEndArray();
        Offers(json, seam, words);
        Bridge(json, seam, words);
        json.WriteEndObject();
    }

    /// <summary>"register": the direction the place after the seam is an octave or more from the place before it, in words; null when not.</summary>
    private static void Register(Utf8JsonWriter json, DraftSeam seam, Words words)
    {
        if (seam.Register == 0)
        {
            json.WriteNull("register");
            return;
        }

        json.WriteStartObject("register");
        json.WriteString("direction", seam.Register > 0 ? "higher" : "lower");
        json.WriteString("label", DraftLabels.Octave(seam.After.Index, seam.Register > 0, words));
        json.WriteEndObject();
    }

    /// <summary>"offers" for the place after the seam: the key before it, the octave towards it, the tempo before it.</summary>
    private static void Offers(Utf8JsonWriter json, DraftSeam seam, Words words)
    {
        void Offer(string kind, string field, int value, string label)
        {
            json.WriteStartObject();
            json.WriteString("kind", kind);
            json.WriteString("place", seam.After.Place);
            json.WriteNumber(field, value);
            json.WriteString("label", label);
            json.WriteEndObject();
        }

        json.WriteStartArray("offers");
        if (seam.Semitones is { } semitones)
        {
            Offer("transpose", "semitones", semitones, DraftLabels.Transpose(seam.After.Index, seam.Transposed!, words));
        }

        if (seam.Octave is { } octave)
        {
            Offer("transpose", "semitones", octave, DraftLabels.Octave(seam.After.Index, octave > 0, words));
        }

        if (seam.Tempo is { } tempo)
        {
            Offer("tempo", "quarterNotesPerMinute", tempo, DraftLabels.TempoAsIn(seam.Before.Index, words));
        }

        json.WriteEndArray();
    }

    /// <summary>"bridge": how many candidates drafts.bridge would put at the seam, in words; null where none is offered.</summary>
    private static void Bridge(Utf8JsonWriter json, DraftSeam seam, Words words)
    {
        int candidates = DraftBridges.Of(seam).Count;
        if (candidates == 0)
        {
            json.WriteNull("bridge");
            return;
        }

        json.WriteStartObject("bridge");
        json.WriteString("place", seam.After.Place);
        json.WriteNumber("candidates", candidates);
        json.WriteString("label", DraftLabels.Bridge(candidates, words));
        json.WriteEndObject();
    }

    private static void Side(Utf8JsonWriter json, SeamSide side, Words words)
    {
        json.WriteStartObject();
        json.WriteString("place", side.Place);
        json.WriteNumber("number", side.Index + 1);
        json.WriteString("variant", side.Base.Id);
        json.WriteString("source", JsonWrite.Name(DraftLabels.Kind(side.Base)));
        if (side.Key is { } key)
        {
            json.WriteStartObject("key");
            json.WriteNumber("fifths", key.Fifths);
            if (key.Mode is { } mode)
            {
                json.WriteString("mode", mode == KeyMode.Minor ? "minor" : "major");
            }
            else
            {
                json.WriteNull("mode");
            }

            json.WriteString("name", DraftLabels.Key(key, words));
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("key");
        }

        json.WriteStartObject("meter");
        json.WriteNumber("beats", side.Meter.Numerator);
        json.WriteNumber("beatType", side.Meter.Denominator);
        json.WriteString("label", side.Meter.ToString());
        json.WriteEndObject();
        TempoBeat beat = TempoBeats.Of(side.Meter);
        json.WriteStartObject("tempo");
        json.WriteNumber("perMinute", side.PerMinute);
        json.WriteString("source", side.Tempo.Source.Name());
        json.WriteString("beat", beat.Name());
        json.WriteNumber("beatsPerMinute", beat.PerMinute(side.Tempo.MicrosecondsPerQuarter));
        if (TempoMarks.AtStart(side.Tempo, beat) is { } mark)
        {
            json.WriteString("mark", mark.Text);
        }
        else
        {
            json.WriteNull("mark");
        }

        json.WriteEndObject();
        json.WriteEndObject();
    }
}