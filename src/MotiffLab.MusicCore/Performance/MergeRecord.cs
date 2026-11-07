using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Performance;

/// <summary>
/// What the player chose for a bar the version plays otherwise, in the merge draft of contract 7 (0022, section 5; testing
/// items 32 and 34). The draft gave way to the assembly (0024); a project file that kept a merge still reads and writes it.
/// </summary>
public enum MergeChoice
{
    /// <summary>The player's own notes stay.</summary>
    Mine,

    /// <summary>The bar is taken from the version.</summary>
    Version,

    /// <summary>The player wrote the bar themselves in the roll, from either side.</summary>
    Own,

    /// <summary>Not a difference: nothing changes and the bar's marker stays away.</summary>
    NotADifference,
}

/// <summary>A choice for one bar. <paramref name="Own"/> holds the bar the player wrote, only for <see cref="MergeChoice.Own"/>.</summary>
public sealed record MergeDecision(int Bar, MergeChoice Choice, TakenBar? Own = null)
{
    public TakenBar? Own { get; } = Choice == MergeChoice.Own
        ? Own ?? throw new ArgumentException("A bar the player wrote needs its notes.", nameof(Own))
        : null;

    public bool Equals(MergeDecision? other) =>
        other is not null && Bar == other.Bar && Choice == other.Choice
        && (Own is null ? other.Own is null : other.Own is not null && Own.Bar == other.Own.Bar && Own.Removed.SequenceEqual(other.Own.Removed) && Own.Added.SequenceEqual(other.Own.Added));

    public override int GetHashCode() => HashCode.Combine(Bar, Choice);
}

/// <summary>
/// The merge kept with a version (project file, <c>merge</c>): the choices of the last draft applied, so the draft can
/// be opened again with them, and when it was applied; null <paramref name="AppliedAt"/> once the merge was undone.
/// </summary>
public sealed record MergeRecord(IReadOnlyList<MergeDecision> Decisions, DateTimeOffset? AppliedAt)
{
    public bool Equals(MergeRecord? other) => other is not null && AppliedAt == other.AppliedAt && Decisions.SequenceEqual(other.Decisions);

    public override int GetHashCode() => HashCode.Combine(Decisions.Count, AppliedAt);
}