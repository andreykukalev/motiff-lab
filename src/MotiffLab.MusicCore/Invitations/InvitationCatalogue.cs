using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Invitations;

/// <summary>What the invitation and experiment catalogues read: a motif, the piece around it, and what the engine measured.</summary>
/// <param name="Notes">The notes of the piece; the motif's notes are picked by id.</param>
/// <param name="Character">The motif's character facts (<see cref="MotifAnalyzer.AnalyzeCharacter"/>).</param>
public sealed record InvitationContext(
    IReadOnlyList<Note> Notes,
    MeterMap Meter,
    MotifSelection Selection,
    MotifAnalysis Motif,
    StructureAnalysis Structure,
    IReadOnlyList<MotifFact> Character,
    Words Words = default);

/// <summary>
/// Turns the facts of a motif into the invitations of the track page (#79,
/// docs/decisions/0009-exploration-layer-and-notebook.md): each rule reads facts the engine
/// measured and, when they are confident, asks a question with an action that answers it.
/// </summary>
/// <remarks>
/// <para>
/// A rule needs every fact it cites to be present and not uncertain; otherwise it produces
/// nothing. An experiment is offered only when its operation's Keep row allows the preset and,
/// for an operation this version has, the operation can run on the motif with those parameters,
/// so an invitation never leads to a disabled step. Questions state only what the cited facts
/// state, and never what the player should feel.
/// </para>
/// <para>
/// Pure and deterministic: the same context gives the same invitations, in the rules' order. No
/// AI; it works the same with AI off.
/// </para>
/// </remarks>
public static class InvitationCatalogue
{
    /// <summary>The fewest onsets, every one the same step after the last, that count as running notes.</summary>
    public const int MinRunningOnsets = 8;

    /// <summary>The fewest melody notes evenly spaced that count as an even flow.</summary>
    public const int MinEvenNotes = 4;

    /// <summary>The tempo a running-notes passage is played at to follow it, as a share of the written tempo.</summary>
    public const int SlowTempoPercent = 60;

    /// <summary>The longest passage a listening action loops, in bars.</summary>
    public const int MaxPassageBars = 8;

    private const int MiddleC = 60;

    private static readonly Func<InvitationContext, Invitation?>[] s_rules =
    [
        ExactRepeat,
        RunningNotes,
        Pickup,
        EvenFlow,
        Mirror,
        TransposedRepeat,
        Register,
    ];

    /// <summary>The invitations for the motif, in the rules' order; empty when no rule finds anything confident.</summary>
    public static IReadOnlyList<Invitation> Invite(InvitationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Selection.NoteIds.Count == 0 ? [] : [.. s_rules.Select(rule => rule(context)).OfType<Invitation>()];
    }

    /// <summary>
    /// "repeat.exact": the motif's line comes back exactly. Compare the stretch from the motif to the
    /// repeat with the same length from the repeat: where do they stop being the same?
    /// </summary>
    private static Invitation? ExactRepeat(InvitationContext context)
    {
        if (!Confident(context, "structure.recurrences") || context.Structure.Recurrences.FirstOrDefault(static recurrence => recurrence.IsExact) is not { } repeat)
        {
            return null;
        }

        MotifSelection motif = context.Selection;
        ((int First, int Last) earlier, (int First, int Last) later) = repeat.FirstBar < motif.FirstBar
            ? ((repeat.FirstBar, repeat.LastBar), (motif.FirstBar, motif.LastBar))
            : ((motif.FirstBar, motif.LastBar), (repeat.FirstBar, repeat.LastBar));
        int length = Math.Min(Math.Min(later.First - earlier.First, MaxPassageBars), LastBar(context) - later.First + 1);
        if (length < 1)
        {
            return null;
        }

        Passage[] passages = [Passage.Bars(context.Meter, earlier.First, earlier.First + length - 1), Passage.Bars(context.Meter, later.First, later.First + length - 1)];
        return new Invitation(
            "repeat.exact",
            InvitationKind.Noticing,
            ["structure.recurrences"],
            $"The motif's line in {Bars(earlier.First, earlier.Last)} comes back exactly in {Bars(later.First, later.Last)}. Where does the music stop being the same?",
            new ListeningAction(ListeningKind.Compare, passages));
    }

