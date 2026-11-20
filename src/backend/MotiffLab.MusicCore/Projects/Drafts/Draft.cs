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
    /// «Поставить…» on the shelf: the shelf's variant <paramref name="variantId"/> as a variant of the place
    /// <paramref name="placeId"/> (<see cref="DraftDrop.Variant"/>) or as a new place of that id at the end (<see cref="DraftDrop.Place"/>).
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such variant on the shelf, or no such place.</exception>
    public Draft Placed(string variantId, DraftDrop drop, string placeId)
    {
        DraftVariant variant = Shelf.FirstOrDefault(each => each.Id == variantId) ?? throw new KeyNotFoundException($"There is no variant {variantId} on the shelf.");
        if (drop == DraftDrop.Shelf)
        {
            throw new ArgumentException("A variant on the shelf goes into a place.", nameof(drop));
        }

        return Changed(Places, [.. Shelf.Where(each => each.Id != variantId)], Steps).With(variant, drop, placeId);
    }

    /// <summary>
    /// The place <paramref name="placeId"/> with another of its variants as its base: its bars, meter, key and tempo from
    /// it. The steps of that place are removed, since they were taken against the base before (docs/project-file.md, "Drafts"),
    /// and so are the new base's anchors, which lined it up with that base.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place, or no such variant in it.</exception>
    public Draft WithBase(string placeId, string variantId)
    {
        DraftPlace place = Place(placeId);
        if (place.Find(variantId) is null)
        {
            throw new KeyNotFoundException($"The place {placeId} has no variant {variantId}.");
        }

        // The new base's anchors lined it up with the base until now: a base has none.
        DraftVariant[] variants = [.. place.Variants.Select(each => each.Id == variantId ? each with { Anchors = [] } : each)];
        return place.Base == variantId ? this : Changed([.. Places.Select(each => each.Id == placeId ? place.With(variants, variantId) : each)], Shelf, [.. Steps.Where(step => step.Place != placeId)]);
    }

    /// <summary>The steps of the journal that name the place, in number order.</summary>
    public IReadOnlyList<DraftStep> StepsOf(string placeId) => [.. Steps.Where(step => step.Place == placeId).OrderBy(static step => step.Number)];

    /// <summary>The result of the place <paramref name="placeId"/>: its base with its steps (<see cref="PlaceResult.Build"/>).</summary>
    public PlaceResult ResultOf(string placeId) => PlaceResult.Build(Place(placeId), StepsOf(placeId));

    /// <summary>The ids of every place and variant, for a new one never to take one.</summary>
    public IEnumerable<string> Ids => [.. Places.Select(static place => place.Id), .. Places.SelectMany(static place => place.Variants).Concat(Shelf).Select(static variant => variant.Id)];

    private DraftPlace Place(string? id) => (id is null ? null : FindPlace(id)) ?? throw new KeyNotFoundException($"The draft has no place {id}.");

    private Draft WithPlace(DraftPlace place) => Changed([.. Places.Select(each => each.Id == place.Id ? place : each)], Shelf, Steps);

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