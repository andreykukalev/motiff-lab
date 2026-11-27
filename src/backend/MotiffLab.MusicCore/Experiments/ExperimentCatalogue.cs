using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Experiments;

/// <summary>One step of an experiment's recipe: a catalogue operation with its parameters, scope included.</summary>
internal sealed record ExperimentStep(string OperationId, OperationParameters Parameters)
{
    /// <summary>The operation this version offers for <see cref="OperationId"/>.</summary>
    public IVariationOperation Operation => VariationOperations.Find(OperationId)
        ?? throw new InvalidOperationException($"This version has no operation {OperationId}.");
}

/// <summary>
/// The experiments' ids, which analytics may send, and how a recipe of catalogue operations runs over a passage: the
/// way the Try chips (<see cref="TryChips"/>) run.
/// </summary>
internal static class ExperimentCatalogue
{
    /// <summary>The most alternatives an experiment offers.</summary>
    public const int MaxAlternatives = 4;

    public const string MelodySpaceId = "melody.space";

    public const string GrooveId = "rhythm.groove";

    public const string AccompanimentId = "layers.accompaniment";

    public const string AnswerId = "motif.answer";

    /// <summary>Every experiment's id, in the catalogue's order; analytics may send only these.</summary>
    public static IReadOnlyList<string> Ids { get; } = [MelodySpaceId, GrooveId, AccompanimentId, AnswerId];

    /// <summary>The chain of the steps over the passage, or null with the reason when a step cannot run.</summary>
    internal static VariationChain? TryStart(
        TickSpan span,
        SelectionFilter filter,
        string presetId,
        IReadOnlyList<ExperimentStep> steps,
        IReadOnlyList<Note> notes,
        MeterMap meter,
        MusicalKey? key,
        Words words,
        out string? reason)
    {
        // The passage came from a selection the app made, so it may be a structure selected whole (0013).
        MotifSelection selection = MotifSelector.Select(notes, meter, span, filter, MotifSelection.MaxStructureBars);
        reason = selection.NoteIds.Count == 0 ? words.T("The passage has no notes.", "В отрывке нет нот.") : null;
        if (reason is not null)
        {
            return null;
        }

        IReadOnlySet<KeepProperty> keep = KeepTable.FindPreset(presetId)!.Keep;
        VariationChain chain = VariationChain.Start(VariationFacts.MotifNotes(notes, selection), VariationContext.For(selection, keep, key: key, meter: meter, words: words));
        foreach (ExperimentStep step in steps)
        {
            reason = chain.UnavailableReason(step.Operation, step.Parameters);
            if (reason is not null)
            {
                return null;
            }

            chain = chain.Propose(step.Operation, step.Parameters).Accept();
        }

        return chain;
    }
}