    /// <summary>
    /// "rhythm.running": every onset of the motif is the same short step after the last, 8th or
    /// shorter. Follow the movement slower, through the phrase when it runs on past the motif.
    /// </summary>
    private static Invitation? RunningNotes(InvitationContext context)
    {
        Tick[] onsets = [.. MotifNotes(context).Select(static note => note.Span.Onset).Distinct().Order()];
        if (!Confident(context, "rhythm.grid", "character.density") || onsets.Length < MinRunningOnsets || EvenStep(onsets) is not { } step
            || step > CanonicalTime.TicksPerQuarter / 2 || NoteValue(step) is not { } value)
        {
            return null;
        }

        MotifSelection motif = context.Selection;
        PhraseSpan? phrase = context.Structure.Phrases.FirstOrDefault(phrase => phrase.Span.Onset <= onsets[0] && onsets[0] < phrase.Span.End);
        int last = phrase is not null && phrase.LastBar > motif.LastBar ? Math.Min(phrase.LastBar, motif.FirstBar + MaxPassageBars - 1) : motif.LastBar;
        string question = last > motif.LastBar
            ? $"Played slower, does the movement ever stop before the end of bar {last}?"
            : "Played slower, which notes carry the movement?";
        return new Invitation(
            "rhythm.running",
            InvitationKind.Noticing,
            ["rhythm.grid", "character.density"],
            $"A note starts every {value} through {Bars(motif.FirstBar, motif.LastBar)}, with no gap. {question}",
            new ListeningAction(ListeningKind.Slow, [Passage.Bars(context.Meter, motif.FirstBar, last)], SlowTempoPercent));
    }

    /// <summary>
    /// "rhythm.pickup": the motif starts off the downbeat and runs over the next bar line. Loop the
    /// opening and the next bar: how many notes does it take to recognise it?
    /// </summary>
    private static Invitation? Pickup(InvitationContext context)
    {
        Note[] notes = MotifNotes(context);
        if (!Confident(context, "rhythm.grid"))
        {
            return null;
        }

        Tick first = notes.Min(static note => note.Span.Onset);
        MetricPosition start = context.Meter.ToPosition(first);
        Bar bar = context.Meter.BarAt(first);
        if ((start.Beat == 1 && start.BeatOffset == 0 && !bar.IsPickup) || !notes.Any(note => note.Span.Onset >= bar.Span.End))
        {
            return null;
        }

        int next = bar.Number + 1;
        string where = bar.IsPickup ? "in the pickup" : start.BeatOffset == 0 ? $"on beat {start.Beat} of bar {bar.Number}" : $"after beat {start.Beat} of bar {bar.Number}";
        return new Invitation(
            "rhythm.pickup",
            InvitationKind.Noticing,
            ["rhythm.grid"],
            $"The motif starts {where}, before the bar line of bar {next}. How many of its notes do you need to recognise it?",
            new ListeningAction(ListeningKind.Loop, [Passage.Bars(context.Meter, bar.Number, Math.Min(next, LastBar(context)))]));
    }

    /// <summary>
    /// "rhythm.even": the melody moves in even steps. Make it long–short with V1 Rhythm reshape,
    /// dotted, keeping the pitch order: is the tune in its pitches or in its even flow?
    /// </summary>
    private static Invitation? EvenFlow(InvitationContext context)
    {
        IReadOnlyList<Note> line = context.Motif.Line;
        if (!Confident(context, "rhythm.durations", "melody.contour") || line.Count < MinEvenNotes
            || EvenStep([.. line.Select(static note => note.Span.Onset)]) is not { } step || NoteValue(step) is not { } value)
        {
            return null;
        }

        var parameters = new RhythmReshapeParameters(RhythmReshapeKind.Dotted);
        return Offered(context, RhythmReshape.Instance, parameters, "keep-pitch-order") is not { } preset
            ? null
            : new Invitation(
                "rhythm.even",
                InvitationKind.Experiment,
                ["rhythm.durations", "melody.contour"],
                $"The melody moves in even {value}s. Is the tune in its pitches or in its even flow?",
                new OperationAction(RhythmReshape.OperationId, "dotted", parameters, preset));
    }

    /// <summary>
    /// "melody.mirror": the melody moves up and down. Mirror it with V10 Change direction, keeping
    /// the rhythm: does the motif survive moving the other way?
    /// </summary>
    private static Invitation? Mirror(InvitationContext context)
    {
        IReadOnlyList<ContourStep> contour = context.Motif.Contour;
        int up = contour.Count(static step => step == ContourStep.Up);
        int down = contour.Count(static step => step == ContourStep.Down);
        if (!Confident(context, "melody.contour") || up + down < 2)
        {
            return null;
        }

        var parameters = new ChangeDirectionParameters(Diatonic: KeyOf(context) is not null);
        return Offered(context, ChangeDirection.Instance, parameters, "keep-rhythm") is not { } preset
            ? null
            : new Invitation(
                "melody.mirror",
                InvitationKind.Experiment,
                ["melody.contour"],
                $"The melody moves up {Times(up)} and down {Times(down)}. Does the motif survive moving the other way?",
                new OperationAction(ChangeDirection.OperationId, "mirror the whole motif", parameters, preset));
    }

