using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// The player's decisions in a place as journal steps (decision record 0034, section 4): a приём taken everywhere («Взять
/// везде · N») or only here («Только здесь»), left as in the base («Оставить»), every sure приём at once («Взять всё
/// уверенное»), and a hand or a selection taken from a variant. Each writes what it takes as notes, the base notes it takes
/// out and the variant notes it puts in, as they are in the place's result now, so the rebuild reads only those; the приём is
/// its label. Pure: each gives a new draft.
/// </summary>
public static class DraftTakes
{
    /// <summary>
    /// The приём <paramref name="number"/> of the place taken: where it is still open, or, with <paramref name="bar"/>, only
    /// at that bar of the place (from 1). Inserted bars go in whole, at the bar line they fall on, in their own meter.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place or приём.</exception>
    /// <exception cref="InvalidOperationException">Nothing of it is open there.</exception>
    public static Draft Take(Draft draft, string placeId, int number, int? bar, DateTimeOffset at, string? group = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        (PlaceTechnique technique, IReadOnlyList<TechniqueOccurrence> occurrences) = Chosen(draft, placeId, number, bar);
        PlaceResult result = draft.ResultOf(placeId);
        HashSet<string> here = [.. result.Music.Notes.Select(static note => note.Id)];
        DraftTechnique label = Label(technique, occurrences);
        return draft.With(next => new DraftTake(next, at, placeId, technique.Variant)
        {
            Technique = label,
            Removed = [.. occurrences.SelectMany(static occurrence => occurrence.Removed).Where(here.Contains).Distinct(StringComparer.Ordinal)],
            Added = [.. occurrences.SelectMany(static occurrence => occurrence.Added).Where(id => !here.Contains(id)).Distinct(StringComparer.Ordinal)],
            Insert = occurrences[0].Insert is { } insert ? insert with { At = result.InResult(insert.At) } : null,
            Group = group,
        });
    }

    /// <summary>«Оставить»: the приём <paramref name="number"/> kept as in the base, where it is open or only at <paramref name="bar"/>; its number goes.</summary>
    /// <exception cref="KeyNotFoundException">No such place or приём.</exception>
    /// <exception cref="InvalidOperationException">Nothing of it is open there.</exception>
    public static Draft Leave(Draft draft, string placeId, int number, int? bar, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(draft);
        (PlaceTechnique technique, IReadOnlyList<TechniqueOccurrence> occurrences) = Chosen(draft, placeId, number, bar);
        return draft.With(next => new DraftLeave(next, at, placeId) { Variant = technique.Variant, Technique = Label(technique, occurrences) });
    }

    /// <summary>
    /// «Взять всё уверенное»: every sure приём of the place still open taken everywhere, in number order, as one group so one
    /// undo takes them back; inserted bars are never among them. The draft and the numbers taken, to show what it took.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place.</exception>
    /// <exception cref="InvalidOperationException">No sure приём is open.</exception>
    public static (Draft Draft, IReadOnlyList<int> Taken) TakeSure(Draft draft, string placeId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(draft);
        int[] sure = [.. draft.TechniquesOf(placeId).Where(static technique => technique.IsSure && technique.State == TechniqueState.Open).Select(static technique => technique.Number)];
        if (sure.Length == 0)
        {
            throw new InvalidOperationException("No sure приём of the place is left to take.");
        }

        string group = Project.NextId("g_", draft.Steps.Select(static step => step.Group).OfType<string>());
        return (sure.Aggregate(draft, (taken, number) => Take(taken, placeId, number, null, at, group)), sure);
    }

    /// <summary>
    /// «Правая рука из варианта…», «Взять отсюда»: the notes of <paramref name="part"/> (all, the right or the left hand) of
    /// the variant <paramref name="variantId"/> taken into the place over <paramref name="span"/> of its result (the whole
    /// place when null): the result's notes of that part there go, except the variant's own, and the variant's notes the
    /// alignment puts there come in.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place, or no such variant in it.</exception>
    /// <exception cref="ArgumentException">The base itself, or the melody, which is taken as a fragment of its own.</exception>
    /// <exception cref="InvalidOperationException">Nothing to take there: the result already has the variant's notes.</exception>
    public static Draft TakeFrom(Draft draft, string placeId, string variantId, DraftPart part, TickSpan? span, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(draft);
        DraftPlace place = draft.FindPlace(placeId) ?? throw new KeyNotFoundException($"The draft has no place {placeId}.");
        DraftVariant variant = place.Find(variantId) ?? throw new KeyNotFoundException($"The place {placeId} has no variant {variantId}.");
        if (variantId == place.Base || part == DraftPart.Melody)
        {
            throw new ArgumentException("A hand or a selection is taken from a variant that is not the base, all of it or one hand.", nameof(variantId));
        }

        PlaceResult result = draft.ResultOf(placeId);
        PerformanceAlignment alignment = DraftAlignment.Of(place, variant);
        bool Within(Tick tick) => span is not { } inside || inside.Contains(tick);
        bool OfPart(Note note) => part == DraftPart.All || note.Hand?.Value == (part == DraftPart.Right ? Hand.Right : Hand.Left);
        string own = variant.NoteId("");
        HashSet<string> here = [.. result.Music.Notes.Select(static note => note.Id)];
        string[] removed = [.. result.Music.Notes.Where(note => OfPart(note) && Within(note.Span.Onset) && !note.Id.StartsWith(own, StringComparison.Ordinal)).Select(static note => note.Id)];
        string[] added = [.. variant.Music.Notes
            .Where(note => OfPart(note) && Within(result.InResult(DraftAlignment.InBase(alignment, note.Span.Onset))))
            .Select(note => variant.NoteId(note.Id))
            .Where(id => !here.Contains(id))];
        if (added.Length == 0)
        {
            throw new InvalidOperationException("The variant has no notes to take there that the place does not have already.");
        }

        return draft.With(next => new DraftTake(next, at, placeId, variantId)
        {
            Part = part == DraftPart.All ? null : part,
            Removed = removed,
            Added = added,
        });
    }

    /// <summary>The приём and the occurrences a decision covers: the open ones, or the open one at <paramref name="bar"/>.</summary>
    private static (PlaceTechnique Technique, IReadOnlyList<TechniqueOccurrence> Occurrences) Chosen(Draft draft, string placeId, int number, int? bar)
    {
        PlaceTechnique technique = draft.TechniquesOf(placeId).FirstOrDefault(each => each.Number == number)
            ?? throw new KeyNotFoundException($"The place {placeId} has no приём {number}.");
        TechniqueOccurrence[] occurrences = [.. technique.Open.Where(occurrence => bar is null || occurrence.Bar == bar)];
        return occurrences.Length > 0
            ? (technique, occurrences)
            : throw new InvalidOperationException(bar is null ? $"The приём {number} is decided everywhere." : $"The приём {number} is not open at bar {bar}.");
    }

    private static DraftTechnique Label(PlaceTechnique technique, IReadOnlyList<TechniqueOccurrence> occurrences) =>
        new(technique.Kind, [.. occurrences.Select(static occurrence => occurrence.Bar)]);
}