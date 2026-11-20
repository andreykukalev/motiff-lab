using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>Where a приём stands in a place: still to decide, taken by a step, or left as in the base.</summary>
public enum TechniqueState
{
    Open,
    Taken,
    Left,
}

/// <summary>
/// One place a приём is found: a bar of the base, with what a take of it means there, the base notes it takes out and the
/// variant notes it puts in (named in the place, <see cref="DraftVariant.NoteId"/>); or, for <see cref="TechniqueKind.InsertBars"/>,
/// the variant's bars put in whole before a bar of the base.
/// </summary>
/// <param name="Bar">The place's bar, from 1, as its base has it: the bar it is in, or the one inserted bars go before (one past the last at the end).</param>
/// <param name="Span">Where it lies in the base's ticks; for inserted bars, where they go and how long they are.</param>
/// <param name="Matched">The bar's notes found in place, as 0024 (section 6) finds them; none for inserted bars, found nowhere in the base.</param>
/// <param name="Total">
/// The bar's notes counted against them: those on the side with more, the base's or the variant's, leaving out the приём's
/// own, the notes its kind explains (<see cref="BarComparison.ExplainedNotHeard"/>, <see cref="BarComparison.ExplainedPlayed"/>:
/// none for <see cref="TechniqueKind.Other"/>); for inserted bars, their notes.
/// </param>
public sealed record TechniqueOccurrence(int Bar, TickSpan Span, int Matched, int Total, IReadOnlyList<string> Removed, IReadOnlyList<string> Added, DraftInsert? Insert, TechniqueState State)
{
    /// <summary>The share of the bar's notes found in place, in whole per cent, rounded down; 100 with none to count, as an empty bar «совпадает».</summary>
    public int Confidence => Total == 0 ? 100 : Matched * 100 / Total;

    /// <summary>
    /// Sure here: the bar «совпадает» as in 0024 (<see cref="AssemblyMatch.IsSame"/>) once the приём's own notes are left
    /// aside; a bar with no other note, all of it the приём's (an octave of the whole bar), as an empty bar does.
    /// </summary>
    public bool IsSure => AssemblyMatch.IsSame(Matched, Total);
}

/// <summary>
/// A приём of a variant against its place's base (decision record 0034, section 4): the differences of one kind and one
/// shape in different bars, numbered in the place. Sure when it is sure at every occurrence (<see cref="TechniqueOccurrence.IsSure"/>);
/// inserted bars are never sure, so never taken by default.
/// </summary>
public sealed record PlaceTechnique(int Number, string Variant, TechniqueKind Kind, IReadOnlyList<TechniqueOccurrence> Occurrences)
{
    /// <summary>The lowest confidence of its occurrences.</summary>
    public int Confidence => Occurrences.Min(static occurrence => occurrence.Confidence);

    public bool IsSure => Kind != TechniqueKind.InsertBars && Occurrences.All(static occurrence => occurrence.IsSure);

    /// <summary>The occurrences still to decide: what «Взять везде · N» takes.</summary>
    public IReadOnlyList<TechniqueOccurrence> Open => [.. Occurrences.Where(static occurrence => occurrence.State == TechniqueState.Open)];

    /// <summary>Open while one occurrence is; else taken when one was taken, else left.</summary>
    public TechniqueState State =>
        Open.Count > 0 ? TechniqueState.Open
            : Occurrences.Any(static occurrence => occurrence.State == TechniqueState.Taken) ? TechniqueState.Taken
            : TechniqueState.Left;
}

/// <summary>
/// The приёмы of a place (decision record 0034, section 4): each variant that is not the base compared with the base by the
/// comparison of 0022 (section 3: each bar's differences and their kind, through the alignment of the variant with the base,
/// confirmed anchors kept), and bars of the variant found nowhere in the base and squeezed into no bars of it, which are
/// bars put in (0024's comparison: not found). Bars of one kind and the same shape (the same places in the bar of the notes
/// taken out and put in) are one приём. Numbered from 1 in the order of their first bar, then of the variants. Worked out,
/// never stored: a step keeps only its notes and the приём as a label.
/// </summary>
public static class PlaceTechniques
{
    /// <summary>The grid the shape of a difference is read on: a 16th.</summary>
    private const long ShapeGrid = CanonicalTime.TicksPerQuarter / 4;

