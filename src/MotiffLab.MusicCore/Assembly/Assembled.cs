using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Playback;
using MotiffLab.MusicCore.Projects;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Assembly;

/// <summary>What happened to one step when the player's notes were rebuilt.</summary>
/// <param name="Applied">False when the step lost its footing: a note it holds on to is gone, or its source is.</param>
/// <param name="Lost">The note or source it could not find, when not applied.</param>
/// <param name="Put">The notes the step put in, by id.</param>
/// <param name="Removed">The notes it took out, by id, with the step that had put each in (0 for the base).</param>
/// <param name="Changed">The notes it changed in place (a note change or a transposition).</param>
/// <param name="Shortened">The notes it shortened at its seam.</param>
/// <param name="Delta">How much longer the music became, in ticks (negative when shorter).</param>
/// <param name="Span">Where the step's music lies right after it was applied.</param>
/// <param name="Leans">The steps whose notes this step holds on to or changes: it depends on them.</param>
public sealed record StepOutcome(
    int Number,
    bool Applied,
    string? Lost,
    IReadOnlyList<string> Put,
    IReadOnlyList<(string Id, int From)> Removed,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Shortened,
    long Delta,
    TickSpan? Span,
    IReadOnlyList<int> Leans);

/// <summary>
/// The player's notes rebuilt from the base by the journal (decision record 0024, section 4): deterministic, so the
/// same base, sources and steps always give the same notes, bars and outcomes.
/// </summary>
/// <param name="Music">The notes, bars, tempo and keys.</param>
/// <param name="Origins">For each note a step put in, that step's number; notes of the base are not here.</param>
/// <param name="Steps">Each step's outcome, in journal order.</param>
public sealed record Assembled(SourceMusic Music, IReadOnlyDictionary<string, int> Origins, IReadOnlyList<StepOutcome> Steps)
{
    /// <summary>The step that put a note in, or 0 for a note of the base.</summary>
    public int OriginOf(string noteId) => Origins.GetValueOrDefault(noteId);

    public StepOutcome? OutcomeOf(int number) => Steps.FirstOrDefault(step => step.Number == number);

    /// <summary>Rebuilds the player's notes from <paramref name="baseMusic"/> by <paramref name="steps"/>, reading carried fragments from <paramref name="sources"/>.</summary>
    public static Assembled Build(SourceMusic baseMusic, Func<string, SourceMusic?> sources, IEnumerable<AssemblyStep> steps)
    {
        ArgumentNullException.ThrowIfNull(baseMusic);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(steps);
        Builder builder = new(baseMusic, sources);
        foreach (AssemblyStep step in steps)
        {
            builder.Apply(step);
        }

        return builder.Result();
    }

    private sealed class Builder(SourceMusic baseMusic, Func<string, SourceMusic?> sources)
    {
        private List<Note> _notes = [.. baseMusic.Notes];
        private IReadOnlyList<AssemblyBar> _bars = baseMusic.Bars;
        private List<PlaybackTempo> _tempos = [.. baseMusic.Tempos];
        private List<KeySignature> _keys = [.. baseMusic.Keys];
        private readonly Dictionary<string, int> _origins = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _touched = new(StringComparer.Ordinal);
        private readonly List<StepOutcome> _outcomes = [];
        private readonly Dictionary<string, SourceMusic?> _sources = new(StringComparer.Ordinal);

        public void Apply(AssemblyStep step)
        {
            switch (step)
            {
                case CarryStep carry:
                    Carry(carry);
                    break;
                case NoteStep change:
                    ChangeNote(change);
                    break;
                case TransposeStep transpose:
                    Transpose(transpose);
                    break;
                default:
                    throw new ArgumentException($"Unknown step {step.GetType().Name}.", nameof(step));
            }
        }

        public Assembled Result()
        {
            Tick end = _bars.Count > 0 ? _bars[^1].Span.End : Tick.Zero;
            long last = _notes.Count == 0 ? 0 : _notes.Max(note => note.Span.End.Value);
            IReadOnlyList<AssemblyBar> bars = _bars;
            if (last > end.Value)
            {
                TimeSignature signature = bars.Count > 0 ? bars[^1].Signature : TimeSignature.CommonTime;
                List<AssemblyBar> more = [.. bars];
                for (Tick at = end; at.Value < last; at = at.Offset(signature.BarTicks))
                {
                    more.Add(new AssemblyBar(new TickSpan(at, signature.BarTicks), signature));
                }

                bars = more;
            }

            SourceMusic music = new(
                [.. _notes.OrderBy(note => note.Span.Onset).ThenBy(note => note.Pitch.MidiNumber).ThenBy(note => note.Id, StringComparer.Ordinal)],
                bars,
                [.. _tempos.OrderBy(tempo => tempo.At)],
                [.. _keys.OrderBy(key => key.At)]);
            return new Assembled(music, _origins, _outcomes);
        }

