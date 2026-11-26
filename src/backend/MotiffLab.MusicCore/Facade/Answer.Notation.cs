using System.Text.Json;

using MotiffLab.MusicCore.Notation;
using MotiffLab.MusicCore.Performance;

namespace MotiffLab.MusicCore.Facade;

/// <summary>The notes as written (0018): the engraving's MEI, its staves, the ossias and the counts of what was written as heard.</summary>
internal static partial class Answer
{
    public static void Notation(Utf8JsonWriter json, GeneratedNotation notation, IReadOnlyList<PerformanceLayerNotes?>? versions = null)
    {
        json.WriteString("mei", notation.Mei);
        Staves(json, notation.Staves);
        if (notation.Excerpt is { } excerpt)
        {
            json.WriteStartObject("excerpt");
            json.WriteNumber("firstBar", excerpt.FirstBar);
            json.WriteNumber("lastBar", excerpt.LastBar);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("excerpt");
        }

        json.WriteStartArray("ossias");
        int index = 0;
        foreach (GeneratedOssia ossia in notation.Ossias)
        {
            json.WriteStartObject();
            json.WriteString("label", ossia.Label);
            Staves(json, ossia.Staves);
            json.WriteNumber("noteCount", ossia.NoteCount);
            json.WriteString("idPrefix", ossia.IdPrefix);
            // Another version's ossia: the engraved ids of the notes it adds (plum) and of its likely artefacts (grey).
            if (versions?.ElementAtOrDefault(index) is { } version)
            {
                JsonWrite.Texts(json, "added", version.Added.Select(id => ossia.IdPrefix + id));
                JsonWrite.Texts(json, "artefacts", version.Artefacts.Select(id => ossia.IdPrefix + id));
            }

            json.WriteEndObject();
            index++;
        }

        json.WriteEndArray();
        json.WriteNumber("noteCount", notation.NoteCount);
        json.WriteNumber("approximatedNoteCount", notation.ApproximatedNoteCount);
        json.WriteNumber("shortenedNoteCount", notation.ShortenedNoteCount);
        json.WriteNumber("lengthenedNoteCount", notation.LengthenedNoteCount);
        json.WriteNumber("cutNoteCount", notation.CutNoteCount);
        json.WriteNumber("graceNoteCount", notation.GraceNoteCount);
        json.WriteNumber("tripletCount", notation.TripletCount);
        json.WriteNumber("slurCount", notation.SlurCount);
        json.WriteNumber("pedalCount", notation.PedalCount);
        json.WriteBoolean("writtenAsHeard", notation.WrittenAsHeard);
        // «Как напечатано» (v1.4 c-4): the bars the app wrote where the score's record does not fit; null without a record.
        if (notation.AppWrittenBarCount is { } appWritten)
        {
            json.WriteNumber("appWrittenBarCount", appWritten);
        }
        else
        {
            json.WriteNull("appWrittenBarCount");
        }
    }

    private static void Staves(Utf8JsonWriter json, IReadOnlyList<NotationStaff> staves)
    {
        json.WriteStartArray("staves");
        foreach (NotationStaff staff in staves)
        {
            json.WriteStartObject();
            json.WriteNumber("number", staff.Number);
            json.WriteNumber("track", staff.Track);
            json.WriteString("clef", JsonWrite.Name(staff.Clef));
            if (staff.Hand is { } hand)
            {
                json.WriteString("hand", JsonWrite.Name(hand));
            }

            json.WriteBoolean("splitInferred", staff.IsSplitInferred);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}