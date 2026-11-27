using MotiffLab.MusicCore.Analysis.Phrasing;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects;

/// <summary>
/// The phrase anchors of a project (0026 C1, #318, <see cref="Project.Anchors"/>): minted when something first attaches to a
/// phrase (<see cref="Attached(Project, Observation, PhraseMap)"/>); the player's boundary edits carry them, and what is
/// attached with them, with their phrases (<see cref="AnchorsFollowed"/>); a re-analysis never moves them: those that no
/// longer match the phrases are told (<see cref="Unmatched"/>) and attached again as a group (<see cref="Reattached"/>).
/// Pure: the same project and phrases give the same anchors.
/// </summary>
internal static class ProjectAnchors
{
    /// <summary>The anchors whose span is not one of <paramref name="phrases"/> any more: the notes or their analysis changed since.</summary>
    internal static IReadOnlyList<PhraseAnchor> Unmatched(this Project project, PhraseMap phrases)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(phrases);
        HashSet<TickSpan> spans = [.. phrases.Phrases.Select(static phrase => phrase.Span)];
        return [.. project.Anchors.Where(anchor => !spans.Contains(anchor.Span))];
    }

    /// <summary>
    /// <paramref name="observation"/> attached to the anchor of its phrase among <paramref name="phrases"/>
    /// (<see cref="Observation.Home"/>), with the project holding that anchor, minted when the phrase has none; as it is
    /// when its passage overlaps no phrase.
    /// </summary>
    internal static (Project Project, Observation Observation) Attached(this Project project, Observation observation, PhraseMap phrases)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(phrases);
        if (observation.Home(phrases.Phrases.Select(static phrase => phrase.Span)) is not { } home)
        {
            return (project, observation with { Anchor = null });
        }

        (Project anchored, string id) = project.AnchorOf(home);
        return (anchored, observation with { Anchor = id });
    }

    /// <summary>
    /// <paramref name="point"/> attached to the anchor of the phrase its passage is (<see cref="PhraseMap.Matching"/>) while
    /// it is open in phrase focus, with the project holding that anchor; attached to none otherwise.
    /// </summary>
    internal static (Project Project, ResumePoint Point) Attached(this Project project, ResumePoint point, PhraseMap phrases)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(phrases);
        if (!point.Focused || point.Passage is not { } passage || phrases.Matching(passage.Span) is not { } phrase)
        {
            return (project, point with { Anchor = null });
        }

        (Project anchored, string id) = project.AnchorOf(phrase.Span);
        return (anchored, point with { Anchor = id });
    }

    /// <summary>
    /// The project with each anchor carried by the player's boundary edit from <paramref name="before"/> to
    /// <paramref name="after"/> (ADR 0026 scenario 4): an anchor that was a phrase follows its start. A start kept stays;
    /// a start taken away where one was set between the same boundaries either side moves there (a mark dragged, a found
    /// boundary put back, and their Undo and Redo); a start taken away otherwise joins the anchor to the phrase that holds it
    /// now. A split leaves it on its first part. An anchor that matched no phrase before stays as it is.
    /// </summary>
    internal static Project AnchorsFollowed(this Project project, PhraseMap before, PhraseMap after)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        Tick[] was = [.. before.Phrases.Select(static phrase => phrase.Span.Onset)];
        Tick[] now = [.. after.Phrases.Select(static phrase => phrase.Span.Onset)];
        var spans = new Dictionary<string, TickSpan>(StringComparer.Ordinal);
        foreach (PhraseAnchor anchor in project.Anchors)
        {
            if (before.Phrases.FirstOrDefault(phrase => phrase.Span == anchor.Span) is not { } phrase)
            {
                continue;
            }

            Tick start = Array.BinarySearch(now, phrase.Span.Onset) >= 0 ? phrase.Span.Onset : MovedTo(phrase.Span.Onset, was, now, after.End) ?? phrase.Span.Onset;
            spans[anchor.Id] = after.At(start).Span;
        }

        return project.WithAnchorsAt(spans, after);
    }

    /// <summary>
    /// The project with every anchor that no longer matches <paramref name="phrases"/> attached to the phrase it belongs to
    /// now, by the rule an idea's phrase is found (<see cref="PhraseMap.HomeOf"/>), all as one step; and how many were.
    /// </summary>
    internal static (Project Project, int Reattached) Reattached(this Project project, PhraseMap phrases)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        TickSpan[] all = [.. phrases.Phrases.Select(static phrase => phrase.Span)];
        var spans = new Dictionary<string, TickSpan>(StringComparer.Ordinal);
        foreach (PhraseAnchor anchor in project.Unmatched(phrases))
        {
            if (PhraseMap.HomeOf(anchor.Span, all) is { } home)
            {
                spans[anchor.Id] = home;
            }
        }

        return (project.WithAnchorsAt(spans, phrases), spans.Count);
    }

    /// <summary>The anchor of the phrase <paramref name="phrase"/>, and the project holding it: the one there, or one minted now.</summary>
    private static (Project Project, string Id) AnchorOf(this Project project, TickSpan phrase)
    {
        IReadOnlyList<PhraseAnchor> anchors = project.Anchors;
        if (anchors.FirstOrDefault(anchor => anchor.Span == phrase) is { } kept)
        {
            return (project, kept.Id);
        }

        PhraseAnchor minted = PhraseAnchor.Mint(phrase, anchors.Select(static anchor => anchor.Id));
        return (project with { MintedAnchors = [.. anchors, minted] }, minted.Id);
    }

    /// <summary>
    /// The start <paramref name="from"/>, taken away, moved to where the edit set the one start between the same kept starts
    /// either side, when it took no other start away there; null otherwise.
    /// </summary>
    private static Tick? MovedTo(Tick from, Tick[] was, Tick[] now, Tick end)
    {
        Tick[] kept = [.. was.Intersect(now)];
        Tick low = kept.Where(tick => tick < from).DefaultIfEmpty(Tick.Zero).Max();
        Tick high = kept.Where(tick => tick > from).DefaultIfEmpty(end).Min();
        bool Between(Tick tick) => tick > low && tick < high;
        Tick[] set = [.. now.Except(was).Where(Between)];
        return set.Length == 1 && was.Except(now).Count(Between) == 1 ? set[0] : null;
    }

    /// <summary>
    /// What the anchors are now (<see cref="AnchorState"/>), so an Undo or Redo of a phrases step can put them back exactly.
    /// </summary>
    internal static AnchorState AnchorState(this Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new(
            project.Anchors,
            project.Observations.ToDictionary(static observation => observation.Id, static observation => observation.Anchor, StringComparer.Ordinal),
            project.ResumePoints.ToDictionary(static point => point.Id, static point => point.Anchor, StringComparer.Ordinal));
    }

    /// <summary>
    /// The project with the anchors as <paramref name="state"/> holds them (an Undo or Redo of a phrases step): its anchors
    /// back at their spans and each idea and resume point it names on its anchor again; an anchor minted since, and what
    /// was kept since, as they are. A resume point in phrase focus goes with its anchor (<see cref="Settled"/>).
    /// </summary>
    internal static Project WithAnchorState(this Project project, AnchorState state, PhraseMap phrases)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(phrases);
        HashSet<string> back = [.. state.Anchors.Select(static anchor => anchor.Id)];
        return project.Settled(
            [.. state.Anchors, .. project.Anchors.Where(anchor => !back.Contains(anchor.Id))],
            [.. project.Observations.Select(observation => state.Ideas.TryGetValue(observation.Id, out string? anchor) ? observation with { Anchor = anchor } : observation)],
            [.. project.ResumePoints.Select(point => state.Points.TryGetValue(point.Id, out string? anchor) ? point with { Anchor = anchor } : point)],
            phrases);
    }

    /// <summary>The project with the anchors of <paramref name="spans"/> at those spans of <paramref name="phrases"/> (<see cref="Settled"/>).</summary>
    private static Project WithAnchorsAt(this Project project, IReadOnlyDictionary<string, TickSpan> spans, PhraseMap phrases) =>
        spans.Count == 0 ? project : project.Settled(
            [.. project.Anchors.Select(anchor => spans.TryGetValue(anchor.Id, out TickSpan span) ? anchor with { Span = span } : anchor)],
            project.Observations,
            project.ResumePoints,
            phrases);

    /// <summary>
    /// The project with <paramref name="anchors"/>, <paramref name="ideas"/> and <paramref name="points"/>: two anchors on
    /// one span made one, the smaller id kept (0026 «Open», the council's proposal; deterministic, so two devices agree) and
    /// what was attached to the other attached to it; and a resume point open in phrase focus whose anchor is at another
    /// span than its anchor had in <paramref name="project"/> at its phrase now, as it is selected.
    /// </summary>
    private static Project Settled(this Project project, IReadOnlyList<PhraseAnchor> anchors, IReadOnlyList<Observation> ideas, IReadOnlyList<ResumePoint> points, PhraseMap phrases)
    {
        Dictionary<string, string> into = anchors
            .GroupBy(static anchor => anchor.Span)
            .SelectMany(static same => same.Select(anchor => (anchor.Id, Kept: same.Select(static other => other.Id).Min(StringComparer.Ordinal)!)))
            .ToDictionary(static pair => pair.Id, static pair => pair.Kept, StringComparer.Ordinal);
        string? Into(string? id) => id is not null && into.TryGetValue(id, out string? kept) ? kept : id;
        Dictionary<string, TickSpan> was = project.Anchors.ToDictionary(static anchor => anchor.Id, static anchor => anchor.Span, StringComparer.Ordinal);
        Dictionary<string, TickSpan> now = anchors.ToDictionary(static anchor => anchor.Id, static anchor => anchor.Span, StringComparer.Ordinal);
        TickSpan? Span(Dictionary<string, TickSpan> spans, string? id) => id is not null && spans.TryGetValue(id, out TickSpan span) ? span : null;
        return project with
        {
            MintedAnchors = [.. anchors.Where(anchor => into[anchor.Id] == anchor.Id)],
            Observations = [.. ideas.Select(observation => observation with { Anchor = Into(observation.Anchor) })],
            ResumePoints = [.. points.Select(point => Carried(point, Span(was, project.FindResumePoint(point.Id)?.Anchor), Span(now, point.Anchor), phrases) with { Anchor = Into(point.Anchor) })],
        };
    }

    /// <summary>A resume point open in phrase focus whose anchor went from <paramref name="was"/> to the phrase <paramref name="now"/>, at that phrase as it is selected; its place with it when it was at the passage.</summary>
    private static ResumePoint Carried(ResumePoint point, TickSpan? was, TickSpan? now, PhraseMap phrases)
    {
        if (!point.Focused || point.Passage is not { } passage || now is not { } span || span == was
            || phrases.Phrases.FirstOrDefault(phrase => phrase.Span == span) is not { } phrase)
        {
            return point;
        }

        TickSpan selected = phrases.Selectable(phrase);
        return point with { Passage = passage with { Span = selected }, At = point.At == passage.Span.Onset ? selected.Onset : point.At };
    }
}