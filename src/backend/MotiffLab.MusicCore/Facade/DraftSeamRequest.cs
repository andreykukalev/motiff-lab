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
    /// "name"}, null when its source states none), "meter" {"beats", "beatType", "label", "inferred"}, "tempo"
    /// {"perMinute", "source" (notated, inferred, player, default), "mark" ("= 92", "≈ 92")}}, and "offers" [{"kind"
    /// (transpose, tempo), "place", "semitones" or "quarterNotesPerMinute", "label"}] for the place after the seam}] in order.
    /// </summary>
    public static string Seams(OpenPiece owner, Request request)
    {
        string id = request.Required("draft");
        Draft draft = owner.Project.FindDraft(id) ?? throw FacadeException.BadRequest($"There is no draft {id}.");
        IReadOnlyList<DraftPlaceSpan> spans = DraftMusic.Of(draft).Places;
        return Answer.Write(json =>
        {
            json.WriteString("draft", draft.Id);
            json.WriteStartArray("seams");
            foreach (DraftSeam seam in DraftSeams.Of(draft))
            {
                Seam(json, seam, spans, owner.Words);
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

    private static void Seam(Utf8JsonWriter json, DraftSeam seam, IReadOnlyList<DraftPlaceSpan> spans, Words words)
    {
        json.WriteStartObject();
        json.WriteString("before", seam.Before.Place);
        json.WriteString("after", seam.After.Place);
        json.WriteNumber("at", seam.At.Value);
        json.WriteNumber("lastBar", spans[seam.Before.Index].LastBar);
        json.WriteNumber("firstBar", spans[seam.After.Index].FirstBar);
        json.WriteStartObject("listen");
        json.WriteNumber("onset", seam.Listen.Onset.Value);
        json.WriteNumber("end", seam.Listen.End.Value);
        json.WriteEndObject();
        json.WriteBoolean("left", seam.Left);
        json.WriteStartArray("changes");
        foreach ((bool changes, string name) in new[] { (seam.KeyChanges, "key"), (seam.MeterChanges, "meter"), (seam.TempoChanges, "tempo") })
        {
            if (changes)
            {
                json.WriteStringValue(name);
            }
        }

        json.WriteEndArray();
        json.WriteStartArray("sides");
        Side(json, seam.Before, words);
        Side(json, seam.After, words);
        json.WriteEndArray();
        json.WriteStartArray("offers");
        if (seam.Semitones is { } semitones)
        {
            json.WriteStartObject();
            json.WriteString("kind", "transpose");
            json.WriteString("place", seam.After.Place);
            json.WriteNumber("semitones", semitones);
            json.WriteString("label", DraftLabels.Transpose(seam.After.Index, seam.Transposed!, words));
            json.WriteEndObject();
        }

        if (seam.Tempo is { } tempo)
        {
            json.WriteStartObject();
            json.WriteString("kind", "tempo");
            json.WriteString("place", seam.After.Place);
            json.WriteNumber("quarterNotesPerMinute", tempo);
            json.WriteString("label", DraftLabels.TempoAsIn(seam.Before.Index, words));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Side(Utf8JsonWriter json, SeamSide side, Words words)
    {
        json.WriteStartObject();
        json.WriteString("place", side.Place);
        json.WriteNumber("number", side.Index + 1);
        json.WriteString("variant", side.Base.Id);
        json.WriteString("source", Answer.Camel(DraftLabels.Kind(side.Base)));
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
        json.WriteBoolean("inferred", side.Heard);
        json.WriteEndObject();
        json.WriteStartObject("tempo");
        json.WriteNumber("perMinute", side.PerMinute);
        json.WriteString("source", side.Tempo.Name());
        json.WriteString("mark", new TempoMark(Tick.Zero, side.PerMinute, side.Tempo == TempoSource.Inferred).Text);
        json.WriteEndObject();
        json.WriteEndObject();
    }
}