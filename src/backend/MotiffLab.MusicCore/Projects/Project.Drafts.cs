using MotiffLab.MusicCore.Projects.Drafts;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The piece's drafts (decision record 0034; project file version 17): the player's own versions put together from
/// fragments of any Notes, of this piece or another, beside the Notes and the work. A draft never changes an original or
/// a Notes, no other part of the piece names one, and deleting a draft deletes only the draft.
/// </summary>
public sealed partial record Project
{
    /// <summary>The list of the file the drafts stand in, as <see cref="UnknownEntity.List"/> names it.</summary>
    public const string DraftsList = "drafts";

    /// <summary>The piece's drafts in the order they were made; a draft kept unknown (S6) is in <see cref="Unknown"/>.</summary>
    public IReadOnlyList<Draft> Drafts { get; init; } = [];

    public Draft? FindDraft(string id) => Drafts.FirstOrDefault(draft => draft.Id == id);

    /// <summary>The ids of the drafts, those kept unknown with them, so a new draft never takes one.</summary>
    public IEnumerable<string> DraftIds => [.. Drafts.Select(static draft => draft.Id), .. UnknownIds(Unknown, DraftsList)];

    /// <summary>The piece with a new draft at the end of its drafts.</summary>
    /// <exception cref="ArgumentException">The piece has a draft with that id, known or kept unknown.</exception>
    public Project WithNewDraft(Draft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return DraftIds.Contains(draft.Id, StringComparer.Ordinal)
            ? throw new ArgumentException($"The piece has a draft {draft.Id} already.", nameof(draft))
            : this with { Drafts = [.. Drafts, draft] };
    }

    /// <summary>The piece with the draft <paramref name="id"/> changed by <paramref name="change"/>; nothing else changes.</summary>
    /// <exception cref="KeyNotFoundException">The piece has no draft with the id.</exception>
    public Project ChangeDraft(string id, Func<Draft, Draft> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Draft draft = FindDraft(id) ?? throw new KeyNotFoundException($"There is no draft {id}.");
        Draft changed = change(draft);
        return changed.Id == id ? this with { Drafts = [.. Drafts.Select(each => each.Id == id ? changed : each)] } : throw new ArgumentException("A draft keeps its id.", nameof(change));
    }

    /// <summary>The piece without the draft <paramref name="id"/>: only the draft goes (decision record 0034, section 1).</summary>
    /// <exception cref="KeyNotFoundException">The piece has no draft with the id.</exception>
    public Project WithoutDraft(string id) =>
        FindDraft(id) is null ? throw new KeyNotFoundException($"There is no draft {id}.") : this with { Drafts = [.. Drafts.Where(draft => draft.Id != id)] };
}