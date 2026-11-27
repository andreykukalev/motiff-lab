using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Assembly;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Pieces;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Projects.Drafts;

/// <summary>The kinds of bridge candidate (the plan's answer 2), in the order they are offered.</summary>
internal enum BridgeKind
{
    /// <summary>«Доминанта»: the bar on V7 of the key after the seam.</summary>
    Dominant,

    /// <summary>«Через общий аккорд»: a chord both keys have, then V7.</summary>
    CommonChord,

    /// <summary>«Ход баса»: the bass by step from the last bass before the seam to the dominant, V7 over it once there.</summary>
    BassWalk,
}

/// <summary>A bridge candidate: its kind and its one bar of music, from 0.</summary>
internal sealed record BridgeCandidate(BridgeKind Kind, SourceMusic Music);

/// <summary>
/// Bridges at a seam (decision record 0034, section 5; the plan's answer 2): where the keys either side differ, two or three
/// candidates of one bar each, in the meter, tempo and key of the place after the seam, built by music-core from the chords
/// of <see cref="HarmonyExplorer.Chords"/>, put as the variants of a small new place between the two (<see cref="DraftPlace.Bridge"/>),
/// the first its base. The player hears them, makes one the base («Сделать основой»), or removes the place («Оставить»).
/// Deterministic: the same draft gives the same candidates. Their notes are marked inferred (made by a rule), and the
/// variant's provenance says music-core made them by which rule (<see cref="NotesProvenance.Variation"/>, "params"
/// {"bridge": kind}); it names as its source the Notes of the place after the seam, whose meter, tempo and key it follows.
/// </summary>
/// <remarks>
/// Register follows the neighbours: the bass is placed nearest the middle of the last bass before the seam and the first
/// after it, the right hand's chord in close position with its top nearest the middle of the last melody note before and
/// the first after, above the bass. Each chord has its root in the bass and the rest of its tones (a seventh's without
/// the root) in the right hand.
/// </remarks>
internal static class DraftBridges
{
    /// <summary>The most candidates offered.</summary>
    public const int Most = 3;

    private const string KindParam = "bridge";

    /// <summary>The numerals a common chord is looked for among in the key after the seam, the one leading on best first.</summary>
    private static readonly string[] s_pivots = ["IV", "II", "VI", "I", "III"];

    /// <summary>The candidates at <paramref name="seam"/>, in <see cref="BridgeKind"/> order, none alike; none when its keys do not differ or a side is a bridge.</summary>
    public static IReadOnlyList<BridgeCandidate> Of(DraftSeam seam)
    {
        ArgumentNullException.ThrowIfNull(seam);
        if (!seam.KeyChanges || seam.Before.Bridge || seam.After.Bridge || seam.Before.Notes.Count == 0 || seam.After.Notes.Count == 0)
        {
            return [];
        }

        MusicalKey to = KeyOf(seam.After.Key!);
        IReadOnlyList<ExplorerChord> chords = HarmonyExplorer.Chords(to);
        ExplorerChord dominant = chords.First(static chord => chord.Id == "V7");
        Voices voices = Voices.Of(seam);
        long bar = seam.After.Meter.BarTicks;
        List<BridgeCandidate> found = [];
        void Add(BridgeKind kind, IReadOnlyList<Sounding>? notes)
        {
            if (notes is not null && !found.Any(each => Same(each.Music.Notes, notes)))
            {
                found.Add(new BridgeCandidate(kind, Music(seam, voices, notes)));
            }
        }

        Add(BridgeKind.Dominant, Chord(dominant, 0, bar, voices.Bass, voices.Top));
        Add(BridgeKind.CommonChord, CommonChord(seam, chords, dominant, voices));
        Add(BridgeKind.BassWalk, BassWalk(seam, to, dominant, voices));
        return [.. found.Take(Most)];
    }

    /// <summary>
    /// «Связка»: the candidates of the seam before the place <paramref name="placeId"/> put as the variants of a new bridge
    /// place just before it, the first its base, <paramref name="at"/> when, their ids from <paramref name="ids"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such place.</exception>
    /// <exception cref="ArgumentException">The first place, which has no seam before it.</exception>
    /// <exception cref="InvalidOperationException">No bridge is offered there: the keys do not differ, or a side is a bridge.</exception>
    public static Draft Put(Draft draft, string placeId, DateTimeOffset at, IdSource ids)
    {
        ArgumentNullException.ThrowIfNull(draft);
        int index = draft.Places.ToList().IndexOf(draft.FindPlace(placeId) ?? throw new KeyNotFoundException($"The draft has no place {placeId}."));
        DraftSeam seam = index > 0 ? DraftSeams.Of(draft)[index - 1] : throw new ArgumentException("The first place has no seam before it.", nameof(placeId));
        IReadOnlyList<BridgeCandidate> candidates = Of(seam);
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("A bridge is offered only where the keys either side of the seam differ.");
        }

