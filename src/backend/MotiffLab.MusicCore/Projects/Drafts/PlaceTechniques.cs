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
/// <param name="Confidence">
/// How sure the comparison is here, in per cent: the share of the base's notes found in place in the bar and its neighbours,
/// as 0022 (section 3) counts it, leaving out the notes the приём itself takes out, which its kind explains (all but
/// <see cref="TechniqueKind.Other"/>, which explains none).
/// </param>
public sealed record TechniqueOccurrence(int Bar, TickSpan Span, int Confidence, IReadOnlyList<string> Removed, IReadOnlyList<string> Added, DraftInsert? Insert, TechniqueState State);

/// <summary>
/// A приём of a variant against its place's base (decision record 0034, section 4): the differences of one kind and one
/// shape in different bars, numbered in the place. Sure when the comparison is at least 85 % sure everywhere it is found,
/// as «совпадает» in 0024 (<see cref="AssemblyMatch.SameShare"/>); inserted bars are never sure, so never taken by default.
/// </summary>
public sealed record PlaceTechnique(int Number, string Variant, TechniqueKind Kind, IReadOnlyList<TechniqueOccurrence> Occurrences)
{
    /// <summary>The confidence a sure приём has at least, in per cent: 85.</summary>
    public const int SureConfidence = (int)(AssemblyMatch.SameShare * 100);

    /// <summary>The lowest confidence of its occurrences.</summary>
    public int Confidence => Occurrences.Min(static occurrence => occurrence.Confidence);

    public bool IsSure => Kind != TechniqueKind.InsertBars && Confidence >= SureConfidence;

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

    /// <summary>The kind of a приём for each kind of a bar's differences in 0022's comparison; inserted bars are no bar's kind.</summary>
    private static readonly (BarDifference Difference, TechniqueKind Kind)[] s_kinds =
    [
        (BarDifference.AddedNotes, TechniqueKind.Fill),
        (BarDifference.DifferentBass, TechniqueKind.Bass),
        (BarDifference.Simplified, TechniqueKind.Simplify),
        (BarDifference.Ornament, TechniqueKind.Ornament),
        (BarDifference.Octave, TechniqueKind.Octave),
        (BarDifference.Rhythm, TechniqueKind.Rhythm),
        (BarDifference.Other, TechniqueKind.Other),
    ];

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
        Dictionary<long, int> byOnset = comparison.Bars.Select((bar, at) => (bar, at)).ToDictionary(each => basis.Meter.GetBar(each.bar.Bar).Span.Onset.Value, static each => each.at);
        Dictionary<string, Note> notes = basis.Notes.ToDictionary(static note => note.Id, StringComparer.Ordinal);

        List<(TechniqueKind Kind, string Shape, List<TechniqueOccurrence> Occurrences)> techniques = [];
        for (int index = 0; index < basis.Bars.Count; index++)
        {
            TickSpan span = basis.Bars[index].Span;
            if (!byOnset.TryGetValue(span.Onset.Value, out int compared) || comparison.Bars[compared] is not { Aligned: true } bar || bar.Difference == BarDifference.None)
            {
                continue;
            }

            TechniqueKind kind = KindOf(bar.Difference);
            string shape = ShapeOf(bar, notes, span);
            string[] added = [.. bar.PlayedNotPrinted.Select(static note => note.Id).Order(StringComparer.Ordinal)];
            var occurrence = new TechniqueOccurrence(index + 1, span, ConfidenceOf(comparison.Bars, compared, kind), bar.PrintedNotHeard, added, null, decided.Of(kind, index + 1, bar.PrintedNotHeard, added));
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

            // As sure as the base's bars on either side of where they go.
            int confidence = basis.Bars.Where(each => each.Span.Onset == at || each.Span.End == at)
                .Select(each => byOnset.TryGetValue(each.Span.Onset.Value, out int around) && comparison.Bars[around].Aligned ? comparison.Bars[around].Confidence : 0)
                .DefaultIfEmpty(0).Min();
            result.Add((TechniqueKind.InsertBars, [new TechniqueOccurrence(bar, new TickSpan(at, span.Duration), confidence, [], [], new DraftInsert(at, span), decided.Of(TechniqueKind.InsertBars, bar, [], []))]));
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

    /// <summary>
    /// The confidence of a bar's difference: the base's notes found in place over those that can be heard, in the bar and
    /// the bars on either side (0022, section 3), the bar's notes not heard that the kind explains left out.
    /// </summary>
    private static int ConfidenceOf(IReadOnlyList<BarComparison> bars, int at, TechniqueKind kind)
    {
        BarComparison bar = bars[at];
        BarComparison[] around = [.. bars.Skip(Math.Max(0, at - 1)).Take(at == 0 ? 2 : 3)];
        int heard = around.Sum(static each => each.Matched.Count);
        int hearable = around.Sum(static each => each.Matched.Count + each.PrintedNotHeard.Count) - (kind == TechniqueKind.Other ? 0 : bar.PrintedNotHeard.Count);
        return hearable <= 0 ? 0 : (int)(100L * heard / hearable);
    }

    /// <summary>The bar line of <paramref name="music"/> nearest to a tick, its end among them; the earlier on a tie.</summary>
    private static Tick NearestBarLine(SourceMusic music, Tick tick) =>
        music.Bars.Select(static bar => bar.Span.Onset).Append(music.End).OrderBy(line => Math.Abs(line.Value - tick.Value)).ThenBy(static line => line.Value).First();

    /// <summary>The shape of a bar's differences: where in the bar, on a 16th, the notes taken out and put in are.</summary>
    private static string ShapeOf(BarComparison bar, Dictionary<string, Note> notes, TickSpan span)
    {
        IEnumerable<long> removed = bar.PrintedNotHeard.Select(id => (notes[id].Span.Onset.Value - span.Onset.Value) / ShapeGrid).Order();
        IEnumerable<long> added = bar.PlayedNotPrinted.Select(note => (note.At.Value - span.Onset.Value + (ShapeGrid / 2)) / ShapeGrid).Order();
        return $"{string.Join(",", removed)}|{string.Join(",", added)}";
    }

    /// <summary>The kind of a приём in a word or two, as the comparison names a bar's differences: «другой бас», «октава»; «вставка тактов».</summary>
    public static string Name(TechniqueKind kind, Words words) =>
        kind == TechniqueKind.InsertBars ? words.T("bars put in", "вставка тактов") : BarComparison.NameOf(s_kinds.First(each => each.Kind == kind).Difference, words);

    private static TechniqueKind KindOf(BarDifference difference) => s_kinds.First(each => each.Difference == difference).Kind;
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