using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// A variant lined up with its place's base (decision record 0034, section 2): as a version is with the score (0022,
/// section 2), through the anchors the player confirmed, which are locked, the rest worked out again around them on every
/// open. The rebuild of a place and its приёмы read the same alignment.
/// </summary>
internal static class DraftAlignment
{
    /// <summary>The variant <paramref name="variant"/> of <paramref name="place"/> lined up with the place's base: a tick of the variant × 1000 to a tick of the base.</summary>
    public static PerformanceAlignment Of(DraftPlace place, DraftVariant variant) =>
        AssemblyMatch.Aligned(variant.Music, place.BaseVariant.Music.Notes, Confirmed(variant));

    /// <summary>The variant's confirmed anchors as the comparison takes them: a tick of the base, a tick of the variant.</summary>
    public static IReadOnlyList<(Tick Notes, Tick Source)> Confirmed(DraftVariant variant) =>
        [.. variant.Anchors.Select(static anchor => (anchor.Place, anchor.Variant))];

    /// <summary>Where a tick of the variant falls in the base: through the alignment, or where it is when nothing lines up.</summary>
    public static Tick InBase(PerformanceAlignment alignment, Tick tick) =>
        alignment.Anchors.Count == 0 ? tick : alignment.TickAt(tick.Value * 1000);

    /// <summary>Where a tick of the base falls in the variant, the other way from <see cref="InBase"/>; never before its start.</summary>
    public static Tick InVariant(PerformanceAlignment alignment, Tick tick) =>
        alignment.Anchors.Count == 0 ? tick : new Tick(Math.Max(0, alignment.MicrosecondsAt(tick) / 1000));
}