namespace MotiffLab.Web.Workspace;

/// <summary>A «Что если» chip: an engine operation with fixed parameters, as its label reads.</summary>
public sealed record WhatIfChip(string Id, string Label);

/// <summary>One Keep check of a variant, in words: «Мелодия: сохранено».</summary>
/// <param name="Kept">True when kept, false when the steps changed it, null when the engine cannot check it.</param>
public sealed record VariantCheck(string Text, bool? Kept);

/// <summary>A chip of a variant in the card's header: kept, the draft being heard, or in the working notes.</summary>
public sealed record VariantChip(string Id, string Name, bool On, bool Draft, bool InWork);

/// <summary>
/// The variant shown, as the panel under the phrase card says it (the AI companion study, section 2.4): its name and where
/// it came from, its steps, what changed written by the engine from the diff, the Keep checks, and what can be done with it.
/// </summary>
/// <param name="Overline">«Вариант 2 · из варианта 1».</param>
/// <param name="Title">The steps that made it: «Нисходящее арпеджио › Бас на педали».</param>
/// <param name="Explanation">What changed, by comparing the notes (music-core's explanation).</param>
/// <param name="Draft">Not kept yet: Keep and Discard; kept: Delete.</param>
/// <param name="InWork">It is in the working notes.</param>
/// <param name="BaseChanged">The notes under it changed since it was made: Recompute.</param>
/// <param name="PromoteRefusal">Why it cannot be taken into work now; null when it can.</param>
public sealed record VariantCard(
    string Overline,
    string Title,
    string Explanation,
    IReadOnlyList<VariantCheck> Checks,
    bool Draft,
    bool InWork,
    bool BaseChanged,
    string? PromoteRefusal);