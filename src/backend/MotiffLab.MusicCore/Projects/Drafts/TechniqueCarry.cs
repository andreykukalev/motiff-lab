using System.Text;
using System.Text.Json;

using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Performance;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>
/// A приём carried into another place (version 1.4, Andrei's answer of 2026-11-26; decision record 0034, section 8): music-core
/// builds, for the place it goes to, a variant that is that place's base with the приём done in each of its bars where the
/// приём's shape fits, as it builds bridge candidates (<see cref="DraftBridges"/>), and puts it in as a variant of the place,
/// which the player then takes from as from any other (<see cref="DraftTakes"/>). Deterministic: the same draft gives the
/// same notes. The notes it makes are marked inferred, and the variant's provenance says music-core made it and from what
/// (<see cref="NotesProvenance.Variation"/>, "params" {"carry": kind, "place", "variant", "bar"}).
/// </summary>
/// <remarks>
/// The приём is read at one of its bars (its first, or the one asked for): in the base, the notes it takes out, by hand,
/// place in the bar on a 16th and rank from the hand's edge (the top for the right hand, the bottom for the left); in the
/// variant, the notes it puts in, each where the alignment puts it in the bar, with its length, hand and interval to the
/// base note of its hand nearest it in pitch that sounds there: whole scale steps of the key there, and the semitones left.
/// In a bar of the other place of the same length and meter, the notes of the same hand, place and rank go (all of them
/// where the приём took all), and each note put in follows the note of the same rank that sounds there, by as many scale
/// steps of that place's key. A bar where a note to take out or to follow is not there, or where nothing would change, does
/// not take it. Bars the variant puts in (<see cref="TechniqueKind.InsertBars"/>) are not a shape and are not carried.
/// </remarks>
internal static class TechniqueCarry
{
    private const string KindParam = "carry";

