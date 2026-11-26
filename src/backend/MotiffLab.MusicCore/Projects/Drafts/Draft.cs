using System.Runtime.CompilerServices;

using MotiffLab.MusicCore.Pieces;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>Where a fragment added to a draft goes (decision record 0034, section 3): never merged by itself.</summary>
public enum DraftDrop
{
    /// <summary>As a variant of a place: «та же музыка».</summary>
    Variant,

    /// <summary>As a new place at the end.</summary>
    Place,

    /// <summary>On the shelf, not yet in a place.</summary>
    Shelf,
}

/// <summary>
/// The player's own version put together from fragments of any Notes, of this piece or another, without changing them
/// (decision record 0034, sections 1 and 2): a name, places in order (its bars numbered from 1 through them), a shelf and
/// a journal. Its structure (the places, their order, their variants and base, the shelf) is its state; its steps change
/// the places' music. Every operation is pure and gives a new draft.
/// </summary>
/// <param name="Next">The number the next step takes.</param>
public sealed record Draft(string Id, string Name, DateTimeOffset CreatedAt, IReadOnlyList<DraftPlace> Places, IReadOnlyList<DraftVariant> Shelf, IReadOnlyList<DraftStep> Steps, int Next)
{
    /// <summary>
    /// The tally of each draft instance (<see cref="Tally"/>), by reference: a draft never changes, a change makes another,
    /// so a kept tally never goes stale; not a field, which <c>with</c> would copy into the changed draft.
    /// </summary>
    private static readonly ConditionalWeakTable<Draft, StrongBox<(int, int)>> s_tallies = [];

    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A draft has an id.", nameof(Id));

    /// <summary>One line, as the player named it.</summary>
    public string Name { get; } = PieceMetadata.Clean(Name) ?? throw new ArgumentException("A draft has a name.", nameof(Name));

    public IReadOnlyList<DraftPlace> Places { get; } = Checked(Places, Shelf, Steps, Next);

    public IReadOnlyList<DraftVariant> Shelf { get; } = [.. Shelf];

    public IReadOnlyList<DraftStep> Steps { get; } = [.. Steps];

    /// <summary>A new draft of one place, the fragment its base (from the selection menu, «Новый черновик»).</summary>
    public static Draft Start(string id, string name, DateTimeOffset at, string placeId, DraftVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return new Draft(id, name, at, [new DraftPlace(placeId, variant.Id, [variant])], [], [], 1);
    }

    public DraftPlace? FindPlace(string id) => Places.FirstOrDefault(place => place.Id == id);

    /// <summary>The draft renamed.</summary>
    public Draft Renamed(string name) => new(Id, name, CreatedAt, Places, Shelf, Steps, Next);