        private Tick? Resolve(TimeAnchor anchor, out string? lost)
        {
            lost = null;
            if (anchor.NoteId is null)
            {
                return new Tick(Math.Max(0, anchor.Offset));
            }

            Note? note = _notes.FirstOrDefault(note => note.Id == anchor.NoteId);
            if (note is null)
            {
                lost = anchor.NoteId;
                return null;
            }

            return new Tick(Math.Max(0, note.Span.Onset.Value + anchor.Offset));
        }

        private bool ResolvePlace(AssemblyPlace place, out Tick start, out Tick end, out string? lost)
        {
            start = end = Tick.Zero;
            if (Resolve(place.Start, out lost) is not { } from)
            {
                return false;
            }

            start = end = from;
            if (place.End is { } anchor)
            {
                if (Resolve(anchor, out lost) is not { } to)
                {
                    return false;
                }

                end = to;
            }

            return true;
        }

        private int[] Leans(IEnumerable<string?> ids) =>
            [.. ids.OfType<string>().Select(id => Math.Max(_origins.GetValueOrDefault(id), _touched.GetValueOrDefault(id))).Where(number => number > 0).Distinct().Order()];

        private void Lost(AssemblyStep step, string lost) =>
            _outcomes.Add(new StepOutcome(step.Number, false, lost, [], [], [], [], 0, null, []));

        private void Carry(CarryStep carry)
        {
            SourceMusic? source = Source(carry.SourceId);
            if (source is null)
            {
                Lost(carry, carry.SourceId);
                return;
            }

            if (!ResolvePlace(carry.To, out Tick from, out Tick to, out string? lost))
            {
                Lost(carry, lost!);
                return;
            }

            Tick t0 = AssemblyBars.Snap(from);
            Tick t1 = AssemblyBars.Snap(to);
            if (t1 < t0)
            {
                t1 = t0;
            }

            Tick s0 = AssemblyBars.Snap(carry.From.Onset);
            Tick s1 = AssemblyBars.Snap(carry.From.End);
            if (s1 <= s0)
            {
                s1 = s0.Offset(AssemblyBars.Grid);
            }

            long length = s1.Value - s0.Value;
            long delta = length - (t1.Value - t0.Value);
            Dictionary<int, int> tracks = TracksByHand();
            List<Note> next = [];
            List<(string, int)> removed = [];
            List<string> shortened = [];
            foreach (Note note in _notes)
            {
                if (note.Span.Onset < t0)
                {
                    if (note.Span.End > t0)
                    {
                        next.Add(note with { Span = TickSpan.FromBounds(note.Span.Onset, t0) });
                        shortened.Add(note.Id);
                    }
                    else
                    {
                        next.Add(note);
                    }
                }
                else if (note.Span.Onset < t1)
                {
                    removed.Add((note.Id, _origins.GetValueOrDefault(note.Id)));
                }
                else
                {
                    next.Add(note with { Span = new TickSpan(note.Span.Onset.Offset(delta), note.Span.Duration) });
                }
            }

            List<string> put = [];
            foreach (Note note in source.Notes.Where(note => note.Span.Onset >= carry.From.Onset && note.Span.Onset < carry.From.End))
            {
                long onset = Math.Max(t0.Value, note.Span.Onset.Value - s0.Value + t0.Value);
                long finish = Math.Min(note.Span.End.Value, carry.From.End.Value) - s0.Value + t0.Value;
                finish = Math.Min(finish, t0.Value + length);
                if (finish <= onset)
                {
                    finish = onset + 1;
                }

                string id = carry.IdOf(note.Id);
                int hand = note.Hand?.Value is { } held ? (int)held : -1;
                next.Add(note with
                {
                    Id = id,
                    Pitch = new Pitch(Math.Clamp(note.Pitch.MidiNumber + carry.Transpose, 0, Pitch.MaxMidiNumber)),
                    Span = TickSpan.FromBounds(new Tick(onset), new Tick(finish)),
                    Track = tracks.TryGetValue(hand, out int track) ? track : tracks.GetValueOrDefault(-1),
                    Voice = null,
                });
                _origins[id] = carry.Number;
                put.Add(id);
            }

            _notes = next;
            foreach ((string id, _) in removed)
            {
                _origins.Remove(id);
            }

            IReadOnlyList<AssemblyBar> fragment = AssemblyBars.Moved(AssemblyBars.Cut(source.Bars, s0, s1), t0.Value - s0.Value);
            _bars = AssemblyBars.Splice(_bars, t0, t1, fragment);
            _tempos = [.. _tempos.Where(tempo => tempo.At < t0 || tempo.At >= t1 || tempo.At == Tick.Zero)
                .Select(tempo => tempo.At >= t1 && tempo.At != Tick.Zero ? new PlaybackTempo(tempo.At.Offset(delta), tempo.MicrosecondsPerQuarter) : tempo)];
            _keys = [.. _keys.Where(key => key.At < t0 || key.At >= t1 || key.At == Tick.Zero)
                .Select(key => key.At >= t1 && key.At != Tick.Zero ? key with { At = key.At.Offset(delta) } : key)];
            int[] leans = Leans([carry.To.Start.NoteId, carry.To.End?.NoteId]);
            _outcomes.Add(new StepOutcome(carry.Number, true, null, put, removed, [], shortened, delta, new TickSpan(t0, Math.Max(1, length)), leans));
        }