    /// <summary>
    /// Where the приём <paramref name="number"/> of the place <paramref name="fromId"/>, read at its bar <paramref name="bar"/>
    /// (its first when null), can go: each other place of the draft that is not a bridge and that it was not carried into
    /// already (as <see cref="Put"/> tells), in order, with its bars (from 1) where the приём fits, none where it fits nowhere
    /// or would take out every note of the place.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place, приём, or bar of it.</exception>
    /// <exception cref="InvalidOperationException">Bars put in, which are not carried; the message in <paramref name="words"/>.</exception>
    public static IReadOnlyList<(DraftPlace Place, IReadOnlyList<int> Bars)> Targets(Draft draft, string fromId, int number, int? bar, Words words)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Pattern pattern = PatternOf(draft, fromId, number, bar, words);
        return [.. draft.Places.Where(place => place.Id != fromId && !place.Bridge)
            .Select(place => (Place: place, Carried: Applied(pattern, place.BaseVariant.Music)))
            .Where(each => !CarriedAlready(each.Place, pattern, each.Carried.Music))
            .Select(each => (each.Place, each.Carried is ({ Notes.Count: > 0 }, var bars) ? bars : []))];
    }

    /// <summary>
    /// The draft with the приём carried into the place <paramref name="toId"/> (as <see cref="Targets"/> reads it): a variant
    /// of that place, <paramref name="at"/> when, its id from <paramref name="ids"/>; and the place's bars it was carried into.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place, приём, or bar of it.</exception>
    /// <exception cref="ArgumentException">The place of the приём itself.</exception>
    /// <exception cref="InvalidOperationException">
    /// Bars put in, a bridge place, no bar it fits, every note of the place taken out, or the place has that variant already;
    /// the message in <paramref name="words"/>.
    /// </exception>
    public static (Draft Draft, IReadOnlyList<int> Bars) Put(Draft draft, string fromId, int number, int? bar, string toId, DateTimeOffset at, Words words, IdSource ids)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Pattern pattern = PatternOf(draft, fromId, number, bar, words);
        DraftPlace to = draft.FindPlace(toId) ?? throw new KeyNotFoundException($"The draft has no place {toId}.");
        if (toId == fromId)
        {
            throw new ArgumentException("The приём is carried into another place.", nameof(toId));
        }

        if (to.Bridge)
        {
            throw new InvalidOperationException(words.T("A bridge holds candidates for its seam: a technique is not carried into it.", "В связке кандидаты для её стыка, приём в неё не переносят."));
        }

        (SourceMusic music, IReadOnlyList<int> bars) = Applied(pattern, to.BaseVariant.Music);
        if (bars.Count > 0 && music.Notes.Count == 0)
        {
            throw new InvalidOperationException(words.T("The technique would take out every note of that place.", "Этот приём убрал бы все ноты этого места."));
        }

        if (bars.Count == 0)
        {
            throw new InvalidOperationException(words.T("The technique fits no bar of that place.", "Этот приём не подходит ни к одному такту этого места."));
        }

        if (CarriedAlready(to, pattern, music))
        {
            throw new InvalidOperationException(words.T("The technique is carried into that place already.", "Этот приём уже перенесён в это место."));
        }

        DraftSource from = to.BaseVariant.From;
        var provenance = new NotesProvenance(NotesProvenance.Variation) { ParamsJson = pattern.Params, At = at };
        var variant = new DraftVariant(draft.NextVariantId(ids), at, new DraftSource(from.Notes, from.Span) { Project = from.Project }, provenance, music);
        return (draft.With(variant, DraftDrop.Variant, toId), bars);
    }

    /// <summary>The kind of приём a variant carries, from its provenance; null for any other variant.</summary>
    public static TechniqueKind? KindOf(DraftVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return variant.MadeBy(KindParam) is { } kind ? Enum.GetValues<TechniqueKind>().Cast<TechniqueKind?>().FirstOrDefault(each => JsonWrite.Name(each!.Value) == kind) : null;
    }

    /// <summary>A note the приём takes out: its hand, its 16th in the bar, and its ranks from the hand's edge among the notes there; all of them, or those ranks.</summary>
    private sealed record Removal(Hand? Hand, long At, bool All, IReadOnlyList<int> Ranks);

    /// <summary>A note the приём puts in: its hand, where in the bar, how long, and the base note it follows (its rank) by scale steps and the semitones left.</summary>
    private sealed record Addition(Hand? Hand, long Offset, long Duration, int Rank, int Steps, int Semitones);

    /// <summary>The приём read at one bar: the bar's length and meter, what it takes out and puts in, and the provenance's params.</summary>
    private sealed record Pattern(long Length, TimeSignature Meter, IReadOnlyList<Removal> Removals, IReadOnlyList<Addition> Additions, string Params);

    /// <summary>The приём <paramref name="number"/> of the place read at its bar <paramref name="bar"/>, its first when null.</summary>
    private static Pattern PatternOf(Draft draft, string fromId, int number, int? bar, Words words)
    {
        DraftPlace place = draft.FindPlace(fromId) ?? throw new KeyNotFoundException($"The draft has no place {fromId}.");
        PlaceTechnique technique = draft.TechniquesOf(fromId).FirstOrDefault(each => each.Number == number) ?? throw new KeyNotFoundException($"The place {fromId} has no приём {number}.");
        if (technique.Kind == TechniqueKind.InsertBars)
        {
            throw new InvalidOperationException(words.T("Inserted bars go in whole in their own place: they are not carried.", "Вставленные такты ставятся целиком в своём месте, их не переносят."));
        }

        TechniqueOccurrence occurrence = bar is null ? technique.Occurrences[0]
            : technique.Occurrences.FirstOrDefault(each => each.Bar == bar) ?? throw new KeyNotFoundException($"The приём {number} is not at bar {bar}.");
        DraftVariant basis = place.BaseVariant, variant = place.Find(technique.Variant)!;
        AssemblyBar measure = basis.Music.Bars[occurrence.Bar - 1];
        Note[] inBar = [.. basis.Music.NotesIn(measure.Span.Onset, measure.Span.End)];
        Removal[] removals = Removals(inBar, [.. inBar.Where(note => occurrence.Removed.Contains(basis.NoteId(note.Id))).Select(static note => note.Id)], measure.Span.Onset);
        Addition[] additions = Additions(inBar, [.. occurrence.Added.Select(id => variant.Music.Notes.First(note => variant.NoteId(note.Id) == id))], DraftAlignment.Of(place, variant), measure, KeyAt(basis.Music, measure.Span.Onset));
        return removals.Length + additions.Length == 0
            ? throw new InvalidOperationException(words.T("The technique has no note to carry.", "В этом приёме нет нот, которые можно перенести."))
            : new Pattern(measure.Span.Duration, measure.Signature, removals, additions, Params(technique, fromId, occurrence.Bar));
    }

    /// <summary>The notes of a bar a приём takes out (by their ids in the base), by hand and 16th from <paramref name="start"/>, each group with its ranks from the hand's edge.</summary>
    private static Removal[] Removals(Note[] inBar, HashSet<string> removed, Tick start)
    {
        long SlotOf(Note note) => (note.Span.Onset.Value - start.Value) / PlaceTechniques.ShapeGrid;
        return [.. inBar.Where(note => removed.Contains(note.Id))
            .GroupBy(note => (Hand: note.Hand?.Value, At: SlotOf(note)))
            .OrderBy(static group => group.Key.At).ThenBy(static group => group.Key.Hand)
            .Select(group =>
            {
                Note[] there = Edged(inBar.Where(note => note.Hand?.Value == group.Key.Hand && SlotOf(note) == group.Key.At), group.Key.Hand);
                int[] ranks = [.. group.Select(note => Array.IndexOf(there, note)).Order()];
                return new Removal(group.Key.Hand, group.Key.At, ranks.Length == there.Length, ranks);
            })];
    }

    /// <summary>
    /// The notes of a variant a приём puts in a bar of the base, each where <paramref name="alignment"/> puts it in the bar,
    /// with the base note of its hand it follows there by scale steps of <paramref name="key"/>; one with none to follow is left out.
    /// </summary>
    private static Addition[] Additions(Note[] inBar, IEnumerable<Note> added, PerformanceAlignment alignment, AssemblyBar measure, MusicalKey key)
    {
        List<Addition> additions = [];
        foreach (Note note in added)
        {
            long offset = Math.Clamp(DraftAlignment.InBase(alignment, note.Span.Onset).Value - measure.Span.Onset.Value, 0, measure.Span.Duration - 1);
            Note[] followed = Followed(inBar, note.Hand?.Value, measure.Span.Onset.Offset(offset));
            if (followed.Length > 0)
            {
                Note nearest = followed.OrderBy(each => Math.Abs(each.Pitch.MidiNumber - note.Pitch.MidiNumber)).ThenBy(static each => each.Pitch.MidiNumber).First();
                int steps = ScaleSteps.Locate(key, note.Pitch.MidiNumber).Step - ScaleSteps.Locate(key, nearest.Pitch.MidiNumber).Step;
                additions.Add(new Addition(note.Hand?.Value, offset, note.Span.Duration, Array.IndexOf(followed, nearest), steps, note.Pitch.MidiNumber - ScaleSteps.Move(key, nearest.Pitch.MidiNumber, steps)));
            }
        }

        return [.. additions];
    }

    /// <summary>The base <paramref name="music"/> with the приём done in each bar of its length and meter where it fits, and those bars (from 1).</summary>
    private static (SourceMusic Music, IReadOnlyList<int> Bars) Applied(Pattern pattern, SourceMusic music)
    {
        HashSet<string> removed = [];
        List<Note> added = [];
        // The notes put in are named c_ and the next numbers in the base, counted on from the first free one.
        int next = int.Parse(IdSource.Numbered.Next("c_", music.Notes.Select(static note => note.Id)).AsSpan(2), System.Globalization.CultureInfo.InvariantCulture);
        List<int> bars = [];
        for (int index = 0; index < music.Bars.Count; index++)
        {
            AssemblyBar bar = music.Bars[index];
            if (bar.Span.Duration != pattern.Length || bar.Signature != pattern.Meter || InBar(pattern, music, bar) is not var (taken, put))
            {
                continue;
            }

            removed.UnionWith(taken);
            foreach (Note note in put)
            {
                added.Add(note with { Id = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"c_{next:0000}") });
                next++;
            }

            bars.Add(index + 1);
        }

        Note[] notes = [.. music.Notes.Where(note => !removed.Contains(note.Id)).Concat(added).OrderBy(static note => note.Span.Onset).ThenBy(static note => note.Pitch.MidiNumber).ThenBy(static note => note.Id, StringComparer.Ordinal)];
        return (music with { Notes = notes }, bars);
    }

    /// <summary>
    /// What the приём does in one bar: the ids of the notes it takes out and the notes it puts in (each named after the note it
    /// follows, given an id after); null when a note to take out or to follow is not there, or when nothing would change.
    /// </summary>
    private static (IReadOnlyList<string> Removed, IReadOnlyList<Note> Added)? InBar(Pattern pattern, SourceMusic music, AssemblyBar bar)
    {
        Note[] inBar = [.. music.NotesIn(bar.Span.Onset, bar.Span.End)];
        List<string> removed = [];
        foreach (Removal removal in pattern.Removals)
        {
            Note[] there = Edged([.. inBar.Where(note => note.Hand?.Value == removal.Hand && (note.Span.Onset.Value - bar.Span.Onset.Value) / PlaceTechniques.ShapeGrid == removal.At)], removal.Hand);
            if (there.Length == 0 || removal.Ranks.Any(rank => rank >= there.Length))
            {
                return null;
            }

            removed.AddRange(removal.All ? there.Select(static note => note.Id) : removal.Ranks.Select(rank => there[rank].Id));
        }

        MusicalKey key = KeyAt(music, bar.Span.Onset);
        Note[] kept = [.. inBar.Where(note => !removed.Contains(note.Id))];
        List<Note> added = [];
        foreach (Addition addition in pattern.Additions)
        {
            Tick at = bar.Span.Onset.Offset(addition.Offset);
            Note[] followed = Followed(inBar, addition.Hand, at);
            if (followed.Length == 0)
            {
                return null;
            }

            Note like = followed[Math.Min(addition.Rank, followed.Length - 1)];
            var pitch = new Pitch(Math.Clamp(ScaleSteps.Move(key, like.Pitch.MidiNumber, addition.Steps) + addition.Semitones, 0, Pitch.MaxMidiNumber));
            if (!kept.Concat(added).Any(note => note.Pitch == pitch && note.Span.Onset == at))
            {
                added.Add(like with
                {
                    Pitch = pitch,
                    Span = new TickSpan(at, addition.Duration),
                    Hand = addition.Hand is { } hand ? new Attributed<Hand>(hand, Attribution.Inferred(1)) : null,
                    Attribution = Attribution.Inferred(1),
                    Source = null,
                });
            }
        }

        return removed.Count + added.Count == 0 ? null : (removed, added);
    }

    /// <summary>
    /// The notes of the bar a note of <paramref name="hand"/> put in at <paramref name="at"/> follows, from the hand's edge:
    /// the hand's notes sounding there, else its notes struck last before it, else any hand's sounding there.
    /// </summary>
    private static Note[] Followed(IReadOnlyList<Note> inBar, Hand? hand, Tick at)
    {
        Note[] own = [.. inBar.Where(note => note.Hand?.Value == hand)];
        Note[] sounding = [.. own.Where(note => note.Span.Onset <= at && note.Span.End > at)];
        if (sounding.Length == 0 && own.Where(note => note.Span.Onset <= at).MaxBy(static note => note.Span.Onset) is { } last)
        {
            sounding = [.. own.Where(note => note.Span.Onset == last.Span.Onset)];
        }

        return Edged(sounding.Length > 0 ? sounding : [.. inBar.Where(note => note.Span.Onset <= at && note.Span.End > at)], hand);
    }

    /// <summary>The notes from the hand's edge: from the bottom for the left hand, from the top otherwise; by id on a tie.</summary>
    private static Note[] Edged(IEnumerable<Note> notes, Hand? hand) => hand == Hand.Left
        ? [.. notes.OrderBy(static note => note.Pitch.MidiNumber).ThenBy(static note => note.Id, StringComparer.Ordinal)]
        : [.. notes.OrderByDescending(static note => note.Pitch.MidiNumber).ThenBy(static note => note.Id, StringComparer.Ordinal)];

    /// <summary>The key in force at a tick, as the harmony reads it: its signature and mode, major where none is stated, C major where no key is.</summary>
    private static MusicalKey KeyAt(SourceMusic music, Tick at) =>
        KeySignature.InForce(music.Keys, at) is { } key ? DraftBridges.KeyOf(key) : new MusicalKey(0, KeyMode.Major);

    /// <summary>The provenance's params: {"carry": the kind, "place", "variant" and "bar" it was read at}.</summary>
    private static string Params(PlaceTechnique technique, string place, int bar)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString(KindParam, JsonWrite.Name(technique.Kind));
            json.WriteString("place", place);
            json.WriteString("variant", technique.Variant);
            json.WriteNumber("bar", bar);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Whether the place has the приём carried into it already: a variant made with its params and the notes <paramref name="carried"/> it would make.</summary>
    private static bool CarriedAlready(DraftPlace to, Pattern pattern, SourceMusic carried) =>
        to.Variants.Any(each => each.MadeWith(pattern.Params) && Same(each.Music.Notes, carried.Notes));

    private static bool Same(IReadOnlyList<Note> one, IReadOnlyList<Note> other) =>
        one.Select(static note => (note.Pitch.MidiNumber, note.Span)).SequenceEqual(other.Select(static note => (note.Pitch.MidiNumber, note.Span)));
}