        DraftSource after = seam.After.Base.From;
        var from = new DraftSource(after.Notes, after.Span) { Project = after.Project };
        string place = draft.NextPlaceId(ids);
        List<DraftVariant> variants = [];
        foreach (BridgeCandidate candidate in candidates)
        {
            string id = ids.Next("v_", [.. draft.Ids, .. variants.Select(static variant => variant.Id)]);
            var provenance = new NotesProvenance(NotesProvenance.Variation) { ParamsJson = $$"""{"{{KindParam}}":"{{Name(candidate.Kind)}}"}""", At = at };
            variants.Add(new DraftVariant(id, at, from, provenance, candidate.Music));
        }

        return draft.WithPlaceBefore(new DraftPlace(place, variants[0].Id, variants) { Bridge = true }, placeId);
    }

    /// <summary>«Оставить» in a bridge place: the place <paramref name="placeId"/> removed with its candidates and its steps.</summary>
    /// <exception cref="KeyNotFoundException">No such place.</exception>
    /// <exception cref="ArgumentException">A place that is not a bridge.</exception>
    public static Draft Leave(Draft draft, string placeId)
    {
        ArgumentNullException.ThrowIfNull(draft);
        DraftPlace place = draft.FindPlace(placeId) ?? throw new KeyNotFoundException($"The draft has no place {placeId}.");
        return place.Bridge ? draft.WithoutPlace(placeId) : throw new ArgumentException($"The place {placeId} is not a bridge.", nameof(placeId));
    }

    /// <summary>The kind of bridge candidate a variant is, from its provenance; null for any other variant.</summary>
    public static BridgeKind? KindOf(DraftVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return variant.MadeBy(KindParam) is { } kind ? Enum.GetValues<BridgeKind>().Cast<BridgeKind?>().FirstOrDefault(each => Name(each!.Value) == kind) : null;
    }

    /// <summary>A candidate's kind in words: «Доминанта», «Через общий аккорд», «Ход баса».</summary>
    public static string Label(BridgeKind kind, Words words) => kind switch
    {
        BridgeKind.Dominant => words.T("Dominant", "Доминанта"),
        BridgeKind.CommonChord => words.T("Through a common chord", "Через общий аккорд"),
        _ => words.T("Bass walk", "Ход баса"),
    };

    /// <summary>
    /// What a candidate holds, only as its notes and key state it: the chords it sounds, by symbol and numeral in its key, and
    /// for a bass walk how many bass notes lead to the dominant: «D7 (V7) в тональности соль мажор»; null for a variant that
    /// is no bridge candidate.
    /// </summary>
    public static string? Explanation(DraftVariant variant, Words words)
    {
        ArgumentNullException.ThrowIfNull(variant);
        if (KindOf(variant) is not { } kind || variant.Music.Keys.FirstOrDefault() is not { } signature)
        {
            return null;
        }

        MusicalKey key = KeyOf(signature);
        IReadOnlyList<ExplorerChord> chords = HarmonyExplorer.Chords(key);
        string[] sounded = [.. variant.Music.Notes.GroupBy(static note => note.Span.Onset).Where(static struck => struck.Count() >= 3).OrderBy(static struck => struck.Key)
            .Select(struck => chords.FirstOrDefault(chord => PitchClasses(chord).SetEquals(struck.Select(static note => note.Pitch.PitchClass))))
            .OfType<ExplorerChord>().Select(static chord => $"{chord.Symbol} ({chord.Numeral})")];
        if (sounded.Length == 0)
        {
            return null;
        }

        string inKey = words.T($"{sounded[^1]} in {key.NameIn(words)}", $"{sounded[^1]} в тональности {key.NameIn(words)}");
        int bass = variant.Music.Notes.Count(static note => note.Hand?.Value == Hand.Left);
        return kind switch
        {
            BridgeKind.CommonChord when sounded.Length > 1 => words.T($"{sounded[0]}, a chord of both keys, then {inKey}", $"{sounded[0]}, аккорд обеих тональностей, затем {inKey}"),
            BridgeKind.BassWalk => words.T(
                $"The bass by step to the dominant: {words.Count(bass, "note", "notes", "нота", "ноты", "нот")}, then {inKey}",
                $"Бас шагами к доминанте: {words.Count(bass, "note", "notes", "нота", "ноты", "нот")}, затем {inKey}"),
            _ => inKey,
        };
    }

    /// <summary>A pitch, its onset and length in the bar, and its hand.</summary>
    private readonly record struct Sounding(int Pitch, long Onset, long Duration, Hand Hand);

    /// <summary>
    /// The neighbours' register: where the bass and the right hand's top are put, the last bass before the seam (where a bass
    /// walk starts), and the velocity and track of each hand, the last before the seam's.
    /// </summary>
    private sealed record Voices(int Bass, int Top, Note LastBass, Note LastTop)
    {
        public static Voices Of(DraftSeam seam)
        {
            MotifTexture before = MotifTexture.Read(seam.Before.Notes), after = MotifTexture.Read(seam.After.Notes);
            Note lastBass = Low(before)[^1], lastTop = before.Melody[^1];
            return new Voices((lastBass.Pitch.MidiNumber + Low(after)[0].Pitch.MidiNumber) / 2, (lastTop.Pitch.MidiNumber + after.Melody[0].Pitch.MidiNumber) / 2, lastBass, lastTop);
        }

        /// <summary>The bass line, or the melody of a single line.</summary>
        private static IReadOnlyList<Note> Low(MotifTexture texture) => texture.Bass.Count > 0 ? texture.Bass : texture.Melody;
    }

    /// <summary>«Через общий аккорд»: a chord of both keys over the first half of the bar's beats, then V7; null when the keys share none or the bar has one beat.</summary>
    private static Sounding[]? CommonChord(DraftSeam seam, IReadOnlyList<ExplorerChord> chords, ExplorerChord dominant, Voices voices)
    {
        TimeSignature meter = seam.After.Meter;
        IReadOnlyList<ExplorerChord> before = HarmonyExplorer.Chords(KeyOf(seam.Before.Key!));
        ExplorerChord? pivot = s_pivots
            .Select(numeral => chords.FirstOrDefault(chord => chord.Type is ChordType.Major or ChordType.Minor && chord.Numeral.ToUpperInvariant() == numeral
                && before.Any(other => other.Type == chord.Type && other.Root.PitchClass == chord.Root.PitchClass)))
            .FirstOrDefault(static chord => chord is not null);
        if (pivot is null || meter.Numerator < 2)
        {
            return null;
        }

        long half = meter.BarTicks / meter.Numerator * (meter.Numerator / 2);
        Sounding[] first = Chord(pivot, 0, half, voices.Bass, voices.Top);
        return [.. first, .. Chord(dominant, half, meter.BarTicks - half, voices.Bass, first.Max(static note => note.Pitch))];
    }

    /// <summary>
    /// «Ход баса»: the bass from the last bass note before the seam by steps of the new key's scale to the dominant's root
    /// nearest it (from the step above when it is there already), one beat each, halved until they fit the bar, the last held
    /// to the end of the bar under V7; null when even sixteenths would not fit.
    /// </summary>
    private static Sounding[]? BassWalk(DraftSeam seam, MusicalKey key, ExplorerChord dominant, Voices voices)
    {
        int start = voices.LastBass.Pitch.MidiNumber;
        int target = BassPattern.Near(dominant.Root.PitchClass, start);
        int from = ScaleSteps.Locate(key, start).Step, to = ScaleSteps.Locate(key, target).Step;
        List<int> walk = start == target ? [ScaleSteps.Move(key, target, 1)] : [start];
        for (int step = 1; step < Math.Abs(to - from); step++)
        {
            walk.Add(ScaleSteps.MidiNumber(key, new ScalePosition(from + (step * Math.Sign(to - from)), 0)));
        }

        walk.Add(target);
        long bar = seam.After.Meter.BarTicks, unit = bar / seam.After.Meter.Numerator;
        while (walk.Count * unit > bar && unit > CanonicalTime.TicksPerQuarter / 4)
        {
            unit /= 2;
        }

        if (walk.Count * unit > bar)
        {
            return null;
        }

        long arrives = (walk.Count - 1) * unit;
        IEnumerable<Sounding> bass = walk.Select((pitch, index) => new Sounding(pitch, index * unit, index == walk.Count - 1 ? bar - arrives : unit, Hand.Left));
        return [.. bass, .. Chord(dominant, arrives, bar - arrives, target, voices.Top).Where(static note => note.Hand == Hand.Right)];
    }

    /// <summary>A chord from <paramref name="onset"/> for <paramref name="duration"/>: its root nearest <paramref name="bass"/> in the left hand, the rest in close position in the right, its top nearest <paramref name="top"/>, above the bass.</summary>
    private static Sounding[] Chord(ExplorerChord chord, long onset, long duration, int bass, int top)
    {
        int root = BassPattern.Near(chord.Root.PitchClass, bass);
        return [new Sounding(root, onset, duration, Hand.Left), .. Upper(chord, top, root).Select(pitch => new Sounding(pitch, onset, duration, Hand.Right))];
    }

    /// <summary>
    /// The right hand's tones of a chord (a seventh's without its root) in close position: of the stacks from each tone, the
    /// one whose top is nearest <paramref name="top"/>, the lower on a tie, moved up by octaves until it is above <paramref name="above"/>.
    /// </summary>
    private static int[] Upper(ExplorerChord chord, int top, int above)
    {
        int[] tones = [.. ChordReference.Spell(chord.Root, chord.Type).Select(static tone => tone.PitchClass)];
        tones = tones.Length > 3 ? tones[1..] : tones;
        int[] best = Enumerable.Range(0, tones.Length).Select(turn => Near(Stack(tones, turn), top))
            .OrderBy(stack => Math.Abs(stack[^1] - top)).ThenBy(static stack => stack[^1]).First();
        while (best[0] <= above)
        {
            best = [.. best.Select(static pitch => pitch + 12)];
        }

        return best;
    }

    /// <summary>The tones from the one at <paramref name="turn"/> each next above the one before, from that tone's pitch class.</summary>
    private static int[] Stack(int[] tones, int turn)
    {
        int[] stack = new int[tones.Length];
        for (int index = 0, previous = tones[turn] - 1; index < tones.Length; index++)
        {
            int pitch = previous + 1 + Mod12(tones[(turn + index) % tones.Length] - previous - 1);
            stack[index] = pitch;
            previous = pitch;
        }

        return stack;
    }

    /// <summary>The stack moved by octaves so its top is nearest <paramref name="top"/>, the lower on a tie.</summary>
    private static int[] Near(int[] stack, int top)
    {
        int lower = top - stack[^1] - Mod12(top - stack[^1]), upper = lower + 12;
        int shift = Math.Abs(stack[^1] + upper - top) < Math.Abs(stack[^1] + lower - top) ? upper : lower;
        return [.. stack.Select(pitch => pitch + shift)];
    }

    /// <summary>The bar's notes in order, named <c>n_0001</c> on, inferred, each hand at the velocity and track of its last note before the seam; the place after's meter, tempo and key.</summary>
    private static SourceMusic Music(DraftSeam seam, Voices voices, IReadOnlyList<Sounding> notes)
    {
        Note[] made = [.. notes.OrderBy(static note => note.Onset).ThenBy(static note => note.Pitch).Select((note, index) =>
        {
            Note like = note.Hand == Hand.Left ? voices.LastBass : voices.LastTop;
            return new Note
            {
                Id = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"n_{index + 1:0000}"),
                Pitch = new Pitch(Math.Clamp(note.Pitch, 0, Pitch.MaxMidiNumber)),
                Span = new TickSpan(new Tick(note.Onset), note.Duration),
                Velocity = like.Velocity,
                Track = like.Track,
                Hand = new Attributed<Hand>(note.Hand, Attribution.Inferred(1)),
                Attribution = Attribution.Inferred(1),
            };
        })];
        TimeSignature meter = seam.After.Meter;
        return new SourceMusic(made, [new AssemblyBar(new TickSpan(Tick.Zero, meter.BarTicks), meter)], [seam.After.Tempo with { At = Tick.Zero }], [seam.After.Key! with { At = Tick.Zero }]);
    }

    private static bool Same(IReadOnlyList<Note> made, IReadOnlyList<Sounding> notes) =>
        made.Select(static note => (note.Pitch.MidiNumber, note.Span.Onset.Value, note.Span.Duration)).Order()
            .SequenceEqual(notes.Select(static note => (note.Pitch, note.Onset, note.Duration)).Order());

    /// <summary>A key as the harmony reads it: its signature and mode, major where the source states no mode.</summary>
    internal static MusicalKey KeyOf(KeySignature key) => new(key.Fifths, key.Mode ?? KeyMode.Major);

    private static HashSet<int> PitchClasses(ExplorerChord chord) => [.. ChordReference.Spell(chord.Root, chord.Type).Select(static tone => tone.PitchClass)];

    /// <summary>The kind as the provenance's params name it: dominant, commonChord, bassWalk.</summary>
    private static string Name(BridgeKind kind) => kind switch
    {
        BridgeKind.Dominant => "dominant",
        BridgeKind.CommonChord => "commonChord",
        _ => "bassWalk",
    };

    private static int Mod12(int value) => ((value % 12) + 12) % 12;
}