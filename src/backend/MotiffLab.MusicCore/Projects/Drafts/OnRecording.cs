using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// «Найти на записи» in a place's card (spec «Ноты + запись»: the engine finds the stretch of the recording): the whole bars
/// of the notes heard in a recording that are the music of a place's base, so they go in as a variant of the place, as a
/// selection in the recording's view goes «Вариантом к месту». Measured in notes, never in the recording's bars, whose grid
/// may be finer or coarser than the base's: first the runs of as many notes as the base has that share most of its pitches,
/// part for part; around the best of them the comparison of 0024 (the alignment of 0022, then each note looked for at its
/// place), whose bars found there in one run bound the stretch; then how much of the stretch is found in the base, the
/// same music as the «В черновик» sheet decides it (<see cref="SameMusic.In"/>). Pure and in integers: the same place and
/// recording give the same stretch.
/// </summary>
public static class OnRecording
{
    /// <summary>How many runs of notes, each mostly outside the stretches compared before it, are compared note by note.</summary>
    public const int Candidates = 3;

    /// <summary>How many parts the base's notes are cut into, in order, for a run to share their pitches part for part.</summary>
    public const int Parts = 8;

    /// <summary>
    /// The stretch of <paramref name="recording"/>, in its ticks and whole bars, that is the music of the base of
    /// <paramref name="place"/>, and how much of it is found there; null when no stretch is the same music (at least half
    /// of its notes found in the base, <see cref="PlaceMatch.IsSameMusic"/>). A recording may add, drop or stretch bars, so
    /// each run is compared with two of the base's bars more on either side, and with a quarter of its notes more on a side
    /// where the music found reaches the edge of what was compared; the stretch is the run of bars
    /// found where most of the base's notes are found, a gap in it no longer than two of the base's bars hold notes (bars
    /// the recording adds); ties go to the earlier.
    /// </summary>
    public static (TickSpan Span, PlaceMatch Match)? Find(DraftPlace place, SourceMusic recording)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(recording);
        Note[] ours = Ordered(place.BaseVariant.Music.Notes), theirs = Ordered(recording.Notes);
        if (ours.Length == 0 || theirs.Length == 0 || recording.Bars.Count == 0)
        {
            return null;
        }

        int margin = Math.Max(1, ours.Length / 4);
        int gap = Math.Max(1, 2 * ours.Length / Math.Max(1, place.BaseVariant.Music.Bars.Count));
        TickSpan Window(int from, int to) =>
            TickSpan.FromBounds(recording.BarAt(theirs[from].Span.Onset).Bar.Span.Onset, recording.BarAt(theirs[to - 1].Span.Onset).Bar.Span.End);
        List<(int From, int To)> compared = [];
        (TickSpan Span, int Matched)? best = null;
        foreach (int start in Starts(ours, theirs))
        {
            if (compared.Count == Candidates)
            {
                break;
            }

            // A run that lies mostly in a stretch compared already is the same music found again.
            if (compared.Any(window => 2L * (Math.Min(window.To, start + ours.Length) - Math.Max(window.From, start)) > ours.Length))
            {
                continue;
            }

            // First as many notes as the base has and two of its bars either side; where the music found reaches an edge,
            // that side again with the margin. Other music aligned by chance far from the place would else pull its notes away.
            (int from, int to) = (Math.Max(0, start - gap), Math.Min(theirs.Length, start + ours.Length + gap));
            TickSpan window = Window(from, to);
            (TickSpan Span, int Matched)? found = Bounded(place, recording, window, gap);
            if (found is { Span: var near } && ((near.Onset == window.Onset && from > 0) || (near.End == window.End && to < theirs.Length)))
            {
                (from, to) = (near.Onset == window.Onset ? Math.Max(0, from - margin) : from, near.End == window.End ? Math.Min(theirs.Length, to + margin) : to);
                found = Bounded(place, recording, Window(from, to), gap);
            }

            compared.Add((from, to));
            if (found is { } candidate && (best is not { } kept || candidate.Matched > kept.Matched || (candidate.Matched == kept.Matched && candidate.Span.Onset < kept.Span.Onset)))
            {
                best = candidate;
            }
        }

        if (best is not { Span: var span })
        {
            return null;
        }

