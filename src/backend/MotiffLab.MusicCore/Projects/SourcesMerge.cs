using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Projects.Drafts;
using MotiffLab.MusicCore.Projects.File;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// How two copies of one piece's originals and Notes become one (decision record 0030, section 5; 0026 S8, S9): a removal
/// travels as its tombstone, Notes merge by id, and corrections inside the same Notes merge note by note. With the copy
/// both last agreed on (the base), a deletion on one side goes through; without it, the merge is a union with no
/// deletions (0026 S4), so an undone correction or removed Notes still on the other copy come back.
/// </summary>
internal static class SourcesMerge
{
    /// <summary>
    /// <paramref name="project"/> with the tombstones of both copies (its own first, then those of <paramref name="other"/>
    /// it has none for). A tombstone wins over a live description of the same original (by fingerprint, else by id)
    /// unless that copy added or held it again after the removal, which revives it: then the tombstone goes.
    /// </summary>
    public static Project WithTombstones(Project project, IReadOnlyList<RemovedOriginal> other)
    {
        List<RemovedOriginal> all = [.. project.Sources.Removed];
        all.AddRange(other.Where(tombstone => !all.Any(each => each.Original.Id == tombstone.Original.Id)));
        List<RemovedOriginal> kept = [];
        foreach (RemovedOriginal tombstone in all)
        {
            PieceOriginal? live = project.Originals.FirstOrDefault(original => Same(original, tombstone.Original));
            if (live is not null && Revives(live, tombstone))
            {
                continue;
            }

            project = live is null ? project : OriginalOperations.Gone(project, live.Id);
            kept.Add(tombstone);
        }

        return kept.SequenceEqual(project.Sources.Removed) ? project : project with { Sources = project.Sources with { Removed = kept } };
    }

    private static bool Same(PieceOriginal live, PieceOriginal removed) =>
        live.Sha256 is { } sha && removed.Sha256 is not null ? removed.Matches(sha) : live.Id == removed.Id;

    private static bool Revives(PieceOriginal live, RemovedOriginal tombstone) =>
        live.AddedAt > tombstone.At || live.Held.Any(mark => mark.At > tombstone.At);

    /// <summary>
    /// The copy that won (<paramref name="winner"/>) with what only the other copy (<paramref name="loser"/>) has of the
    /// piece's Notes: its tombstones; the other Notes and compared versions it added, by id; and its corrections of notes the
    /// winner did not correct, in the working notes and in other Notes alike, while both are the same Notes with the same
    /// music (S9 and its source guard). A correction of the same note on both sides is a conflict the winner's side takes;
    /// the loser's stays in the copy kept beside.
    /// </summary>
    public static Project Merged(Project winner, Project loser, Project? @base)
    {
        Project merged = WithTombstones(winner, loser.Sources.Removed);
        if (merged.HasNotes && merged.NotesId == loser.NotesId && merged.Source.AsSpan().SequenceEqual(loser.Source))
        {
            bool sameBase = @base is not null && @base.NotesId == merged.NotesId && @base.Source.AsSpan().SequenceEqual(merged.Source);
            NoteEditLog edits = Edits(merged.Edits, loser.Edits, sameBase ? @base!.Edits : null);
            merged = edits.Equals(merged.Edits) ? merged : merged with { Edits = edits };
        }

        merged = OtherNotes(merged, loser, @base);
        return Drafts(Versions(merged, loser, @base), loser, @base);
    }

    /// <summary>
    /// The drafts of both copies by id, as Notes merge (decision record 0034, decision log; 0026 S8): the drafts only the
    /// other copy has come in, and one changed only on the other side since the base takes that side's. The same draft
    /// changed on both is a conflict: the winner's stands, the other's stays in the copy kept beside. With the base, a
    /// draft removed on one side since then goes; without it, it comes back.
    /// </summary>
    private static Project Drafts(Project merged, Project loser, Project? @base)
    {
        List<Draft> drafts = [];
        foreach (Draft mine in merged.Drafts)
        {
            Draft? theirs = loser.FindDraft(mine.Id);
            Draft? before = @base?.FindDraft(mine.Id);
            bool unchanged = before is not null && DraftsCodec.Same(before, mine);
            if (theirs is null && unchanged && !Project.UnknownIds(loser.Unknown, Project.DraftsList).Contains(mine.Id, StringComparer.Ordinal))
            {
                continue;
            }

            drafts.Add(theirs is not null && unchanged ? theirs : mine);
        }

        HashSet<string> known = [.. merged.DraftIds];
        HashSet<string> inBase = @base is null ? [] : [.. @base.DraftIds];
        drafts.AddRange(loser.Drafts.Where(theirs => !known.Contains(theirs.Id) && !inBase.Contains(theirs.Id)));
        return drafts.SequenceEqual(merged.Drafts) ? merged : merged with { Drafts = drafts };
    }

