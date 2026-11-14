namespace MotiffLab.Web.Compare;

/// <summary>
/// What a <see cref="CompareSwitch"/> shows when it is not the workspace's variation, such as a repair draft in the Check
/// drawer (version 0.6): which of the two is heard, whether they alternate, which sounds, and the two names.
/// </summary>
/// <param name="Sounding">While the two alternate: true while the second sounds, false while the first does; otherwise null.</param>
public sealed record CompareView(bool Original, bool Variation, bool Alternating, bool? Sounding, string OriginalLabel, string VariationLabel);