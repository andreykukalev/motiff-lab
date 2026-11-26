using MotiffLab.MusicCore.Assembly;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>How much of a fragment the alignment finds in a place's base: the share of the fragment's notes, in whole per cent.</summary>
public sealed record PlaceMatch(string Place, int Matched, int Total)
{
    /// <summary>The share in whole per cent, rounded down.</summary>
    public int Percent => Total == 0 ? 0 : Matched * 100 / Total;

    /// <summary>
    /// «Та же музыка» (decision record 0034, section 3, decided without Andrei): at least half of the fragment's notes found
    /// in place, as «частично» in 0024 (<see cref="AssemblyMatch.PartlyShare"/>).
    /// </summary>
    public bool IsSameMusic => Total > 0 && Matched >= Total * AssemblyMatch.PartlyShare;
}

/// <summary>
/// The «та же музыка» suggestion of the «В черновик» sheet (decision record 0034, section 3): a fragment compared with the
/// base of each place of a draft by the comparison of 0024 (the alignment of 0022, then each note looked for at its place),
/// so a variant of the place where it is the same music is offered first, with the percentage.
/// </summary>
public static class SameMusic
{
    /// <summary>The fragment against the base of every place of the draft, in the draft's order.</summary>
    public static IReadOnlyList<PlaceMatch> Of(Draft draft, SourceMusic fragment)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(fragment);
        return [.. draft.Places.Select(place => In(place, fragment))];
    }

    /// <summary>The fragment against the base of <paramref name="place"/>: how many of its notes are found there.</summary>
    public static PlaceMatch In(DraftPlace place, SourceMusic fragment)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(fragment);
        return new PlaceMatch(place.Id, AssemblyMatch.Of("fragment", fragment, place.BaseVariant.Music, "").Bars.Sum(static bar => bar.Matched), fragment.Notes.Count);
    }

    /// <summary>The place offered first: the one where most of the fragment is found, when that is the same music; ties go to the earlier place.</summary>
    public static PlaceMatch? Suggested(IReadOnlyList<PlaceMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return matches.Where(static match => match.IsSameMusic).OrderByDescending(static match => match.Matched * 1000L / match.Total).FirstOrDefault();
    }
}