    private static Project OtherNotes(Project merged, Project loser, Project? @base)
    {
        List<OtherNotes> notes = [];
        List<string> removed = [];
        foreach (OtherNotes mine in merged.Sources.Notes)
        {
            OtherNotes? theirs = loser.Sources.Notes.FirstOrDefault(each => each.Id == mine.Id);
            OtherNotes? before = @base?.Sources.Notes.FirstOrDefault(each => each.Id == mine.Id);
            if (theirs is null)
            {
                // Removed on the other side since the base, and not changed here, they go; else they stay.
                if (before is not null && before.Equals(mine) && loser.NotesId != mine.Id)
                {
                    removed.Add(mine.Id);
                }

                notes.Add(mine);
                continue;
            }

            bool sameBase = before is not null && before.Music.AsSpan().SequenceEqual(mine.Music);
            notes.Add(theirs.Music.AsSpan().SequenceEqual(mine.Music) ? mine with { Edits = Edits(mine.Edits, theirs.Edits, sameBase ? before!.Edits : null) } : mine);
        }

        HashSet<string> known = [merged.NotesId, .. notes.Select(static each => each.Id), .. merged.Performances.Select(static each => each.Id), .. Project.UnknownIds(merged.Unknown, "notes")];
        HashSet<string> inBase = @base is null ? [] : [@base.NotesId, .. @base.Sources.Notes.Select(static each => each.Id)];
        notes.AddRange(loser.Sources.Notes.Where(theirs => !known.Contains(theirs.Id) && !inBase.Contains(theirs.Id)));
        if (removed.Count == 0 && notes.SequenceEqual(merged.Sources.Notes))
        {
            return merged;
        }

        Project result = merged with { Sources = merged.Sources with { Notes = notes } };
        // The motifs, variations and ideas made on Notes removed go with them.
        return removed.Aggregate(result, static (project, id) => project.DeleteNotes(id));
    }

    private static Project Versions(Project merged, Project loser, Project? @base)
    {
        HashSet<string> inBase = @base is null ? [] : [.. @base.Performances.Select(static each => each.Id)];
        HashSet<string> known = [merged.NotesId, .. merged.Sources.Notes.Select(static each => each.Id), .. merged.Performances.Select(static each => each.Id)];
        foreach (SavedPerformance theirs in loser.Performances.Where(theirs => !known.Contains(theirs.Id) && !inBase.Contains(theirs.Id)))
        {
            merged = merged.AddPerformance(theirs);
        }

        if (@base is null)
        {
            return merged;
        }

        // A version removed on the other side since the base, and not changed here, goes.
        foreach (SavedPerformance mine in merged.Performances.Where(mine => loser.FindPerformance(mine.Id) is null && mine.Equals(@base.FindPerformance(mine.Id))).ToList())
        {
            merged = merged.RemovePerformance(mine.Id);
        }

        return merged;
    }

    /// <summary>
    /// Two logs of corrections of the same notes as one (0026 S9), note by note: the same corrections on both sides stay; a
    /// note corrected on one side only (since the base, when there is one) takes that side's; a note corrected differently
    /// on both is a conflict, and the winner's stand, unless with no base one side only goes further than the other. The
    /// winner's corrections come first in their order, then the other side's, in theirs.
    /// </summary>
    public static NoteEditLog Edits(NoteEditLog winner, NoteEditLog loser, NoteEditLog? @base)
    {
        if (winner.Equals(loser))
        {
            return winner;
        }

        Dictionary<string, bool> fromLoser = new(StringComparer.Ordinal);
        foreach (string id in winner.EditedIds.Concat(loser.EditedIds).Distinct(StringComparer.Ordinal))
        {
            fromLoser[id] = TakesLoser(Of(winner, id), Of(loser, id), @base is null ? null : Of(@base, id));
        }

        return new NoteEditLog([.. winner.Edits.Where(edit => !fromLoser[edit.NoteId]), .. loser.Edits.Where(edit => fromLoser[edit.NoteId])]);
    }

    /// <summary>The corrections of one note in a log, in its order.</summary>
    internal static List<NoteEdit> Of(NoteEditLog log, string id) => [.. log.Edits.Where(edit => edit.NoteId == id)];

    private static bool TakesLoser(List<NoteEdit> winner, List<NoteEdit> loser, List<NoteEdit>? @base)
    {
        if (winner.SequenceEqual(loser))
        {
            return false;
        }

        if (@base is not null)
        {
            // Only the other side changed the note since the base.
            return winner.SequenceEqual(@base);
        }

        // No base: a union with no deletions, so the side that goes further along the same corrections wins.
        return loser.Count > winner.Count && loser.Take(winner.Count).SequenceEqual(winner);
    }
}