    /// <summary>
    /// "repeat.transposed": the motif comes back at another pitch. Try another pitch level with V4
    /// Melodic sequence, a scale step in the same direction, keeping the rhythm.
    /// </summary>
    private static Invitation? TransposedRepeat(InvitationContext context)
    {
        if (!Confident(context, "structure.recurrences", "key")
            || context.Structure.Recurrences.FirstOrDefault(static recurrence => !recurrence.IsExact) is not { } repeat)
        {
            return null;
        }

        int steps = Math.Sign(repeat.Transposition);
        var parameters = new MelodicSequenceParameters(steps);
        string direction = steps > 0 ? "higher" : "lower";
        return Offered(context, MelodicSequence.Instance, parameters, "keep-rhythm") is not { } preset
            ? null
            : new Invitation(
                "repeat.transposed",
                InvitationKind.Experiment,
                ["structure.recurrences", "key"],
                $"The motif comes back a {MotifAnalyzer.IntervalName(Math.Abs(repeat.Transposition))} {direction} in {Bars(repeat.FirstBar, repeat.LastBar)}. What does it sound like one scale step {direction}, straight after itself?",
                new OperationAction(MelodicSequence.OperationId, $"1 scale step {direction}, once", parameters, preset));
    }

    /// <summary>
    /// "character.register": every note sits on one side of middle C. Move the motif an octave the
    /// other way with V3 Voicing and register, keeping the melody's pitch classes.
    /// </summary>
    private static Invitation? Register(InvitationContext context)
    {
        Note[] notes = MotifNotes(context);
        if (!Confident(context, "character.register"))
        {
            return null;
        }

        bool above = notes.All(static note => note.Pitch.MidiNumber >= MiddleC);
        bool below = notes.All(static note => note.Pitch.MidiNumber < MiddleC);
        int shift = above ? -12 : below ? 12 : 0;
        if (shift == 0 || notes.Any(note => note.Pitch.MidiNumber + shift is < OperationRules.LowestPitch or > OperationRules.HighestPitch)
            || KeepTable.DisabledReason("V3", KeepTable.FindPreset("keep-melody")!.Keep) is not null)
        {
            return null;
        }

        string direction = shift < 0 ? "lower" : "higher";
        return new Invitation(
            "character.register",
            InvitationKind.Experiment,
            ["character.register"],
            $"Every note of the motif is {(above ? "at or above" : "below")} middle C. What stays the same if it moves an octave {direction}?",
            new OperationAction("V3", $"octave shift {(shift < 0 ? "down" : "up")}", null, "keep-melody"));
    }

    /// <summary>The preset's id when the Keep table allows the operation with it and the operation can run on the motif; otherwise null.</summary>
    private static string? Offered<TParameters>(InvitationContext context, VariationOperation<TParameters> operation, TParameters parameters, string presetId)
        where TParameters : OperationParameters
    {
        IReadOnlySet<KeepProperty> keep = KeepTable.FindPreset(presetId)!.Keep;
        IVariationOperation offered = operation;
        VariationContext variation = VariationContext.For(context.Selection, keep, key: KeyOf(context), meter: context.Meter);
        return offered.DisabledReason(keep) is null && offered.UnavailableReason(MotifNotes(context), variation, parameters) is null ? presetId : null;
    }

    /// <summary>Whether every fact is in the motif's analysis and none is uncertain.</summary>
    private static bool Confident(InvitationContext context, params string[] ids) =>
        ids.All(id => Facts(context).FirstOrDefault(fact => fact.Id == id) is { Certainty: not FactCertainty.Uncertain });

    private static IEnumerable<MotifFact> Facts(InvitationContext context) =>
        context.Motif.Facts.Concat(context.Structure.Facts).Concat(context.Character);

    private static MusicalKey? KeyOf(InvitationContext context) =>
        context.Motif.Key is { Certainty: not FactCertainty.Uncertain } reading ? reading.Key : null;

    private static Note[] MotifNotes(InvitationContext context) => [.. VariationFacts.MotifNotes(context.Notes, context.Selection)];

    private static int LastBar(InvitationContext context) =>
        context.Meter.BarAt(new Tick(context.Notes.Max(static note => note.Span.End.Value) - 1)).Number;

    /// <summary>The step between the onsets when every one is the same step after the last; null otherwise.</summary>
    private static long? EvenStep(Tick[] onsets)
    {
        long[] steps = [.. onsets.Zip(onsets.Skip(1), static (from, to) => to.Value - from.Value).Distinct()];
        return steps.Length == 1 && steps[0] > 0 ? steps[0] : null;
    }

    private static string? NoteValue(long ticks) => ticks switch
    {
        CanonicalTime.TicksPerQuarter => "quarter",
        CanonicalTime.TicksPerQuarter / 2 => "8th",
        CanonicalTime.TicksPerQuarter / 3 => "triplet 8th",
        CanonicalTime.TicksPerQuarter / 4 => "16th",
        CanonicalTime.TicksPerQuarter / 6 => "triplet 16th",
        CanonicalTime.TicksPerQuarter / 8 => "32nd",
        _ => null,
    };

    /// <summary>"bar 5", "bars 5–6", "the pickup", "the pickup and bar 1".</summary>
    private static string Bars(int first, int last) => (first, last) switch
    {
        (0, 0) => "the pickup",
        (0, 1) => "the pickup and bar 1",
        (0, _) => $"the pickup and bars 1–{last}",
        _ when first == last => $"bar {first}",
        _ => $"bars {first}–{last}",
    };

    private static string Times(int count) => count switch
    {
        1 => "once",
        2 => "twice",
        _ => $"{count} times",
    };
}