        PlaceMatch match = SameMusic.In(place, DraftFragment.Of(recording, span, DraftPart.All));
        return match.IsSameMusic ? (span, match) : null;
    }

    /// <summary>
    /// The first notes (indices into <paramref name="theirs"/>) of the runs of as many notes as <paramref name="ours"/> that
    /// share most pitches with them, part for part (<see cref="Parts"/>), best first, the earlier on a tie; none that shares
    /// none. Counted as the run slides, a note into and out of each part.
    /// </summary>
    private static IEnumerable<int> Starts(Note[] ours, Note[] theirs)
    {
        int parts = Math.Min(Parts, ours.Length), length = ours.Length;
        int Bound(int part) => part * length / parts;
        int[,] wanted = new int[parts, 128], held = new int[parts, 128];
        for (int part = 0; part < parts; part++)
        {
            for (int index = Bound(part); index < Bound(part + 1); index++)
            {
                wanted[part, ours[index].Pitch.MidiNumber]++;
            }
        }

        int shared = 0;
        void Enter(int part, int at)
        {
            if (at < theirs.Length)
            {
                int pitch = theirs[at].Pitch.MidiNumber;
                shared += held[part, pitch]++ < wanted[part, pitch] ? 1 : 0;
            }
        }

        void Leave(int part, int at)
        {
            if (at < theirs.Length)
            {
                int pitch = theirs[at].Pitch.MidiNumber;
                shared -= --held[part, pitch] < wanted[part, pitch] ? 1 : 0;
            }
        }

        for (int part = 0; part < parts; part++)
        {
            for (int index = Bound(part); index < Bound(part + 1); index++)
            {
                Enter(part, index);
            }
        }

        List<(int Start, int Shared)> starts = [];
        for (int start = 0; start < Math.Max(1, theirs.Length - length + 1); start++)
        {
            if (start > 0)
            {
                for (int part = 0; part < parts; part++)
                {
                    Leave(part, start - 1 + Bound(part));
                    Enter(part, start - 1 + Bound(part + 1));
                }
            }

            if (shared > 0)
            {
                starts.Add((start, shared));
            }
        }

        return starts.OrderByDescending(static each => each.Shared).ThenBy(static each => each.Start).Select(static each => each.Start);
    }

    /// <summary>
    /// In <paramref name="window"/> of the recording, the run of bars the comparison finds in the base at least «частично»
    /// (<see cref="MatchKind.Partly"/>) whose found notes are most, a gap between two found bars holding at most
    /// <paramref name="gap"/> notes (bars the recording adds), so a bar that only happens to match apart from it stays out;
    /// the run from its first to its last found bar and how many notes were found in it; null when no bar is found.
    /// </summary>
    private static (TickSpan Span, int Matched)? Bounded(DraftPlace place, SourceMusic recording, TickSpan window, int gap)
    {
        if (recording.NotesIn(window.Onset, window.End).Count == 0)
        {
            return null;
        }

        SourceMusic part = DraftFragment.Of(recording, window, DraftPart.All);
        (TickSpan Span, int Matched)? best = null, run = null;
        long between = 0;
        foreach (MatchBar bar in AssemblyMatch.Of("recording", part, place.BaseVariant.Music, "").Bars)
        {
            if (bar.Total == 0 || bar.Kind is not (MatchKind.Same or MatchKind.Partly))
            {
                between += part.NotesIn(bar.Span.Onset, bar.Span.End).Count;
                continue;
            }

            TickSpan span = new(bar.Span.Onset.Offset(window.Onset.Value), bar.Span.Duration);
            run = run is { } open && between <= gap ? (TickSpan.FromBounds(open.Span.Onset, span.End), open.Matched + bar.Matched) : (span, bar.Matched);
            between = 0;
            if (best is not { } kept || run.Value.Matched > kept.Matched)
            {
                best = run;
            }
        }

        return best;
    }

    /// <summary>The notes by onset, then pitch, as a run of notes counts them.</summary>
    private static Note[] Ordered(IReadOnlyList<Note> notes) =>
        [.. notes.OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Pitch.MidiNumber).ThenBy(static note => note.Id, StringComparer.Ordinal)];
}