    /// <summary>The приёмы of the place <paramref name="place"/>, decided as its <paramref name="steps"/> (its own) left them.</summary>
    public static IReadOnlyList<PlaceTechnique> Of(DraftPlace place, IReadOnlyList<DraftStep> steps)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(steps);
        HashSet<string> here = [.. PlaceResult.Build(place, steps).Music.Notes.Select(static note => note.Id)];
        SourceMusic basis = Named(place.BaseVariant);
        List<(int VariantIndex, TechniqueKind Kind, List<TechniqueOccurrence> Occurrences)> found = [];
        for (int index = 0; index < place.Variants.Count; index++)
        {
            DraftVariant variant = place.Variants[index];
            if (variant.Id == place.Base)
            {
                continue;
            }

            Decided decided = new(variant.Id, steps, here);
            found.AddRange(Compared(basis, variant, decided).Select(each => (index, each.Kind, each.Occurrences)));
        }

        return [.. found
            .OrderBy(static each => each.Occurrences[0].Bar).ThenBy(static each => each.VariantIndex).ThenBy(static each => each.Kind)
            .Select((each, number) => new PlaceTechnique(number + 1, place.Variants[each.VariantIndex].Id, each.Kind, each.Occurrences))];
    }

    /// <summary>A variant's music with its notes named as in a place.</summary>
    private static SourceMusic Named(DraftVariant variant) =>
        variant.Music with { Notes = [.. variant.Music.Notes.Select(note => note with { Id = variant.NoteId(note.Id) })] };

    /// <summary>The variant's приёмы against the base, each a kind and its occurrences in bar order.</summary>
    private static List<(TechniqueKind Kind, List<TechniqueOccurrence> Occurrences)> Compared(SourceMusic basis, DraftVariant variant, Decided decided)
    {
        SourceMusic music = Named(variant);
        AssemblyMatch match = AssemblyMatch.Of(variant.Id, music, basis, "", DraftAlignment.Confirmed(variant));
        PerformanceAlignment alignment = match.Alignment;
        List<(TickSpan Span, Tick At)> inserted = Inserted(match, basis, alignment);
        IReadOnlyList<PlayedNote> played = [.. AssemblyMatch.Played(music).Where(note => !inserted.Any(run => run.Span.Contains(new Tick(note.Onset / 1000))))];
        PerformanceComparison comparison = PerformanceComparison.Compare(basis.Notes, basis.Meter, played, alignment);
        Dictionary<long, BarComparison> byOnset = comparison.Bars.ToDictionary(bar => basis.Meter.GetBar(bar.Bar).Span.Onset.Value);
        Dictionary<string, Note> notes = basis.Notes.ToDictionary(static note => note.Id, StringComparer.Ordinal);

        List<(TechniqueKind Kind, string Shape, List<TechniqueOccurrence> Occurrences)> techniques = [];
        for (int index = 0; index < basis.Bars.Count; index++)
        {
            TickSpan span = basis.Bars[index].Span;
            if (!byOnset.TryGetValue(span.Onset.Value, out BarComparison? bar) || !bar.Aligned || bar.Difference == BarDifference.None)
            {
                continue;
            }

            TechniqueKind kind = KindOf(bar.Difference);
            // The bar's notes on each side, the notes its kind explains (the приём's own) left aside, count against the ones found
            // in place: the base's matched, not heard or probably not heard, the variant's matched, not printed or likely artefacts.
            int missed = bar.ProbablyNotHeard.Count + bar.PrintedNotHeard.Count - bar.ExplainedNotHeard.Count;
            int extra = bar.LikelyArtefacts.Count + bar.PlayedNotPrinted.Count - bar.ExplainedPlayed.Count;
            int total = bar.Matched.Count + Math.Max(missed, extra);
            // A take writes the приём's own notes; «иначе» explains none, so taking it takes all the bar's differences.
            (IReadOnlyList<string> removed, IReadOnlyList<PlacedNote> put) = kind == TechniqueKind.Other
                ? (bar.PrintedNotHeard, bar.PlayedNotPrinted)
                : (bar.ExplainedNotHeard, bar.ExplainedPlayed);
            string shape = ShapeOf(removed, put, notes, span);
            string[] added = [.. put.Select(static note => note.Id).Order(StringComparer.Ordinal)];
            var occurrence = new TechniqueOccurrence(index + 1, span, bar.Matched.Count, total, removed, added, null, decided.Of(kind, index + 1, removed, added));
            if (techniques.FindIndex(each => each.Kind == kind && each.Shape == shape) is var at and >= 0)
            {
                techniques[at].Occurrences.Add(occurrence);
            }
            else
            {
                techniques.Add((kind, shape, [occurrence]));
            }
        }

        List<(TechniqueKind Kind, List<TechniqueOccurrence> Occurrences)> result = [.. techniques.Select(static each => (each.Kind, each.Occurrences))];
        foreach ((TickSpan span, Tick at) in inserted)
        {
            int bar = basis.Bars.Count(each => each.Span.Onset < at) + 1;
            result.Add((TechniqueKind.InsertBars, [new TechniqueOccurrence(bar, new TickSpan(at, span.Duration), 0, music.NotesIn(span.Onset, span.End).Count, [], [], new DraftInsert(at, span), decided.Of(TechniqueKind.InsertBars, bar, [], []))]));
        }

        return result;
    }

    /// <summary>
    /// The runs of the variant's bars found nowhere in the base (0024's «не найдено») that the alignment squeezes into less
    /// than half their length of the base, or puts past its end: bars the variant adds. Each with the bar line of the base
    /// nearest to where it falls, where it would go.
    /// </summary>
    private static List<(TickSpan Span, Tick At)> Inserted(AssemblyMatch match, SourceMusic basis, PerformanceAlignment alignment)
    {
        List<TickSpan> runs = [];
        foreach (MatchBar bar in match.Bars.Where(static bar => bar.Kind == MatchKind.NotFound))
        {
            if (runs.Count > 0 && runs[^1].End == bar.Span.Onset)
            {
                runs[^1] = TickSpan.FromBounds(runs[^1].Onset, bar.Span.End);
            }
            else
            {
                runs.Add(bar.Span);
            }
        }

        List<(TickSpan Span, Tick At)> inserted = [];
        foreach (TickSpan span in runs)
        {
            Tick from = DraftAlignment.InBase(alignment, span.Onset);
            Tick to = DraftAlignment.InBase(alignment, span.End);
            if (from >= basis.End || (to.Value - from.Value) * 2 <= span.Duration)
            {
                inserted.Add((span, NearestBarLine(basis, from)));
            }
        }

        return inserted;
    }

    /// <summary>The bar line of <paramref name="music"/> nearest to a tick, its end among them; the earlier on a tie.</summary>
    private static Tick NearestBarLine(SourceMusic music, Tick tick) =>
        music.Bars.Select(static bar => bar.Span.Onset).Append(music.End).OrderBy(line => Math.Abs(line.Value - tick.Value)).ThenBy(static line => line.Value).First();

    /// <summary>The shape of a приём in a bar: where in the bar, on a 16th, the notes it takes out and puts in are.</summary>
    private static string ShapeOf(IReadOnlyList<string> removed, IReadOnlyList<PlacedNote> put, Dictionary<string, Note> notes, TickSpan span)
    {
        IEnumerable<long> taken = removed.Select(id => (notes[id].Span.Onset.Value - span.Onset.Value) / ShapeGrid).Order();
        IEnumerable<long> added = put.Select(note => (note.At.Value - span.Onset.Value + (ShapeGrid / 2)) / ShapeGrid).Order();
        return $"{string.Join(",", taken)}|{string.Join(",", added)}";
    }

    /// <summary>The kind of a приём for the kind of a bar's differences in 0022's comparison.</summary>
    private static TechniqueKind KindOf(BarDifference difference) => difference switch
    {
        BarDifference.AddedNotes => TechniqueKind.Fill,
        BarDifference.DifferentBass => TechniqueKind.Bass,
        BarDifference.Simplified => TechniqueKind.Simplify,
        BarDifference.Ornament => TechniqueKind.Ornament,
        BarDifference.Octave => TechniqueKind.Octave,
        BarDifference.Rhythm => TechniqueKind.Rhythm,
        _ => TechniqueKind.Other,
    };

    /// <summary>
    /// How the steps of a place left an occurrence of a variant's приём: left when a leave of the variant names its kind and
    /// bar; taken when a take does, or when its notes are already as the variant has them (a hand or a selection taken).
    /// </summary>
    private sealed class Decided(string variant, IReadOnlyList<DraftStep> steps, HashSet<string> here)
    {
        public TechniqueState Of(TechniqueKind kind, int bar, IReadOnlyList<string> removed, IReadOnlyList<string> added)
        {
            bool Names(DraftTechnique? technique) => technique is { } named && named.Kind == kind && named.Bars.Contains(bar);
            if (steps.Any(step => step is DraftLeave leave && leave.Variant == variant && Names(leave.Technique)))
            {
                return TechniqueState.Left;
            }

            bool asTheVariant = removed.Count + added.Count > 0 && removed.All(id => !here.Contains(id)) && added.All(here.Contains);
            return asTheVariant || steps.Any(step => step is DraftTake take && take.Variant == variant && Names(take.Technique))
                ? TechniqueState.Taken
                : TechniqueState.Open;
        }
    }
}