        private void ChangeNote(NoteStep change)
        {
            int index = _notes.FindIndex(note => note.Id == change.NoteId);
            if (index < 0)
            {
                Lost(change, change.NoteId);
                return;
            }

            Note note = _notes[index];
            int[] leans = Leans([change.NoteId]);
            if (change.Semitones is not { } semitones)
            {
                _notes.RemoveAt(index);
                _outcomes.Add(new StepOutcome(change.Number, true, null, [], [(note.Id, _origins.GetValueOrDefault(note.Id))], [], [], 0, note.Span, leans));
                _origins.Remove(note.Id);
                return;
            }

            _notes[index] = note with
            {
                Pitch = new Pitch(Math.Clamp(note.Pitch.MidiNumber + semitones, 0, Pitch.MaxMidiNumber)),
                Attribution = Attribution.UserEdited,
            };
            _touched[note.Id] = change.Number;
            _outcomes.Add(new StepOutcome(change.Number, true, null, [], [], [note.Id], [], 0, note.Span, leans));
        }

        private void Transpose(TransposeStep transpose)
        {
            if (!ResolvePlace(transpose.Place, out Tick from, out Tick to, out string? lost))
            {
                Lost(transpose, lost!);
                return;
            }

            List<string> changed = [];
            for (int i = 0; i < _notes.Count; i++)
            {
                Note note = _notes[i];
                if (note.Span.Onset >= from && note.Span.Onset < to)
                {
                    _notes[i] = note with { Pitch = new Pitch(Math.Clamp(note.Pitch.MidiNumber + transpose.Semitones, 0, Pitch.MaxMidiNumber)) };
                    _touched[note.Id] = transpose.Number;
                    changed.Add(note.Id);
                }
            }

            int[] leans = Leans([transpose.Place.Start.NoteId, transpose.Place.End?.NoteId]);
            _outcomes.Add(new StepOutcome(transpose.Number, true, null, [], [], changed, [], 0, to > from ? TickSpan.FromBounds(from, to) : null, leans));
        }

        /// <summary>The track of the player's notes that holds each hand most (key -1: the track of most notes).</summary>
        private Dictionary<int, int> TracksByHand()
        {
            Dictionary<int, int> tracks = [];
            foreach (IGrouping<int, Note> hand in _notes.GroupBy(note => note.Hand?.Value is { } held ? (int)held : -1))
            {
                tracks[hand.Key] = hand.GroupBy(note => note.Track).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;
            }

            if (!tracks.ContainsKey(-1))
            {
                tracks[-1] = _notes.Count == 0 ? 0 : _notes.GroupBy(note => note.Track).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;
            }

            return tracks;
        }

        private SourceMusic? Source(string id)
        {
            if (!_sources.TryGetValue(id, out SourceMusic? music))
            {
                music = sources(id);
                _sources[id] = music;
            }

            return music;
        }
    }
}