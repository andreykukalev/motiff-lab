using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// «Найти на записи» in a place's card (spec «Ноты + запись»: the engine finds the stretch of the recording): the whole bars
/// of the notes heard in a recording that are the music of a place's base, so they go in as a variant of the place, as a
/// selection in the recording's view goes «Вариантом к месту». First the starts whose bars hold most of the base's pitches
/// bar for bar; around the best of them the comparison of 0024 (the alignment of 0022, then each note looked for at its
/// place), whose bars found there bound the stretch; then the stretch where most of its notes are found in the base,
/// the same music as the «В черновик» sheet decides it (<see cref="SameMusic.In"/>). Pure and in integers: the same place
/// and recording give the same stretch.
/// </summary>
public static class OnRecording
{
    /// <summary>How many of the best starts by bars are compared note by note.</summary>
    public const int Candidates = 3;

    /// <summary>
    /// The stretch of <paramref name="recording"/>, in its ticks and whole bars, that is the music of the base of
    /// <paramref name="place"/>, and how much of it is found there; null when no stretch is the same music (at least half
    /// of its notes found in the base, <see cref="PlaceMatch.IsSameMusic"/>). A recording may add, drop or stretch bars, so
    /// each start is compared with a quarter of the base's bars more on either side (a bar at least). Ties go to the
    /// stretch with more of the base's notes found, then the higher share, then the earlier one.
    /// </summary>
    public static (TickSpan Span, PlaceMatch Match)? Find(DraftPlace place, SourceMusic recording)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(recording);
        SourceMusic music = place.BaseVariant.Music;
        int length = music.Bars.Count, bars = recording.Bars.Count;
        if (length == 0 || bars == 0)
        {
            return null;
        }

        int[][] ours = PitchesByBar(music), theirs = PitchesByBar(recording);
        int margin = Math.Max(1, length / 4);
        (TickSpan Span, PlaceMatch Match)? best = null;
        HashSet<TickSpan> compared = [];
        foreach (int start in Starts(ours, theirs))
        {
            // Near the recording's ends, or with a place as long as it, two starts may give one window: it is compared once.
            TickSpan window = TickSpan.FromBounds(recording.Bars[Math.Max(0, start - margin)].Span.Onset, recording.Bars[Math.Min(bars, start + length + margin) - 1].Span.End);
            if (!compared.Add(window) || Bounded(place, recording, window) is not { } stretch || recording.NotesIn(stretch.Onset, stretch.End).Count == 0)
            {
                continue;
            }

            PlaceMatch match = SameMusic.In(place, DraftFragment.Of(recording, stretch, DraftPart.All));
            if (best is not { } kept || Better(match, stretch, kept.Match, kept.Span))
            {
                best = (stretch, match);
            }
        }

        return best is { Match.IsSameMusic: true } found ? found : null;
    }

    /// <summary>The starts (bar indices of the recording) whose bars, one for one with the base's, share most pitches with them, best first; none that shares none.</summary>
    private static IEnumerable<int> Starts(int[][] ours, int[][] theirs) =>
        Enumerable.Range(0, Math.Max(1, theirs.Length - ours.Length + 1))
            .Select(start => (Start: start, Shared: ours.Select((bar, index) => start + index < theirs.Length ? Shared(bar, theirs[start + index]) : 0).Sum()))
            .Where(static each => each.Shared > 0)
            .OrderByDescending(static each => each.Shared)
            .ThenBy(static each => each.Start)
            .Take(Candidates)
            .Select(static each => each.Start);

    /// <summary>
    /// The bars of <paramref name="window"/> of the recording from the first to the last the comparison finds in the base at
    /// least «частично» (<see cref="MatchKind.Partly"/>), so a bar that only shares a note at the edge stays out and a bar the
    /// recording adds inside stays in; null when none.
    /// </summary>
    private static TickSpan? Bounded(DraftPlace place, SourceMusic recording, TickSpan window)
    {
        if (recording.NotesIn(window.Onset, window.End).Count == 0)
        {
            return null;
        }

        MatchBar[] found = [.. AssemblyMatch.Of("recording", DraftFragment.Of(recording, window, DraftPart.All), place.BaseVariant.Music, "").Bars
            .Where(static bar => bar.Total > 0 && bar.Kind is MatchKind.Same or MatchKind.Partly)];
        return found.Length == 0 ? null : TickSpan.FromBounds(found[0].Span.Onset.Offset(window.Onset.Value), found[^1].Span.End.Offset(window.Onset.Value));
    }

    private static bool Better(PlaceMatch match, TickSpan span, PlaceMatch kept, TickSpan keptSpan) =>
        match.Matched != kept.Matched ? match.Matched > kept.Matched
            : match.Matched * (long)kept.Total != kept.Matched * (long)match.Total ? match.Matched * (long)kept.Total > kept.Matched * (long)match.Total
            : span.Onset < keptSpan.Onset;

    /// <summary>The MIDI numbers of the notes that start in each bar, in order.</summary>
    private static int[][] PitchesByBar(SourceMusic music) =>
        [.. music.Bars.Select(bar => music.NotesIn(bar.Span.Onset, bar.Span.End).Select(static note => note.Pitch.MidiNumber).Order().ToArray())];

    /// <summary>How many pitches two bars share, each note counted once (both in order).</summary>
    private static int Shared(int[] one, int[] other)
    {
        int shared = 0;
        for (int i = 0, j = 0; i < one.Length && j < other.Length;)
        {
            if (one[i] == other[j])
            {
                shared++;
                i++;
                j++;
            }
            else if (one[i] < other[j])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return shared;
    }
}