namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// One stretch of music in a draft (decision record 0034, section 2): the same music from different sources as variants,
/// one of them the base, which gives the place its bars, meter, key and tempo. Its result is the base with the journal's
/// steps that name it (<see cref="PlaceResult"/>).
/// </summary>
/// <param name="Base">The id of the variant that is the base.</param>
public sealed record DraftPlace(string Id, string Base, IReadOnlyList<DraftVariant> Variants)
{
    public string Base { get; } = Base;

    public string Id { get; } = !string.IsNullOrWhiteSpace(Id) ? Id : throw new ArgumentException("A place has an id.", nameof(Id));

    public IReadOnlyList<DraftVariant> Variants { get; } = Variants is { Count: > 0 } && Variants.Any(variant => variant.Id == Base)
        && Variants.Select(static variant => variant.Id).Distinct(StringComparer.Ordinal).Count() == Variants.Count
        ? [.. Variants]
        : throw new ArgumentException("A place has distinct variants, its base among them.", nameof(Variants));

    /// <summary>The pencil marks on the place, in its ticks; empty when none.</summary>
    public Marks Marks { get; init; } = Marks.Empty;

    /// <summary>Whether the place was made for a seam, its variants the bridge candidates (decision record 0034, section 5).</summary>
    public bool Bridge { get; init; }

    /// <summary>The variant that is the base.</summary>
    public DraftVariant BaseVariant => Variants.First(variant => variant.Id == Base);

    public DraftVariant? Find(string variant) => Variants.FirstOrDefault(each => each.Id == variant);

    /// <summary>The place with other variants or another base, its marks and whether it is a bridge kept.</summary>
    public DraftPlace With(IReadOnlyList<DraftVariant> variants, string @base) => new(Id, @base, variants) { Marks = Marks, Bridge = Bridge };
}