using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Facade;

/// <summary>Piece DNA (0018 section 11) and the sections of the form it shares with the phrase marks.</summary>
internal static partial class Answer
{
    /// <summary>A section of the form, as Piece DNA and the phrase marks write it: its letter, its bars and the span a tap selects (at most 32 bars).</summary>
    public static void FormSection(Utf8JsonWriter json, DnaSection section, MeterMap? meter)
    {
        json.WriteString("id", section.Id);
        json.WriteString("label", section.Label);
        Passage(json, "passage", section.Span, section.FirstBar, section.LastBar);
        Span(json, "selectable", meter is null ? section.Span : FormAnalyzer.Selectable(section, meter));
        JsonWrite.Texts(json, "sharesWith", section.SharesWith);
    }

    /// <summary>
    /// Piece DNA (0018 section 11): every lane with its status (<c>notYet</c>, <c>partial</c>, <c>ready</c>), so the
    /// screen shows an empty state for a lane not analysed yet.
    /// </summary>
    public static void Dna(Utf8JsonWriter json, PieceDna dna, Words words, Tick end = default, MeterMap? meter = null)
    {
        json.WriteString("status", JsonWrite.Name(dna.Status));
        json.WriteString("summary", dna.Summary);
        json.WriteNumber("firstBar", dna.FirstBar);
        json.WriteNumber("lastBar", dna.LastBar);
        JsonWrite.Numbers(json, "barStarts", dna.BarStarts);
        DnaForm(json, dna, meter);
        DnaIdeas(json, dna);
        DnaKeys(json, dna, words);
        json.WriteStartObject("newMaterial");
        json.WriteString("status", JsonWrite.Name(dna.NewMaterialStatus));
        JsonWrite.Numbers(json, "bars", dna.NewMaterialBars);
        json.WriteEndObject();
        DnaHarmony(json, dna);
        DnaCircles(json, dna, words, end);
        Levels(json, "density", dna.DensityStatus, dna.Density);
        DnaRegister(json, dna);
        Levels(json, "dynamics", dna.DynamicsStatus, dna.Dynamics);
    }

    private static void DnaForm(Utf8JsonWriter json, PieceDna dna, MeterMap? meter)
    {
        json.WriteStartObject("form");
        json.WriteString("status", JsonWrite.Name(dna.FormStatus));
        json.WriteStartArray("sections");
        foreach (DnaSection section in dna.Form)
        {
            json.WriteStartObject();
            FormSection(json, section, meter);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void DnaIdeas(Utf8JsonWriter json, PieceDna dna)
    {
        json.WriteStartObject("ideas");
        json.WriteString("status", JsonWrite.Name(dna.IdeasStatus));
        json.WriteStartArray("items");
        foreach (DnaIdea idea in dna.Ideas)
        {
            json.WriteStartObject();
            json.WriteString("id", idea.Id);
            json.WriteBoolean("fromSelection", idea.FromSelection);
            json.WriteString("title", idea.Title);
            json.WriteString("summary", idea.Summary);
            Passage(json, "passage", idea.Span, idea.FirstBar, idea.LastBar);
            JsonWrite.Texts(json, "noteIds", idea.NoteIds);
            json.WriteStartArray("occurrences");
            foreach (IdeaOccurrence occurrence in idea.Occurrences)
            {
                json.WriteStartObject();
                json.WriteString("kind", JsonWrite.Name(occurrence.Kind));
                Passage(json, "passage", occurrence.Span, occurrence.FirstBar, occurrence.LastBar);
                json.WriteNumber("transposition", occurrence.Transposition);
                JsonWrite.Texts(json, "noteIds", occurrence.NoteIds);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void DnaKeys(Utf8JsonWriter json, PieceDna dna, Words words)
    {
        json.WriteStartObject("keys");
        json.WriteString("status", JsonWrite.Name(dna.KeysStatus));
        json.WriteStartArray("spans");
        foreach (DnaKey key in dna.Keys)
        {
            json.WriteStartObject();
            MusicalKey(json, "key", key.Key);
            json.WriteString("label", key.Key.NameIn(words));
            json.WriteBoolean("modeInferred", key.ModeInferred);
            json.WriteBoolean("keyInferred", key.KeyInferred);
            Doubt(json, key.Doubt);
            Passage(json, "passage", key.Span, key.FirstBar, key.LastBar);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void DnaHarmony(Utf8JsonWriter json, PieceDna dna)
    {
        json.WriteStartObject("harmony");
        json.WriteString("status", JsonWrite.Name(dna.HarmonyStatus));
        json.WriteStartArray("bars");
        foreach (DnaChord chord in dna.Harmony)
        {
            json.WriteStartObject();
            json.WriteNumber("bar", chord.Bar);
            json.WriteString("symbol", chord.Symbol);
            json.WriteBoolean("clear", chord.Clear);
            json.WriteString("degree", chord.Degree);
            json.WriteString("function", JsonWrite.Name(chord.Function));
            Doubt(json, chord.Doubt);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("cadences");
        foreach (DnaCadence cadence in dna.Cadences)
        {
            json.WriteStartObject();
            json.WriteNumber("bar", cadence.Bar);
            json.WriteNumber("at", cadence.At.Value);
            json.WriteString("kind", cadence.Kind == CloseKind.Full ? "full" : "half");
            json.WriteString("explanation", cadence.Explanation);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void DnaCircles(Utf8JsonWriter json, PieceDna dna, Words words, Tick end)
    {
        json.WriteStartArray("circles");
        foreach (DnaCircle circle in dna.Circles)
        {
            json.WriteStartObject();
            json.WriteString("id", circle.Id);
            json.WriteString("label", circle.Label);
            Passage(json, "passage", circle.Span, circle.FirstBar, circle.LastBar);
            if (circle.From is { } from)
            {
                MusicalKey(json, "from", from);
                json.WriteString("fromName", from.NameIn(words));
                json.WriteNumber("fromPosition", circle.FromPosition ?? 0);
            }

            Doubt(json, circle.Doubt);
            Circle(json, circle.Circle, circle.Span.Onset, end == default ? null : end);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void DnaRegister(Utf8JsonWriter json, PieceDna dna)
    {
        json.WriteStartObject("register");
        json.WriteString("status", JsonWrite.Name(dna.DensityStatus));
        json.WriteStartArray("bars");
        foreach (DnaRange range in dna.Register)
        {
            json.WriteStartObject();
            json.WriteNumber("bar", range.Bar);
            json.WriteNumber("low", range.Low);
            json.WriteNumber("high", range.High);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Levels(Utf8JsonWriter json, string name, DnaStatus status, IReadOnlyList<DnaBarLevel> levels)
    {
        json.WriteStartObject(name);
        json.WriteString("status", JsonWrite.Name(status));
        json.WriteStartArray("bars");
        foreach (DnaBarLevel level in levels)
        {
            json.WriteStartObject();
            json.WriteNumber("bar", level.Bar);
            json.WriteNumber("value", level.Value);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }
}