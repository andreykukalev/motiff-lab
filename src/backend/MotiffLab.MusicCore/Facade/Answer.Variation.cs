using System.Text.Json;

using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Facade;

/// <summary>A variation chain with its steps and result (0018, section 8), and the catalogue of operations and Keep.</summary>
internal static partial class Answer
{
    public static void Chain(Utf8JsonWriter json, VariationChain chain, Words words)
    {
        Keep(json, "keep", chain.Context.Keep);
        json.WriteNumber("seed", chain.Context.Seed);
        json.WriteStartArray("steps");
        foreach (VariationStep step in chain.Steps)
        {
            Step(json, step);
        }

        json.WriteEndArray();
        if (chain.Pending is { } pending)
        {
            json.WriteStartObject("pending");
            ProjectFile.WriteStep(json, new SavedStep(pending.Operation.Id, pending.Parameters));
            json.WriteString("explanation", pending.Result.Explanation);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("pending");
        }

        json.WriteNumber("undone", chain.Undone.Count);
        json.WriteBoolean("canUndo", chain.CanUndo);
        json.WriteBoolean("canRedo", chain.CanRedo);
        json.WriteString("acceptDisabledReason", chain.AcceptDisabledReason);
        json.WriteString("editDisabledReason", chain.EditDisabledReason);
        Notes(json, "original", chain.Original);
        Notes(json, "current", chain.Current);
        Notes(json, "heard", chain.Heard);
        Result(json, "result", chain.Pending?.Result ?? chain.Result);

        json.WriteStartArray("operations");
        foreach (IVariationOperation operation in VariationOperations.All.Append(HandEdit.Instance))
        {
            json.WriteStartObject();
            json.WriteString("id", operation.Id);
            json.WriteString("name", operation.NameIn(words));
            json.WriteString("disabledReason", operation.DisabledReason(chain.Context.Keep, words));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void Step(Utf8JsonWriter json, VariationStep step)
    {
        json.WriteStartObject();
        ProjectFile.WriteStep(json, new SavedStep(step.Operation.Id, step.Parameters));
        json.WriteString("explanation", step.Result.Explanation);
        json.WriteEndObject();
    }

    private static void Result(Utf8JsonWriter json, string name, Variation? variation)
    {
        if (variation is null)
        {
            json.WriteNull(name);
            return;
        }

        json.WriteStartObject(name);
        json.WriteString("operation", variation.Operation);
        Notes(json, "notes", variation.Notes);
        json.WriteStartArray("changes");
        foreach (NoteChange change in variation.Changes)
        {
            json.WriteStartObject();
            json.WritePropertyName("before");
            Note(json, change.Before);
            json.WritePropertyName("after");
            Note(json, change.After);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        Notes(json, "added", variation.Added);
        Notes(json, "removed", variation.Removed);
        Checks(json, "checks", variation.Checks);
        Checks(json, "properties", variation.Properties);
        JsonWrite.Texts(json, "flags", variation.Flags);
        json.WriteString("explanation", variation.Explanation);
        json.WriteEndObject();
    }

    private static void Checks(Utf8JsonWriter json, string name, IEnumerable<KeepCheck> checks)
    {
        json.WriteStartArray(name);
        foreach (KeepCheck check in checks)
        {
            json.WriteStartObject();
            json.WriteString("property", JsonWrite.Name(check.Property));
            json.WriteString("status", JsonWrite.Name(check.Status));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    public static void Catalogue(Utf8JsonWriter json, Words words)
    {
        json.WriteStartObject("keep");
        json.WriteStartArray("properties");
        foreach (KeepProperty property in Enum.GetValues<KeepProperty>())
        {
            json.WriteStartObject();
            json.WriteString("id", JsonWrite.Name(property));
            json.WriteString("label", words.IsRussian ? KeepTable.RussianName(property) : property.ToString());
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("presets");
        foreach (KeepPreset preset in KeepTable.Presets)
        {
            json.WriteStartObject();
            json.WriteString("id", preset.Id);
            json.WriteString("label", preset.Label);
            Keep(json, "keep", preset.Keep);
            JsonWrite.Texts(json, "operations", preset.Operations);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();

        json.WriteStartArray("operations");
        foreach (IVariationOperation operation in VariationOperations.All.Append(HandEdit.Instance))
        {
            KeepRow? row = KeepTable.Find(operation.Id);
            json.WriteStartObject();
            json.WriteString("id", operation.Id);
            json.WriteString("name", operation.NameIn(words));
            if (row is not null)
            {
                json.WriteString("whatItChanges", words.IsRussian ? row.RussianWhatItChanges : row.WhatItChanges);
                Keep(json, "compatible", row.Compatible);
                json.WriteStartArray("footnotes");
                foreach ((KeepProperty property, KeepFootnote footnote) in row.Footnotes.OrderBy(pair => pair.Key))
                {
                    json.WriteStartObject();
                    json.WriteString("property", JsonWrite.Name(property));
                    json.WriteNumber("number", footnote.Number);
                    json.WriteString("text", footnote.Text);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            JsonWrite.Texts(json, "scopes", Enum.GetValues<OperationScope>().Where(operation.Supports).Select(JsonWrite.Name));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}