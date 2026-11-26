using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// A place made longer or shorter in bars (version 1.4, Andrei's answer of 2026-11-26): its base cut again from the Notes it
/// was copied from over the new span, the same variant and part (<see cref="DraftFragment.Variant"/>), so the project file
/// stays as it is. What the place's journal and its other variants hold in the base's ticks or bars moves with its start;
/// what falls outside the new span goes. The length is the draft's structure, not a step. Pure: it gives a new draft.
/// </summary>
internal static class PlaceLength
{
    /// <summary>
    /// Why the base of <paramref name="place"/> cannot be cut again, whatever its source: a bridge place, whose one bar is
    /// a candidate, or a base music-core made (<see cref="NotesProvenance.Variation"/>), which was cut from nothing; null
    /// when it can be.
    /// </summary>
    public static string? Refusal(DraftPlace place, Words words)
    {
        ArgumentNullException.ThrowIfNull(place);
        return place.Bridge ? words.T("A bridge is one bar made for its seam: its length does not change.", "Связка — один такт для своего стыка, её длина не меняется.")
            : place.BaseVariant.Provenance.Op == NotesProvenance.Variation ? words.T("The base was made by the app, not cut from notes: choose another base first.", "Основу сделало приложение, она не вырезана из нот: сначала выберите другую основу.")
            : null;
    }

    /// <summary>
    /// The place <paramref name="placeId"/> with <paramref name="cut"/> as its base: the base cut again over another span of
    /// the same Notes, part and id. The place's steps follow its start: a step whose notes, bars or bars put in are outside
    /// the new span goes, the bars of the others are numbered again from the new start; the seam kept as it is («Оставить»)
    /// before the place goes when its start moved, the one after it (a step of the next place) when its end moved, as with a
    /// move (<see cref="Draft.WithPlaceAt"/>), and so does a bridge on a seam whose edge moved, with its steps. Each other variant keeps its anchors that fall inside the new base, moved with
    /// its start, and is lined up with the new base anew.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place.</exception>
    /// <exception cref="InvalidOperationException">A place whose base is not cut again (<see cref="Refusal"/>).</exception>
    /// <exception cref="ArgumentException">A cut that is not the base's, or over the span the base has already.</exception>
    public static Draft Resized(Draft draft, string placeId, DraftVariant cut)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(cut);
        DraftPlace place = draft.FindPlace(placeId) ?? throw new KeyNotFoundException($"The draft has no place {placeId}.");
        if (Refusal(place, Words.English) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        DraftVariant was = place.BaseVariant;
        if (cut.Id != was.Id || cut.From.Notes != was.From.Notes || cut.From.Project != was.From.Project || cut.From.Part != was.From.Part)
        {
            throw new ArgumentException("The base is cut again from its own Notes, as the same variant and part.", nameof(cut));
        }

        if (cut.From.Span == was.From.Span)
        {
            throw new ArgumentException("The place is that length already.", nameof(cut));
        }

        var moved = new Moved(place, cut);
        int index = draft.Places.ToList().IndexOf(place);
        string? before = cut.From.Span.Onset != was.From.Span.Onset && index > 0 ? draft.Places[index - 1].Id : null;
        string? next = cut.From.Span.End != was.From.Span.End && index + 1 < draft.Places.Count ? draft.Places[index + 1].Id : null;
        DraftVariant[] variants = [.. place.Variants.Select(variant => variant.Id == was.Id ? cut : variant with { Anchors = [.. moved.Anchors(variant.Anchors)] })];
        Draft resized = draft.WithPlace(place.With(variants, was.Id), Steps(draft, placeId, next, moved));
        return WithoutBridge(WithoutBridge(resized, next), before);
    }

    /// <summary>
    /// <paramref name="draft"/> without the place <paramref name="placeId"/> when it is a bridge, built for a seam whose
    /// edge moved (the lead's decision of 2026-11-26, as with a move): its steps go with it; as it was otherwise.
    /// </summary>
    private static Draft WithoutBridge(Draft draft, string? placeId) =>
        placeId is not null && draft.FindPlace(placeId) is { Bridge: true } ? draft.WithoutPlace(placeId) : draft;

    /// <summary>
    /// The draft's journal with the place's steps read over its new base (<see cref="Moved.Step"/>), and without the seam
    /// kept as it is before the place <paramref name="next"/>, the one after it, when its end moved (null otherwise).
    /// </summary>
    private static List<DraftStep> Steps(Draft draft, string placeId, string? next, Moved moved)
    {
        List<DraftStep> steps = [];
        long inserted = 0;
        foreach (DraftStep step in draft.Steps.OrderBy(static step => step.Number))
        {
            if (step.Place != placeId)
            {
                if (!(step.Place == next && step is DraftLeave { Seam: true }))
                {
                    steps.Add(step);
                }
            }
            else if (moved.Step(step, inserted) is { } kept)
            {
                steps.Add(kept);
                inserted += kept is DraftTake { Insert: { } insert } ? insert.Span.Duration : 0;
            }
        }

        return steps;
    }

