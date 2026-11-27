using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Experiments;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Assistant.Variants;

/// <summary>
/// A variant made again over the piece's notes: the chain of its steps, whether the notes under it changed since it was
/// made, or why it cannot be made.
/// </summary>
/// <param name="Chain">The steps over the notes under it, all accepted; null when a step can no longer run.</param>
/// <param name="BaseChanged">The notes under it are not the ones it was made on: it is heard over the notes as they are now, and offers to recompute.</param>
/// <param name="Refusal">Why it cannot be made, in the player's language; null when it can.</param>
internal sealed record VariantReplay(PhraseVariant Variant, VariationChain? Chain, bool BaseChanged, string? Refusal)
{
    /// <summary>The original against the variant's notes, with the Keep checks and the explanation; null when it cannot be made or changes nothing.</summary>
    public Variation? Result => Chain?.Result;
}

/// <summary>
/// Making, replaying and recomputing variants of a phrase (the AI companion study, sections 2.4 and 4.6), with no AI:
/// every note comes from the variation operations. Pure: the same notes, variant and words give the same chain.
/// </summary>
internal static class PhraseVariants
{
    /// <summary>
    /// A variant from the phrase's notes by one engine operation with fixed parameters (a «Что если» chip): its Keep
    /// preset, the key the phrase reads in when it is sure.
    /// </summary>
    public static VariantReplay FromChip(TryChip chip, string id, string name, MotifSelection selection, IReadOnlyList<Note> notes, MeterMap meter, MusicalKey? key, DateTimeOffset at, Words words)
    {
        ArgumentNullException.ThrowIfNull(chip);
        IReadOnlySet<KeepProperty> keep = KeepTable.FindPreset(chip.KeepPresetId)?.Keep ?? new HashSet<KeepProperty>();
        return Start(id, name, selection, notes, meter, key, keep, [StepOf(chip)], VariantProvenance.FromChip(chip.Id, at), words);
    }

    /// <summary>A variant from the variant <paramref name="parent"/> by one more chip: the parent's steps, Keep and key, then the chip's step.</summary>
    public static VariantReplay FromChip(TryChip chip, PhraseVariant parent, string id, string name, IReadOnlyList<Note> notes, MeterMap meter, DateTimeOffset at, Words words)
    {
        ArgumentNullException.ThrowIfNull(chip);
        return Derive(parent, id, name, [StepOf(chip)], VariantProvenance.FromChip(chip.Id, at), notes, meter, words);
    }

