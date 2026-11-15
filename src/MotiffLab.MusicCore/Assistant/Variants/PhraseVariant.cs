using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Assistant.Variants;

/// <summary>Who asked for a variant's steps.</summary>
public enum VariantOrigin
{
    /// <summary>The player picked an engine operation (a «Что если» chip), with no AI.</summary>
    Variation,

    /// <summary>The assistant called the engine's tools on the player's request.</summary>
    Assistant,
}

/// <summary>
/// Where a variant came from, in the shape the sources model (v16) gives derived notes: an operation ("variation" or
/// "assistant"), when, and its parameters (the chip, or the player's request and the model). The engine made every note
/// either way; the model only chose the tool calls.
/// </summary>
public sealed record VariantProvenance(VariantOrigin Origin, DateTimeOffset At)
{
    /// <summary>The Try chip the player picked (<see cref="Experiments.TryChips"/>), or null.</summary>
    public string? ChipId { get; init; }

    /// <summary>The player's words to the assistant, or null.</summary>
    public string? Request { get; init; }

    /// <summary>The model that chose the tool calls, or null.</summary>
    public string? Model { get; init; }

    /// <summary>"variation" or "assistant", as v16 writes a derivation's op.</summary>
    public string Op => Origin == VariantOrigin.Assistant ? "assistant" : "variation";

    /// <summary>The parameters as v16 writes them: chip, request and model, those that are set, in that order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Params =>
    [
        .. ChipId is { } chip ? [new KeyValuePair<string, string>("chip", chip)] : Array.Empty<KeyValuePair<string, string>>(),
        .. Request is { } request ? [new KeyValuePair<string, string>("request", request)] : Array.Empty<KeyValuePair<string, string>>(),
        .. Model is { } model ? [new KeyValuePair<string, string>("model", model)] : Array.Empty<KeyValuePair<string, string>>(),
    ];

    public static VariantProvenance FromChip(string chipId, DateTimeOffset at) => new(VariantOrigin.Variation, at) { ChipId = chipId };

    public static VariantProvenance FromAssistant(string request, string model, DateTimeOffset at) => new(VariantOrigin.Assistant, at) { Request = request, Model = model };
}

/// <summary>One note under a variant as it was when the variant was made, to tell when the notes under it changed.</summary>
public sealed record BaseNote(string Id, NoteValues Values);

/// <summary>
/// A variant of a phrase (the AI companion study, sections 2.4 and 4.6): replayable steps (the variation operations and
/// hand edits, as <see cref="SavedStep"/>s) over the notes of a phrase, never the notes themselves, so the engine makes it
/// again and the original is never changed. A variant made from another carries the whole chain from the phrase's notes
/// and names its parent. Its notes are what <see cref="PhraseVariants.Replay"/> gives; taking it into the working notes is
/// a step of the assembly journal (<see cref="VariantPromotion"/>). It is a <see cref="KeptVariation"/> with lineage,
/// provenance and a bar-count change, so a kept variant is saved as an idea today and with its provenance by v16.
/// </summary>
/// <param name="Id">The idea's id it is or will be kept as, for example "o_0003".</param>
/// <param name="Name">«Вариант 2», as the player reads it.</param>
/// <param name="Phrase">The passage its steps run on.</param>
/// <param name="Base">The notes under it when it was made (or last recomputed), in selection order.</param>
/// <param name="Steps">Every step from the phrase's notes, the parent's first.</param>
/// <param name="Keep">The properties the steps run with, in Keep table order; a child keeps its parent's.</param>
/// <param name="Key">The key the steps count scale steps in, or null; the reading they ran with, not a fact.</param>
public sealed record PhraseVariant(
    string Id,
    string Name,
    SavedPassage Phrase,
    IReadOnlyList<BaseNote> Base,
    IReadOnlyList<SavedStep> Steps,
    IReadOnlyList<KeepProperty> Keep,
    MusicalKey? Key,
    VariantProvenance Provenance)
{
    /// <summary>The most bars a variant adds or takes away at its end.</summary>
    public const int MaxBarChange = 8;

    /// <summary>The variant it was made from, or null for one made from the phrase's notes.</summary>
    public string? ParentId { get; init; }

    /// <summary>
    /// Whole bars added (positive) or taken away (negative) at the variant's end, besides bars added for notes the steps put
    /// after the phrase. Taking it into the working notes splices the bars (<see cref="Assembly.AssemblyBars.Splice"/>).
    /// </summary>
    public int BarChange { get; init; }

    /// <summary>The ids of the notes under it.</summary>
    public IReadOnlyList<string> BaseIds => [.. Base.Select(static note => note.Id)];

    /// <summary>The variant as an idea's variation today: passage, notes, steps, Keep and key; lineage and provenance wait for v16.</summary>
    public KeptVariation ToKept() => new(Phrase, BaseIds, Steps, null, Keep, Key);

    /// <summary>
    /// A variant from a kept idea: its steps over its passage, with the notes under it as they are now (an idea keeps their
    /// ids, not their values), made by the player's steps. Null for an idea that keeps a note alone.
    /// </summary>
    public static PhraseVariant? FromObservation(Observation observation, IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(notes);
        if (observation.Variation is not { } kept)
        {
            return null;
        }

        Dictionary<string, Note> byId = notes.ToDictionary(static note => note.Id, StringComparer.Ordinal);
        BaseNote[] under = [.. kept.NoteIds.Select(id => byId.TryGetValue(id, out Note? note) ? new BaseNote(id, NoteValues.Of(note)) : new BaseNote(id, default))];
        return new PhraseVariant(observation.Id, observation.Name, kept.Passage, under, kept.Steps, kept.Keep, kept.Key, new VariantProvenance(VariantOrigin.Variation, observation.CreatedAt));
    }

    public bool Equals(PhraseVariant? other) =>
        other is not null
        && Id == other.Id
        && Name == other.Name
        && Phrase == other.Phrase
        && Key == other.Key
        && Provenance == other.Provenance
        && ParentId == other.ParentId
        && BarChange == other.BarChange
        && Base.SequenceEqual(other.Base)
        && Steps.SequenceEqual(other.Steps)
        && Keep.SequenceEqual(other.Keep);

    public override int GetHashCode() => HashCode.Combine(Id, Steps.Count, Phrase);
}