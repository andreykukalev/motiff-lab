using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>What happened to one step when its place was rebuilt.</summary>
/// <param name="Lost">The note it could not find, when not applied.</param>
public sealed record DraftStepOutcome(int Number, bool Applied, string? Lost);

/// <summary>
/// The result of a place (decision record 0034, section 4): its base with the steps that name it applied in number order,
/// as the assembly's journal is (0024, section 2). Deterministic: the same place and steps give the same notes, bars,
/// tempo and keys. A step whose notes are no longer there is not applied and says which it lost. The base's notes and
/// those a step put in are named by their variant (<see cref="DraftVariant.NoteId"/>).
/// </summary>
/// <param name="Origins">For each note a step put in, the variant it came from; the base's notes are not here.</param>
public sealed record PlaceResult(SourceMusic Music, IReadOnlyDictionary<string, string> Origins, IReadOnlyList<DraftStepOutcome> Steps)
{
    public DraftStepOutcome? OutcomeOf(int number) => Steps.FirstOrDefault(step => step.Number == number);

    /// <summary>The place's base with <paramref name="steps"/> (its own, in number order) applied.</summary>
    public static PlaceResult Build(DraftPlace place, IEnumerable<DraftStep> steps)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(steps);
        Builder builder = new(place);
        foreach (DraftStep step in steps.OrderBy(static step => step.Number))
        {
            builder.Apply(step);
        }

        return builder.Result();
    }

    private sealed class Builder(DraftPlace place)
    {
        private readonly DraftVariant _base = place.BaseVariant;
        private readonly Dictionary<string, PerformanceAlignment> _alignments = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _origins = new(StringComparer.Ordinal);
        private readonly List<DraftStepOutcome> _outcomes = [];

        /// <summary>The bars put in so far, in order: where, in the place's ticks as they were then, and how long.</summary>
        private readonly List<(Tick At, long Duration)> _inserted = [];
        private List<Note> _notes = [.. place.BaseVariant.Music.Notes.Select(note => note with { Id = place.BaseVariant.NoteId(note.Id) })];
        private IReadOnlyList<AssemblyBar> _bars = place.BaseVariant.Music.Bars;
        private IReadOnlyList<PlaybackTempo> _tempos = place.BaseVariant.Music.Tempos;
        private IReadOnlyList<KeySignature> _keys = place.BaseVariant.Music.Keys;

        public void Apply(DraftStep step)
        {
            string? lost = step switch
            {
                DraftTake { Insert: { } insert } take => Insert(take, insert),
                DraftTake take => Take(take),
                DraftTranspose transpose => Transpose(transpose.Semitones),
                DraftTempo tempo => Tempo(tempo.QuarterNotesPerMinute),
                _ => null,
            };
            _outcomes.Add(new DraftStepOutcome(step.Number, lost is null, lost));
        }

        public PlaceResult Result() => new(
            new SourceMusic([.. _notes.OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Pitch.MidiNumber).ThenBy(static note => note.Id, StringComparer.Ordinal)], _bars, _tempos, _keys),
            _origins,
            _outcomes);

        /// <summary>The base notes taken out and the variant's notes put in at the place the alignment with the base gives them; the id it lost, else null.</summary>
        private string? Take(DraftTake take)
        {
            DraftVariant variant = place.Find(take.Variant)!;
            Dictionary<string, Note> theirs = variant.Music.Notes.ToDictionary(note => variant.NoteId(note.Id), StringComparer.Ordinal);
            HashSet<string> here = [.. _notes.Select(static note => note.Id)];
            string? lost = take.Removed.FirstOrDefault(id => !here.Contains(id))
                ?? take.Added.FirstOrDefault(id => !theirs.ContainsKey(id) || (here.Contains(id) && !take.Removed.Contains(id)));
            if (lost is not null)
            {
                return lost;
            }

            HashSet<string> removed = [.. take.Removed];
            PerformanceAlignment alignment = AlignmentOf(variant);
            _notes = [.. _notes.Where(note => !removed.Contains(note.Id))];
            foreach (string id in take.Added)
            {
                Note note = theirs[id];
                Tick at = AfterInserts(alignment.Anchors.Count == 0 ? note.Span.Onset : new Tick(Math.Max(0, alignment.TickAt(note.Span.Onset.Value * 1000).Value)));
                _notes.Add(note with { Id = id, Span = new TickSpan(at, note.Span.Duration) });
                _origins[id] = variant.Id;
            }

            return null;
        }

        /// <summary>The variant's bars put in whole at a place tick, in their own meter, the music after them moved on; the id it lost, else null.</summary>
        private string? Insert(DraftTake take, DraftInsert insert)
        {
            DraftVariant variant = place.Find(take.Variant)!;
            Tick end = _bars.Count == 0 ? Tick.Zero : _bars[^1].Span.End;
            if (insert.At > end || insert.Span.End > variant.Music.End)
            {
                return variant.Id;
            }

            long delta = insert.Span.Duration;
            long shift = insert.At.Value - insert.Span.Onset.Value;
            Tick Moved(Tick tick) => tick >= insert.At ? tick.Offset(delta) : tick;
            _bars = AssemblyBars.Splice(_bars, insert.At, insert.At, AssemblyBars.Moved(AssemblyBars.Cut(variant.Music.Bars, insert.Span.Onset, insert.Span.End), shift));
            _notes = [.. _notes.Select(note => note with { Span = new TickSpan(Moved(note.Span.Onset), note.Span.Duration) })];
            foreach (Note note in variant.Music.NotesIn(insert.Span.Onset, insert.Span.End))
            {
                string id = variant.NoteId(note.Id);
                _notes.Add(note with { Id = id, Span = new TickSpan(note.Span.Onset.Offset(shift), note.Span.Duration) });
                _origins[id] = variant.Id;
            }

            _tempos = [.. _tempos.Select(tempo => tempo with { At = Moved(tempo.At) })];
            _keys = [.. _keys.Select(key => key with { At = Moved(key.At) })];
            _inserted.Add((insert.At, delta));
            return null;
        }

        /// <summary>A tick of the base as it was, moved on by the bars put in before it so far, each at the place's ticks of its time.</summary>
        private Tick AfterInserts(Tick tick) => _inserted.Aggregate(tick, static (at, insert) => at >= insert.At ? at.Offset(insert.Duration) : at);

        private string? Transpose(int semitones)
        {
            _notes = [.. _notes.Select(note => note with { Pitch = new Pitch(Math.Clamp(note.Pitch.MidiNumber + semitones, 0, Pitch.MaxMidiNumber)) })];
            return null;
        }

        private string? Tempo(int perMinute)
        {
            _tempos = Playback.PlayerTempo.Over(_tempos, perMinute);
            return null;
        }

        /// <summary>The variant lined up with the base as it was, as a version is with the score (decision record 0022, section 2).</summary>
        private PerformanceAlignment AlignmentOf(DraftVariant variant)
        {
            if (!_alignments.TryGetValue(variant.Id, out PerformanceAlignment? alignment))
            {
                alignment = AssemblyMatch.Aligned(variant.Music, _base.Music.Notes);
                _alignments[variant.Id] = alignment;
            }

            return alignment;
        }
    }
}