    /// <summary>A variant from the phrase's notes by <paramref name="steps"/>, the notes under it as they are now.</summary>
    public static VariantReplay Start(string id, string name, MotifSelection selection, IReadOnlyList<Note> notes, MeterMap meter, MusicalKey? key, IReadOnlySet<KeepProperty> keep, IReadOnlyList<SavedStep> steps, VariantProvenance provenance, Words words)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(keep);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(provenance);
        var variant = new PhraseVariant(id, name.Trim(), SavedPassage.Of(selection), BaseOf(notes, selection.NoteIds), [.. steps], [.. keep.Distinct().Order()], key, provenance);
        return Replay(variant, notes, meter, words);
    }

    /// <summary>A variant from another: all of the parent's steps and then <paramref name="steps"/>, with the parent's passage, Keep and key.</summary>
    public static VariantReplay Derive(PhraseVariant parent, string id, string name, IReadOnlyList<SavedStep> steps, VariantProvenance provenance, IReadOnlyList<Note> notes, MeterMap meter, Words words)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(steps);
        PhraseVariant child = parent with { Id = id, Name = name.Trim(), Steps = [.. parent.Steps, .. steps], Provenance = provenance, ParentId = parent.Id };
        return Replay(child, notes, meter, words);
    }

    /// <summary>
    /// The variant made again over <paramref name="notes"/>: over the notes it was made on while they are there; when they
    /// changed, over the notes of its passage as they are now, marked <see cref="VariantReplay.BaseChanged"/>.
    /// </summary>
    public static VariantReplay Replay(PhraseVariant variant, IReadOnlyList<Note> notes, MeterMap meter, Words words)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        if (variant.BarChange is < -PhraseVariant.MaxBarChange or > PhraseVariant.MaxBarChange)
        {
            return new VariantReplay(variant, null, false, words.T(
                $"A variant adds or takes away at most {PhraseVariant.MaxBarChange} bars.",
                $"Вариант добавляет или убирает не больше {PhraseVariant.MaxBarChange} тактов."));
        }

        if (Under(variant, notes, meter) is not { } selection)
        {
            return new VariantReplay(variant, null, true, words.T("The phrase under this variant has no notes now.", "Во фразе под этим вариантом сейчас нет нот."));
        }

        bool changed = !BaseOf(notes, selection.NoteIds).SequenceEqual(variant.Base);
        try
        {
            VariationChain chain = VariationChain.Start(
                VariationFacts.MotifNotes(notes, selection),
                VariationContext.For(selection, variant.Keep.ToHashSet(), variant.Provenance.Seed, variant.Key, meter, words));
            foreach (SavedStep step in variant.Steps)
            {
                IVariationOperation? operation = VariationOperations.Find(step.Operation);
                string? reason = operation is null
                    ? words.T($"This version has no operation {step.Operation}.", $"В этой версии нет операции {step.Operation}.")
                    : chain.UnavailableReason(operation, step.Parameters);
                if (reason is not null)
                {
                    return new VariantReplay(variant, null, changed, reason);
                }

                chain = chain.Propose(operation!, step.Parameters).Accept();
            }

            return new VariantReplay(variant, chain, changed, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return new VariantReplay(variant, null, changed, exception.Message);
        }
    }

    /// <summary>«Пересчитать»: the variant over the notes under its passage as they are now, so it no longer says they changed.</summary>
    public static PhraseVariant Rebase(PhraseVariant variant, IReadOnlyList<Note> notes, MeterMap meter)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(meter);
        MotifSelection selection = Select(notes, meter, variant.Phrase) ?? throw new InvalidOperationException("The phrase under this variant has no notes now.");
        return variant with { Phrase = SavedPassage.Of(selection), Base = BaseOf(notes, selection.NoteIds) };
    }

    /// <summary>
    /// «Вариант 3»: one more than the highest number among <paramref name="names"/> that read «Вариант n» or "Variant n",
    /// in the player's language.
    /// </summary>
    public static string NextName(IEnumerable<string> names, Words words)
    {
        ArgumentNullException.ThrowIfNull(names);
        int highest = 0;
        foreach (string name in names)
        {
            string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0] is "Вариант" or "Variant" && int.TryParse(parts[1], out int number))
            {
                highest = Math.Max(highest, number);
            }
        }

        return words.T($"Variant {highest + 1}", $"Вариант {highest + 1}");
    }

    /// <summary>A chip's step: its operation and parameters.</summary>
    public static SavedStep StepOf(TryChip chip)
    {
        ArgumentNullException.ThrowIfNull(chip);
        return chip.Parameters is { } parameters
            ? new SavedStep(chip.OperationId, parameters)
            : throw new ArgumentException($"The chip {chip.Id} has no operation in this version.", nameof(chip));
    }

    /// <summary>
    /// A draft of note edits (the notes.edit tool, made on the phrase's notes) as hand edits a variant replays: removed,
    /// changed, then added, as the draft lists them.
    /// </summary>
    public static IReadOnlyList<SavedStep> StepsOf(AssistantDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return [.. draft.Edits.Select(static edit => new SavedStep(HandEdit.OperationId, edit.Kind switch
        {
            DraftEditKind.Remove => new HandEditParameters(HandEditKind.Remove, edit.NoteId, null),
            DraftEditKind.Change => new HandEditParameters(HandEditKind.Change, edit.NoteId, edit.After!.Value),
            _ => new HandEditParameters(HandEditKind.Add, edit.NoteId, edit.After!.Value),
        }))];
    }

    /// <summary>The notes under the variant: the ones it was made on while they are all there, else its passage's notes now.</summary>
    private static MotifSelection? Under(PhraseVariant variant, IReadOnlyList<Note> notes, MeterMap meter)
    {
        MotifSelection? selection = Select(notes, meter, variant.Phrase);
        if (selection is not null && selection.NoteIds.SequenceEqual(variant.BaseIds, StringComparer.Ordinal))
        {
            return selection;
        }

        var ids = notes.Select(static note => note.Id).ToHashSet(StringComparer.Ordinal);
        if (variant.Base.Count > 0 && variant.Base.All(note => ids.Contains(note.Id)))
        {
            try
            {
                MotifSelection picked = MotifSelector.SelectNotes(notes, meter, variant.BaseIds);
                if (picked.Span == variant.Phrase.Span)
                {
                    return picked;
                }
            }
            catch (ArgumentException)
            {
                // Picked one by one they no longer make the passage: the passage's notes now stand under it.
            }
        }

        return selection;
    }

    private static MotifSelection? Select(IReadOnlyList<Note> notes, MeterMap meter, SavedPassage passage)
    {
        try
        {
            MotifSelection selection = MotifSelector.Select(notes, meter, passage.Span, passage.Filter, MotifSelection.MaxStructureBars);
            return selection.NoteIds.Count == 0 ? null : selection;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static BaseNote[] BaseOf(IReadOnlyList<Note> notes, IReadOnlyList<string> ids)
    {
        Dictionary<string, Note> byId = notes.ToDictionary(static note => note.Id, StringComparer.Ordinal);
        return [.. ids.Select(id => new BaseNote(id, byId.TryGetValue(id, out Note? note) ? NoteValues.Of(note) : default))];
    }
}