    /// <summary>
    /// The draft with a fragment added where the player says: as a variant of the place <paramref name="placeId"/>, as a
    /// new place <paramref name="placeId"/> at the end, or on the shelf.
    /// </summary>
    /// <exception cref="KeyNotFoundException">A variant of a place the draft does not have.</exception>
    /// <exception cref="ArgumentException">An id the draft has already, or no place id where one is needed.</exception>
    public Draft With(DraftVariant variant, DraftDrop drop, string? placeId = null)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return drop switch
        {
            DraftDrop.Variant => WithPlace(Place(placeId).With([.. Place(placeId).Variants, variant], Place(placeId).Base)),
            DraftDrop.Place => Changed([.. Places, new DraftPlace(placeId ?? throw new ArgumentException("A new place has an id.", nameof(placeId)), variant.Id, [variant])], Shelf, Steps),
            _ => Changed(Places, [.. Shelf, variant], Steps),
        };
    }

    /// <summary>
    /// «Поставить…» on the shelf, or a variant moved on the draft's map: the variant <paramref name="variantId"/>, on the shelf
    /// or a place's that is not its base, as a variant of the place <paramref name="placeId"/> (<see cref="DraftDrop.Variant"/>),
    /// as a new place of that id at the end (<see cref="DraftDrop.Place"/>) or on the shelf (<see cref="DraftDrop.Shelf"/>,
    /// no place id). Taken out of a place, it leaves its anchors and the steps that name it (<see cref="StepsNaming"/>), which
    /// were taken from it there (docs/project-file.md, "Drafts").
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such variant, or no such place.</exception>
    /// <exception cref="ArgumentException">A place's base, which stays until another is chosen, or a variant put where it is already.</exception>
    public Draft Placed(string variantId, DraftDrop drop, string? placeId)
    {
        DraftPlace? from = Places.FirstOrDefault(place => place.Find(variantId) is not null);
        DraftVariant variant = from?.Find(variantId) ?? Shelf.FirstOrDefault(each => each.Id == variantId) ?? throw new KeyNotFoundException($"The draft has no variant {variantId}.");
        if (from?.Base == variantId)
        {
            throw new ArgumentException($"The variant {variantId} is the base of its place: choose another base first.", nameof(variantId));
        }

        if (from is null ? drop == DraftDrop.Shelf : drop == DraftDrop.Variant && from.Id == placeId)
        {
            throw new ArgumentException($"The variant {variantId} is there already.", nameof(drop));
        }

        Draft taken = from is null
            ? Changed(Places, [.. Shelf.Where(each => each.Id != variantId)], Steps)
            : Changed([.. Places.Select(each => each.Id == from.Id ? each.With([.. each.Variants.Where(other => other.Id != variantId)], each.Base) : each)], Shelf, [.. Steps.Except(StepsNaming(variantId))]);
        return taken.With(variant with { Anchors = [] }, drop, placeId);
    }

    /// <summary>The steps of the journal that take from or leave the variant <paramref name="variantId"/>: they go with it when it leaves its place.</summary>
    public IReadOnlyList<DraftStep> StepsNaming(string variantId) =>
        [.. Steps.Where(step => step switch { DraftTake take => take.Variant == variantId, DraftLeave leave => leave.Variant == variantId, _ => false })];

    /// <summary>
    /// The place <paramref name="placeId"/> with another of its variants as its base: its bars, meter, key and tempo from
    /// it. The steps of that place are removed, since they were taken against the base before (docs/project-file.md, "Drafts"),
    /// and so are the anchors of all its variants, which lined them up with that base: the place is compared anew.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place, or no such variant in it.</exception>
    public Draft WithBase(string placeId, string variantId)
    {
        DraftPlace place = Place(placeId);
        if (place.Find(variantId) is null)
        {
            throw new KeyNotFoundException($"The place {placeId} has no variant {variantId}.");
        }

        // Every anchor lined a variant up with the base until now, the new base's among them: a base has none.
        DraftVariant[] variants = [.. place.Variants.Select(static each => each with { Anchors = [] })];
        return place.Base == variantId ? this : Changed([.. Places.Select(each => each.Id == placeId ? place.With(variants, variantId) : each)], Shelf, [.. Steps.Where(step => step.Place != placeId)]);
    }

    /// <summary>
    /// The draft with <paramref name="place"/> put just before the place <paramref name="beforeId"/>: a bridge at the seam
    /// before it (<see cref="DraftBridges"/>). That place has another place before it now (<see cref="Reshaped"/>).
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place.</exception>
    /// <exception cref="ArgumentException">An id the draft has already.</exception>
    public Draft WithPlaceBefore(DraftPlace place, string beforeId)
    {
        ArgumentNullException.ThrowIfNull(place);
        int index = Places.ToList().IndexOf(Place(beforeId));
        return Reshaped([.. Places.Take(index), place, .. Places.Skip(index)]);
    }

    /// <summary>
    /// «Карта черновика: порядок мест»: the place <paramref name="placeId"/> moved to <paramref name="index"/> among the places
    /// (from 0), the others in their order; its variants, marks and steps go with it. Where a place has other neighbours
    /// now, what was made for its seams goes (<see cref="Reshaped"/>). A bridge itself is not moved.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An index outside the places.</exception>
    /// <exception cref="ArgumentException">The place is at that index already.</exception>
    /// <exception cref="InvalidOperationException">A bridge place, built for its seam (the lead's decision of 2026-11-26).</exception>
    public Draft WithPlaceAt(string placeId, int index)
    {
        DraftPlace place = Place(placeId);
        if (place.Bridge)
        {
            throw new InvalidOperationException($"The place {placeId} is a bridge, built for its seam: it is not moved.");
        }

        if (index < 0 || index >= Places.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"A place moves to a position from 0 to {Places.Count - 1}.");
        }

        if (Places[index].Id == placeId)
        {
            throw new ArgumentException($"The place {placeId} is there already.", nameof(index));
        }

        List<DraftPlace> places = [.. Places.Where(each => each.Id != placeId)];
        places.Insert(index, place);
        return Reshaped(places);
    }

    /// <summary>The draft without the place <paramref name="placeId"/>: its variants go with it, and the steps that name it, and what its neighbours' seams lose (<see cref="Reshaped"/>).</summary>
    /// <exception cref="KeyNotFoundException">No such place.</exception>
    public Draft WithoutPlace(string placeId)
    {
        _ = Place(placeId);
        return Reshaped([.. Places.Where(place => place.Id != placeId)]);
    }

    /// <summary>
    /// The variant <paramref name="variantId"/> of the place <paramref name="placeId"/>, not its base, with other anchors
    /// (<see cref="DraftAnchors"/>); its steps stay, read through the alignment the anchors give.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place, or no such variant in it.</exception>
    /// <exception cref="ArgumentException">The place's base, which has no anchors, or anchors that do not strictly increase.</exception>
    public Draft WithAnchors(string placeId, string variantId, IReadOnlyList<DraftAnchor> anchors)
    {
        DraftPlace place = Place(placeId);
        DraftVariant variant = place.Find(variantId) ?? throw new KeyNotFoundException($"The place {placeId} has no variant {variantId}.");
        return WithPlace(place.With([.. place.Variants.Select(each => each.Id == variantId ? variant with { Anchors = anchors } : each)], place.Base));
    }

    /// <summary>The draft with <paramref name="step"/> at the end of its journal: it takes the number <see cref="Next"/>, and the next one counts on.</summary>
    /// <exception cref="ArgumentException">The step has another number, or names a place or a variant the draft has not.</exception>
    public Draft With(Func<int, DraftStep> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        DraftStep made = step(Next);
        return made.Number == Next
            ? new Draft(Id, Name, CreatedAt, Places, Shelf, [.. Steps, made], Next + 1)
            : throw new ArgumentException($"The next step is number {Next}.", nameof(step));
    }

    /// <summary>
    /// Undo: the last step of the journal goes, and with it every step accepted together with it (its group). The numbers
    /// count on, so a step undone never lends its number to another.
    /// </summary>
    /// <exception cref="InvalidOperationException">The journal is empty.</exception>
    public Draft Undone()
    {
        DraftStep last = Steps.MaxBy(static step => step.Number) ?? throw new InvalidOperationException("The draft has no step to undo.");
        return Changed(Places, Shelf, [.. Steps.Where(step => step.Number != last.Number && (last.Group is null || step.Group != last.Group))]);
    }

    /// <summary>The steps of the journal that name the place, in number order.</summary>
    public IReadOnlyList<DraftStep> StepsOf(string placeId) => [.. Steps.Where(step => step.Place == placeId).OrderBy(static step => step.Number)];

    /// <summary>The result of the place <paramref name="placeId"/>: its base with its steps (<see cref="PlaceResult.Build"/>).</summary>
    public PlaceResult ResultOf(string placeId) => PlaceResult.Build(Place(placeId), StepsOf(placeId));

    /// <summary>
    /// The приёмы of the place <paramref name="placeId"/>'s variants against its base, decided as its steps left them
    /// (<see cref="PlaceTechniques.Of"/>); none in a bridge place, whose variants are candidates to choose from, not sources
    /// to take from.
    /// </summary>
    public IReadOnlyList<PlaceTechnique> TechniquesOf(string placeId) => Place(placeId) is { Bridge: false } place ? PlaceTechniques.Of(place, StepsOf(placeId)) : [];

    /// <summary>
    /// The приёмы of all its places, how many are taken of how many (<see cref="PlaceTechniques.Tally"/>), for the Sources
    /// caption: worked out once for this draft, which never changes, and kept beside it (<see cref="s_tallies"/>).
    /// </summary>
    public (int Taken, int Total) Tally() =>
        s_tallies.GetValue(this, static draft => new StrongBox<(int, int)>(PlaceTechniques.Tally([.. draft.Places.SelectMany(place => draft.TechniquesOf(place.Id))]))).Value;

    /// <summary>The ids of every place and variant, for a new one never to take one.</summary>
    public IEnumerable<string> Ids => [.. Places.Select(static place => place.Id), .. Variants.Select(static variant => variant.Id)];

    /// <summary>Every variant of the draft: its places' in order, then the shelf's.</summary>
    public IEnumerable<DraftVariant> Variants => Places.SelectMany(static place => place.Variants).Concat(Shelf);

    /// <summary>The id a new place takes: <c>pl_</c> and the next number in the draft.</summary>
    public string NextPlaceId() => Project.NextId("pl_", Ids);

    /// <summary>The id a new variant takes: <c>v_</c> and the next number in the draft.</summary>
    public string NextVariantId() => Project.NextId("v_", Ids);

    private DraftPlace Place(string? id) => (id is null ? null : FindPlace(id)) ?? throw new KeyNotFoundException($"The draft has no place {id}.");

    /// <summary>
    /// The draft with its places in the order <paramref name="places"/>, some put in, taken out or moved, and what was made
    /// for a seam that is another now gone with it: a bridge place (<see cref="DraftPlace.Bridge"/>) whose place before or
    /// after it is another, with its steps, until no bridge left has; then each step that keeps the seam before a place as it
    /// is («Оставить», a <see cref="DraftLeave"/> with <see cref="DraftLeave.Seam"/>), of a place with another place before
    /// it now. The steps of a place taken out go with it.
    /// </summary>
    private Draft Reshaped(List<DraftPlace> places)
    {
        static (string?, string?) Neighbours(IReadOnlyList<DraftPlace> order, string id)
        {
            int index = order.ToList().FindIndex(place => place.Id == id);
            return index < 0 ? (null, null) : (index > 0 ? order[index - 1].Id : null, index < order.Count - 1 ? order[index + 1].Id : null);
        }

        HashSet<string> were = [.. Places.Select(static place => place.Id)];
        while (places.FirstOrDefault(place => place.Bridge && were.Contains(place.Id) && Neighbours(places, place.Id) != Neighbours(Places, place.Id)) is { } stale)
        {
            places.Remove(stale);
        }

        HashSet<string> kept = [.. places.Select(static place => place.Id)];
        HashSet<string> reseamed = [.. places.Select(static place => place.Id).Where(id => Neighbours(places, id).Item1 != Neighbours(Places, id).Item1)];
        return Changed(places, Shelf, [.. Steps.Where(step => kept.Contains(step.Place) && !(step is DraftLeave { Seam: true } && reseamed.Contains(step.Place)))]);
    }

    private Draft WithPlace(DraftPlace place) => WithPlace(place, Steps);

    /// <summary>The draft with <paramref name="place"/> in place of the one of its id and <paramref name="steps"/> as its journal (<see cref="PlaceLength"/>), checked again as a new one is.</summary>
    internal Draft WithPlace(DraftPlace place, IReadOnlyList<DraftStep> steps) => Changed([.. Places.Select(each => each.Id == place.Id ? place : each)], Shelf, steps);

    /// <summary>The draft with other places, shelf or steps, checked again as a new one is.</summary>
    private Draft Changed(IReadOnlyList<DraftPlace> places, IReadOnlyList<DraftVariant> shelf, IReadOnlyList<DraftStep> steps) => new(Id, Name, CreatedAt, places, shelf, steps, Next);

    /// <summary>The places, when the ids of places and variants are distinct and each step names a place and a variant the draft has.</summary>
    private static IReadOnlyList<DraftPlace> Checked(IReadOnlyList<DraftPlace> places, IReadOnlyList<DraftVariant> shelf, IReadOnlyList<DraftStep> steps, int next)
    {
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(shelf);
        ArgumentNullException.ThrowIfNull(steps);
        string[] variants = [.. places.SelectMany(static place => place.Variants).Concat(shelf).Select(static variant => variant.Id)];
        if (places.Select(static place => place.Id).Distinct(StringComparer.Ordinal).Count() != places.Count || variants.Distinct(StringComparer.Ordinal).Count() != variants.Length)
        {
            throw new ArgumentException("The places and variants of a draft have distinct ids.", nameof(places));
        }

        if (shelf.Any(static variant => variant.Anchors.Count > 0))
        {
            throw new ArgumentException("A variant on the shelf has no anchors: they line up a variant of a place with its base.", nameof(shelf));
        }

        if (steps.Select(static step => step.Number).Distinct().Count() != steps.Count || steps.Any(step => step.Number >= next))
        {
            throw new ArgumentException("The steps of a draft have distinct numbers below the next one.", nameof(steps));
        }

        foreach (DraftStep step in steps)
        {
            DraftPlace place = places.FirstOrDefault(each => each.Id == step.Place) ?? throw new ArgumentException($"A step names a place the draft has not: {step.Place}.", nameof(steps));
            string? variant = step switch { DraftTake take => take.Variant, DraftLeave leave => leave.Variant, _ => null };
            if (variant is not null && place.Find(variant) is null)
            {
                throw new ArgumentException($"A step names a variant its place has not: {variant}.", nameof(steps));
            }
        }

        return [.. places];
    }
}