    /// <summary>The ticks and bars of a place's base as they are in the base cut again: moved back by how far its start moved.</summary>
    private sealed class Moved(DraftPlace place, DraftVariant cut)
    {
        private readonly SourceMusic _was = place.BaseVariant.Music;
        private readonly SourceMusic _now = cut.Music;
        private readonly long _delta = cut.From.Span.Onset.Value - place.BaseVariant.From.Span.Onset.Value;
        private readonly HashSet<string> _notes = [.. cut.Music.Notes.Select(note => cut.NoteId(note.Id))];
        private readonly Dictionary<string, PerformanceAlignment> _alignments = new(StringComparer.Ordinal);

        /// <summary>
        /// The step as it reads over the new base, its bars numbered from the new start; null when what it names is outside:
        /// a base note it takes out, a variant note it puts where the new base has no tick, bars it puts in where the place has
        /// no bar line, or a bar of its приём; and the seam kept before a place whose start moved.
        /// </summary>
        /// <param name="inserted">How long the bars put in by the steps kept before it are: where bars can still go after the end.</param>
        public DraftStep? Step(DraftStep step, long inserted) => step switch
        {
            DraftLeave { Seam: true } => _delta == 0 ? step : null,
            DraftLeave leave => Bars(leave.Technique) is var (fits, technique) && fits ? leave with { Technique = technique } : null,
            DraftTake take => Take(take, inserted),
            _ => step,
        };

        /// <summary>The anchors of another variant that fall inside the new base, moved with its start.</summary>
        public IEnumerable<DraftAnchor> Anchors(IEnumerable<DraftAnchor> anchors) =>
            anchors.Where(anchor => Inside(anchor.Place)).Select(anchor => anchor with { Place = new Tick(anchor.Place.Value - _delta) });

        private DraftTake? Take(DraftTake take, long inserted)
        {
            if (Bars(take.Technique) is not (true, var technique))
            {
                return null;
            }

            if (take.Insert is { } insert)
            {
                long at = insert.At.Value - _delta;
                return at >= 0 && at <= _now.End.Value + inserted ? take with { Technique = technique, Insert = insert with { At = new Tick(at) } } : null;
            }

            DraftVariant variant = place.Find(take.Variant)!;
            Dictionary<string, Tick> onsets = variant.Music.Notes.ToDictionary(note => variant.NoteId(note.Id), static note => note.Span.Onset, StringComparer.Ordinal);
            bool inside = take.Removed.All(_notes.Contains) && take.Added.All(id => onsets.TryGetValue(id, out Tick onset) && Inside(DraftAlignment.InBase(AlignmentOf(variant), onset)));
            return inside ? take with { Technique = technique } : null;
        }

        /// <summary>Whether a tick of the base as it was is inside the new base.</summary>
        private bool Inside(Tick tick) => tick.Value - _delta >= 0 && tick.Value - _delta < _now.End.Value;

        /// <summary>The приём's bars numbered from the new start, and whether all of them are there; true with none to number.</summary>
        private (bool Fits, DraftTechnique? Technique) Bars(DraftTechnique? technique)
        {
            if (technique is null)
            {
                return (true, null);
            }

            int[] bars = [.. technique.Bars.Select(Bar).OfType<int>()];
            return bars.Length == technique.Bars.Count ? (true, new DraftTechnique(technique.Kind, bars)) : (false, null);
        }

        /// <summary>A bar of the place (from 1, one past the last for the end) as numbered in the new base; null when it is not there.</summary>
        private int? Bar(int bar)
        {
            if (bar < 1 || bar > _was.Bars.Count + 1)
            {
                return null;
            }

            long at = (bar <= _was.Bars.Count ? _was.Bars[bar - 1].Span.Onset.Value : _was.End.Value) - _delta;
            int index = _now.Bars.ToList().FindIndex(each => each.Span.Onset.Value == at);
            return index >= 0 ? index + 1 : at == _now.End.Value ? _now.Bars.Count + 1 : null;
        }

        private PerformanceAlignment AlignmentOf(DraftVariant variant) =>
            _alignments.TryGetValue(variant.Id, out PerformanceAlignment? alignment) ? alignment : _alignments[variant.Id] = DraftAlignment.Of(place, variant);
    }
}