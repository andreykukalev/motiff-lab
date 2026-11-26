using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// The anchors the player confirms between a place's base and one of its other variants (decision record 0034, section 2;
/// the plan's answer 4, «Не тот такт? Сопоставить…» and «Открепить»): a tick of the base and a tick of the variant that
/// sound together, locked, the rest of the alignment worked out again around them on every open (<see cref="DraftAlignment"/>),
/// so the rebuild and the приёмы follow them. Never on a base or a variant on the shelf; both ticks strictly increase.
/// Anchors are the variant's state, not steps. Pure: each gives a new draft.
/// </summary>
internal static class DraftAnchors
{
    /// <summary>
    /// «Сопоставить»: <paramref name="anchor"/> confirmed on the variant <paramref name="variantId"/> of the place
    /// <paramref name="placeId"/>, in place of any it had at either tick, the others kept.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place, or no such variant in it.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A tick outside the base or the variant.</exception>
    /// <exception cref="ArgumentException">The place's base, or an anchor that crosses one confirmed already.</exception>
    public static Draft Confirm(Draft draft, string placeId, string variantId, DraftAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(draft);
        (DraftPlace place, DraftVariant variant) = Of(draft, placeId, variantId);
        if (anchor.Place.Value < 0 || anchor.Place >= place.BaseVariant.Music.End)
        {
            throw new ArgumentOutOfRangeException(nameof(anchor), anchor.Place.Value, "An anchor is a tick of the place's base.");
        }

        if (anchor.Variant.Value < 0 || anchor.Variant >= variant.Music.End)
        {
            throw new ArgumentOutOfRangeException(nameof(anchor), anchor.Variant.Value, "An anchor is a tick of the variant.");
        }

        // A base has no anchors and a crossing one does not strictly increase: the draft refuses both (DraftPlace, DraftVariant).
        return draft.WithAnchors(placeId, variantId, [.. variant.Anchors.Where(each => each.Place != anchor.Place && each.Variant != anchor.Variant).Append(anchor).OrderBy(static each => each.Place)]);
    }

    /// <summary>«Открепить»: the anchor at the base's tick <paramref name="at"/> of the variant <paramref name="variantId"/> taken away.</summary>
    /// <exception cref="KeyNotFoundException">No such place, variant or anchor.</exception>
    public static Draft Remove(Draft draft, string placeId, string variantId, Tick at)
    {
        ArgumentNullException.ThrowIfNull(draft);
        (_, DraftVariant variant) = Of(draft, placeId, variantId);
        return variant.Anchors.Any(each => each.Place == at)
            ? draft.WithAnchors(placeId, variantId, [.. variant.Anchors.Where(each => each.Place != at)])
            : throw new KeyNotFoundException($"The variant {variantId} has no anchor at {at.Value}.");
    }

    private static (DraftPlace Place, DraftVariant Variant) Of(Draft draft, string placeId, string variantId)
    {
        DraftPlace place = draft.FindPlace(placeId) ?? throw new KeyNotFoundException($"The draft has no place {placeId}.");
        return (place, place.Find(variantId) ?? throw new KeyNotFoundException($"The place {placeId} has no variant {variantId}."));
    }
}