using System.Text.Json;

using MotiffLab.MusicCore.Analysis.Phrasing;

namespace MotiffLab.MusicCore.Facade;

/// <summary>What the facade answers of the phrases: the three levels, their edits and their units (0018, section 3; contract 3).</summary>
internal static partial class Answer
{
    /// <summary>The three levels (0018, section 3; contract 3): the level shown, every level with its count, the edits per level and the units of the level shown.</summary>
    public static void Phrases(Utf8JsonWriter json, PhraseLevels levels, PhraseLevel shown, Words words)
    {
        json.WriteString("level", PhraseLevels.Name(shown));
        json.WriteStartArray("levels");
        foreach (PhraseLevel level in PhraseLevels.All)
        {
            json.WriteStartObject();
            json.WriteString("id", PhraseLevels.Name(level));
            json.WriteBoolean("available", true);
            json.WriteNumber("count", levels.Units(level).Count);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteString("evidence", Name(levels.Phrases.Evidence));
        json.WriteStartObject("edits");
        foreach (PhraseLevel level in PhraseLevels.All)
        {
            json.WriteStartObject(PhraseLevels.Name(level));
            Ticks(json, "added", levels.Edits[level].Added);
            Ticks(json, "removed", levels.Edits[level].Removed);
            json.WriteEndObject();
        }

        json.WriteEndObject();

        json.WriteStartArray("units");
        foreach (PhraseUnit unit in levels.Units(shown))
        {
            Unit(json, levels, unit, words);
        }

        json.WriteEndArray();
    }

    public static void Unit(Utf8JsonWriter json, PhraseLevels levels, PhraseUnit unit, Words words)
    {
        json.WriteStartObject();
        json.WriteNumber("number", unit.Number);
        json.WriteString("level", PhraseLevels.Name(unit.Level));
        json.WriteString("name", PhraseLevels.Describe(unit.Level, words));
        json.WriteNumber("firstBar", unit.FirstBar);
        json.WriteNumber("lastBar", unit.LastBar);
        json.WriteNumber("onset", unit.Span.Onset.Value);
        json.WriteNumber("end", unit.Span.End.Value);
        Span(json, "selectable", levels.Selectable(unit));
        json.WriteBoolean("long", unit.IsLong);
        if (levels.Parent(unit) is { } parent)
        {
            json.WriteNumber("parent", parent.Number);
        }
        else
        {
            json.WriteNull("parent");
        }

        json.WriteStartObject("start");
        json.WriteNumber("at", unit.Span.Onset.Value);
        Texts(json, "evidence", unit.Evidence);
        Attribution(json, unit.Start);
        json.WriteString("description", unit.Description);
        json.WriteEndObject();
        json.WriteEndObject();
    }
}