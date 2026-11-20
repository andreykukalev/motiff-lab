using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Export;
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

    /// <summary>The id a new draft takes: <c>d_</c> and the next number.</summary>
    public string NextDraftId() => NextId("d_", DraftIds);

    /// <summary>
    /// The draft <paramref name="id"/> as a piece only to be shown («вкладка черновика»): its score is the places' results in
    /// order (<see cref="DraftMusic"/>), as an assembly applied over its own MIDI, so it is engraved and played, each place in
    /// its own meter, key and tempo; with <paramref name="variant"/>, that variant's own music alone, from 0, so a приём is
    /// heard as the variant has it («как в нотах»). The piece itself does not change.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The piece has no draft with the id, or the draft no such variant.</exception>
    /// <exception cref="InvalidOperationException">The draft has no notes to show: no place yet, or its places' steps took every note out.</exception>
    public Project ViewOfDraft(string id, string? variant = null)
    {
        Draft draft = FindDraft(id) ?? throw new KeyNotFoundException($"There is no draft {id}.");
        SourceMusic music = variant is null ? DraftMusic.Of(draft).Music
            : (draft.Variants.FirstOrDefault(each => each.Id == variant) ?? throw new KeyNotFoundException($"The draft has no variant {variant}.")).Music;
        if (music.Notes.Count == 0)
        {
            throw new InvalidOperationException($"The draft {draft.Name} has no notes to show yet: add a fragment to it first.");
        }

        byte[] midi = MidiExporter.Export(music.Notes, music.Tempos, music.Meter, draft.Name);
        return Create(Id, $"{draft.Name}.mid", midi, draft.CreatedAt) with { Assembly = PieceAssembly.Empty with { Applied = music } };
    }

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

    /// <summary>
    /// The piece without the draft <paramref name="id"/>: only the draft goes (decision record 0034, section 1), and its id
    /// leaves the views hidden from the switch.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The piece has no draft with the id.</exception>
    public Project WithoutDraft(string id) =>
        FindDraft(id) is null ? throw new KeyNotFoundException($"There is no draft {id}.")
            : this with { Drafts = [.. Drafts.Where(draft => draft.Id != id)], HiddenViews = [.. HiddenViews.Where(view => view != id)] };
}