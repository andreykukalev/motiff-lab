using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Events;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Time;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Experiments;

/// <summary>The musical question an experiment explores, as the "I have a question" list shows it.</summary>
public enum ExperimentTopic
{
    /// <summary>How does the character change if the notes stay the same?</summary>
    Character,

    /// <summary>Is the tune in its pitches or in its rhythm?</summary>
    Rhythm,

    /// <summary>What does each layer add?</summary>
    Layers,

    /// <summary>Which changes keep the motif recognisable?</summary>
    Motif,
}

/// <summary>One step of an experiment's recipe: a catalogue operation with its parameters, scope included.</summary>
public sealed record ExperimentStep(string OperationId, OperationParameters Parameters)
{
    /// <summary>The operation this version offers for <see cref="OperationId"/>.</summary>
    public IVariationOperation Operation => VariationOperations.Find(OperationId)
        ?? throw new InvalidOperationException($"This version has no operation {OperationId}.");
}

/// <summary>One way to try an experiment: a label, what it keeps, and the steps that make it.</summary>
/// <param name="Label">As the player reads it on the card, for example "Detached".</param>
/// <param name="Keeps">What stays the same, completing "Keeps …", for example "the pitches and when each note starts".</param>
/// <param name="Steps">The recipe, applied in order; at least one step.</param>
public sealed record ExperimentAlternative(string Label, string Keeps, IReadOnlyList<ExperimentStep> Steps)
{
    public bool Equals(ExperimentAlternative? other) =>
        other is not null && Label == other.Label && Keeps == other.Keeps && Steps.SequenceEqual(other.Steps);

    public override int GetHashCode() => HashCode.Combine(Label, Keeps, Steps.Count);
}

/// <summary>
/// A musical experiment offered for a passage (level 3 in the Explore model): a short idea, the
/// question behind it, and two or three alternatives, each a recipe of catalogue steps that the
/// player hears against the original and tries at the piano.
/// </summary>
/// <param name="Id">Stable once shipped, for example "melody.space"; notebook entries and analytics store it.</param>
/// <param name="Title">The idea in a few words, for example "Give the melody more space".</param>
/// <param name="Change">What changes and what stays, in one or two sentences that state only what the steps do.</param>
/// <param name="Question">The question it explores; never what the player should feel.</param>
/// <param name="ListeningFocus">What to listen for, and what to try at the piano.</param>
/// <param name="FactIds">The facts it rests on, so Understand can show them.</param>
/// <param name="Passage">Where the steps apply: the selection, or for a motif experiment the motif's repeat.</param>
/// <param name="Filter">Which notes of the passage the steps read, as in the selection.</param>
/// <param name="KeepPresetId">The Keep preset the steps run with (<see cref="KeepTable.Presets"/>).</param>
/// <param name="Alternatives">The ways to try it, closest to the original first; each one can run on the passage.</param>
public sealed record Experiment(
    string Id,
    ExperimentTopic Topic,
    string Title,
    string Change,
    string Question,
    string ListeningFocus,
    IReadOnlyList<string> FactIds,
    Passage Passage,
    SelectionFilter Filter,
    string KeepPresetId,
    IReadOnlyList<ExperimentAlternative> Alternatives)
{
    public bool Equals(Experiment? other) =>
        other is not null
        && Id == other.Id
        && Topic == other.Topic
        && Title == other.Title
        && Change == other.Change
        && Question == other.Question
        && ListeningFocus == other.ListeningFocus
        && Passage == other.Passage
        && Filter == other.Filter
        && KeepPresetId == other.KeepPresetId
        && FactIds.SequenceEqual(other.FactIds, StringComparer.Ordinal)
        && Alternatives.SequenceEqual(other.Alternatives);

    public override int GetHashCode() => HashCode.Combine(Id, Passage, Alternatives.Count);
}

/// <summary>What "Surprise me" picked: an experiment and which of its alternatives to play first.</summary>
public sealed record SurprisePick(Experiment Experiment, int Alternative);

/// <summary>
/// The experiments of the Explore mode: musical ideas built from catalogue operations, each offered
/// when the facts of the passage fit it and at least one of its alternatives can run there.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue reads the same context as the invitation catalogue, so Understand and Explore
/// rest on the same facts. A rule needs every fact it cites to be present and not uncertain. An
/// alternative is kept only when every step can run in turn on the passage with the experiment's
/// Keep preset, so a card never leads to a disabled step.
/// </para>
/// <para>
/// Pure and deterministic, with no AI: the same context gives the same experiments in the
/// catalogue's order, and "Surprise me" picks by the seed it is given.
/// </para>
/// </remarks>
public static class ExperimentCatalogue
{
    /// <summary>The share of melody notes that must sound until the next melody onset for "more space" to be offered.</summary>
    public const decimal MinConnectedShare = 0.5m;

    /// <summary>The fewest melody notes evenly spaced that a groove experiment needs.</summary>
    public const int MinEvenNotes = 4;

    /// <summary>The most alternatives an experiment offers.</summary>
    public const int MaxAlternatives = 4;

    public const string MelodySpaceId = "melody.space";

    public const string GrooveId = "rhythm.groove";

    public const string AccompanimentId = "layers.accompaniment";

    public const string AnswerId = "motif.answer";

    /// <summary>Every experiment's id, in the catalogue's order; analytics may send only these.</summary>
    public static IReadOnlyList<string> Ids { get; } = [MelodySpaceId, GrooveId, AccompanimentId, AnswerId];

    private static readonly Func<ExperimentContext, Experiment?>[] s_rules =
    [
        MelodySpace,
        Groove,
        LeftHand,
        AnswerTheMotif,
    ];

    /// <summary>The question each topic stands for in the "I have a question" list.</summary>
    public static string Prompt(ExperimentTopic topic) => Prompt(topic, Words.English);

    /// <summary>The question for a topic in a language.</summary>
    public static string Prompt(ExperimentTopic topic, Words words) => topic switch
    {
        ExperimentTopic.Character => words.T("How does the character change if the notes stay the same?", "Как меняется характер, если ноты остаются теми же?"),
        ExperimentTopic.Rhythm => words.T("Is the tune in its pitches or in its rhythm?", "Мелодия держится на высотах или на ритме?"),
        ExperimentTopic.Layers => words.T("What does each layer add?", "Что добавляет каждый слой?"),
        ExperimentTopic.Motif => words.T("Which changes keep the motif recognisable?", "Какие изменения оставляют мотив узнаваемым?"),
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, "Unknown topic."),
    };

    /// <summary>The experiments that fit the selection, in the catalogue's order; empty when none does.</summary>
    public static IReadOnlyList<Experiment> Offer(InvitationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Selection.NoteIds.Count == 0)
        {
            return [];
        }

        var experiment = new ExperimentContext(context, KeyOf(context));
        return [.. s_rules.Select(rule => rule(experiment)).OfType<Experiment>()];
    }

    /// <summary>The experiments that fit the selection and explore <paramref name="topic"/>.</summary>
    public static IReadOnlyList<Experiment> ForQuestion(InvitationContext context, ExperimentTopic topic) =>
        [.. Offer(context).Where(experiment => experiment.Topic == topic)];

    /// <summary>
    /// The experiments of <paramref name="offered"/> that rest on the fact <paramref name="factId"/>,
    /// in their order, so a fact can link to its experiments as each experiment links to its facts
    /// through <see cref="Experiment.FactIds"/>.
    /// </summary>
    public static IReadOnlyList<Experiment> RestingOn(IEnumerable<Experiment> offered, string factId)
    {
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(factId);
        return [.. offered.Where(experiment => experiment.FactIds.Contains(factId, StringComparer.Ordinal))];
    }

    /// <summary>
    /// "Surprise me": one experiment that fits, preferring those whose id is not in
    /// <paramref name="tried"/>, and one of its alternatives, both chosen by <paramref name="seed"/>.
    /// Null when nothing fits.
    /// </summary>
    public static SurprisePick? Surprise(InvitationContext context, int seed, IReadOnlySet<string> tried)
    {
        ArgumentNullException.ThrowIfNull(tried);
        IReadOnlyList<Experiment> offered = Offer(context);
        Experiment[] fresh = [.. offered.Where(experiment => !tried.Contains(experiment.Id))];
        Experiment[] pool = fresh.Length > 0 ? fresh : [.. offered];
        if (pool.Length == 0)
        {
            return null;
        }

        uint mixed = unchecked((uint)seed * 2654435761u);
        Experiment picked = pool[(int)(mixed % (uint)pool.Length)];
        return new SurprisePick(picked, (int)(mixed / (uint)pool.Length % (uint)picked.Alternatives.Count));
    }

    /// <summary>
    /// The variation of one alternative: a chain over the passage's notes with every step accepted,
    /// which the player hears, keeps or changes like any other variation.
    /// </summary>
    /// <exception cref="InvalidOperationException">A step cannot run on these notes.</exception>
    /// <param name="words">The language of the variation's explanation; English by default.</param>
    public static VariationChain Start(Experiment experiment, int alternative, IReadOnlyList<Note> notes, MeterMap meter, MusicalKey? key, Words words = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentOutOfRangeException.ThrowIfNegative(alternative);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(alternative, experiment.Alternatives.Count);
        return TryStart(experiment.Passage.Span, experiment.Filter, experiment.KeepPresetId, experiment.Alternatives[alternative].Steps, notes, meter, key, words, out string? reason)
            ?? throw new InvalidOperationException(reason);
    }

    /// <summary>The variation of one alternative, over the notes, meter and key the experiment was offered for, in the context's language.</summary>
    /// <exception cref="InvalidOperationException">A step cannot run on these notes.</exception>
    public static VariationChain Start(Experiment experiment, int alternative, InvitationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Start(experiment, alternative, context.Notes, context.Meter, KeyOf(context), context.Words);
    }

    /// <summary>
    /// "melody.space": the melody's notes mostly sound until the next one starts. Space them with
    /// V13 Articulation limited to the melody, so the accompaniment keeps ringing.
    /// </summary>
    private static Experiment? MelodySpace(ExperimentContext context)
    {
        IReadOnlyList<Note> line = context.Source.Motif.Line;
        if (!context.Confident("character.articulation", "melody.line") || line.Count < 2 || ConnectedShare(line) < MinConnectedShare)
        {
            return null;
        }

        Words w = context.Source.Words;
        string keeps = w.T("the pitches and when each note starts", "высоты и моменты начала нот");
        return context.Build(
            MelodySpaceId,
            ExperimentTopic.Character,
            w.T("Give the melody more space", "Дать мелодии больше воздуха"),
            w.T(
                "Same pitches, same onsets. Each melody note sounds for part of its time, so a gap opens before the next one; the accompaniment is not changed.",
                "Те же высоты, те же начала нот. Каждая нота мелодии звучит лишь часть своего времени, поэтому перед следующей появляется зазор; аккомпанемент не меняется."),
            w.T("How does the character change when the notes themselves stay the same?", "Как меняется характер, если сами ноты остаются теми же?"),
            w.T(
                "Listen for the gaps between the melody notes. Then play the phrase detached on the piano, and once more connected.",
                "Слушайте зазоры между нотами мелодии. Затем сыграйте фразу на фортепиано отрывисто, а потом ещё раз связно."),
            ["character.articulation", "melody.line"],
            "keep-pitch-order",
            [
                Alternative(w.T("Detached", "Отрывисто"), keeps, new ExperimentStep(Articulation.OperationId, new ArticulationParameters(ArticulationKind.Detached) { Scope = OperationScope.Melody })),
                Alternative(w.T("Short", "Коротко"), keeps, new ExperimentStep(Articulation.OperationId, new ArticulationParameters(ArticulationKind.Short) { Scope = OperationScope.Melody })),
            ]);
    }

    /// <summary>
    /// "rhythm.groove": the melody moves in even steps. Make it long–short or push it ahead of the
    /// beat with V1 Rhythm reshape limited to the melody, keeping the pitch order.
    /// </summary>
    private static Experiment? Groove(ExperimentContext context)
    {
        IReadOnlyList<Note> line = context.Source.Motif.Line;
        Tick[] onsets = [.. line.Select(static note => note.Span.Onset)];
        long[] steps = [.. onsets.Zip(onsets.Skip(1), static (from, to) => to.Value - from.Value).Distinct()];
        if (!context.Confident("rhythm.durations", "melody.contour") || line.Count < MinEvenNotes || steps.Length != 1)
        {
            return null;
        }

        Words w = context.Source.Words;
        string keeps = w.T("the pitches and their order", "высоты и их порядок");
        return context.Build(
            GrooveId,
            ExperimentTopic.Rhythm,
            w.T("Same notes, different groove", "Те же ноты, другой ритм"),
            w.T(
                "The melody keeps its pitches and their order; its rhythm changes. The accompaniment is not changed.",
                "Мелодия сохраняет высоты и их порядок; меняется её ритм. Аккомпанемент не меняется."),
            w.T("Is the tune in its pitches or in its even flow?", "Мелодия держится на высотах или на ровном движении?"),
            w.T(
                "Sing or play the new rhythm before you hear it again, then try it at the piano with the original pitches.",
                "Пропойте или простучите новый ритм, прежде чем слушать снова, затем попробуйте его на фортепиано с исходными высотами."),
            ["rhythm.durations", "melody.contour"],
            "keep-pitch-order",
            [
                Alternative(w.T("Long–short", "Долгая–короткая"), keeps, new ExperimentStep(RhythmReshape.OperationId, new RhythmReshapeParameters(RhythmReshapeKind.Dotted) { Scope = OperationScope.Melody })),
                Alternative(w.T("Ahead of the beat", "С опережением доли"), keeps, new ExperimentStep(RhythmReshape.OperationId, new RhythmReshapeParameters(RhythmReshapeKind.Syncopate) { Scope = OperationScope.Melody })),
            ]);
    }

    /// <summary>
    /// "layers.accompaniment": the selection has an accompaniment under its melody. Replay each bar's
    /// accompaniment notes in a new pattern with V2, keeping the melody.
    /// </summary>
    private static Experiment? LeftHand(ExperimentContext context)
    {
        MotifTexture texture = MotifTexture.Read(context.Notes);
        if (!context.Confident("melody.line") || texture.IsSingleLine || texture.Notes.Count == texture.Melody.Count)
        {
            return null;
        }

        static ExperimentStep Pattern(AccompanimentPatternKind kind, PatternUnit unit) =>
            new(AccompanimentPattern.OperationId, new AccompanimentPatternParameters(kind, unit));
        Words w = context.Source.Words;
        string keeps = w.T("the melody and the notes of each bar's accompaniment", "мелодия и ноты аккомпанемента каждого такта");
        return context.Build(
            AccompanimentId,
            ExperimentTopic.Layers,
            w.T("Keep the melody, change the accompaniment", "Мелодия та же, аккомпанемент другой"),
            w.T(
                "The melody stays as written. Each bar's accompaniment plays the same notes in a new pattern.",
                "Мелодия остаётся как написано. Аккомпанемент каждого такта играет те же ноты в новом рисунке."),
            w.T("What does each layer add to what you hear?", "Что добавляет каждый слой к тому, что вы слышите?"),
            w.T(
                "Play the melody alone first, then with the new accompaniment. Notice what the pattern adds to the movement.",
                "Сначала сыграйте одну мелодию, затем с новым аккомпанементом. Заметьте, что рисунок добавляет к движению."),
            ["melody.line"],
            "keep-melody",
            [
                Alternative(w.T("Repeated chord in 8ths", "Повторяющийся аккорд восьмыми"), keeps, Pattern(AccompanimentPatternKind.RepeatedChord, PatternUnit.Eighths)),
                Alternative(w.T("Held chord", "Выдержанный аккорд"), keeps, Pattern(AccompanimentPatternKind.HeldChord, PatternUnit.Eighths)),
                Alternative(w.T("Falling arpeggio", "Нисходящее арпеджио"), keeps, Pattern(AccompanimentPatternKind.ArpeggioDown, PatternUnit.Sixteenths)),
                Alternative(
                    w.T("Held chord under a detached melody", "Выдержанный аккорд под отрывистой мелодией"),
                    w.T("the pitches, the melody's onsets and the notes of each bar's accompaniment", "высоты, начала нот мелодии и ноты аккомпанемента каждого такта"),
                    Pattern(AccompanimentPatternKind.HeldChord, PatternUnit.Eighths),
                    new(Articulation.OperationId, new ArticulationParameters(ArticulationKind.Detached) { Scope = OperationScope.Melody })),
            ]);
    }

    /// <summary>
    /// "motif.answer": the motif's line comes back exactly. Keep the first statement and change the
    /// repeat with V9 Interval reshape or V10 Change direction, keeping its rhythm.
    /// </summary>
    private static Experiment? AnswerTheMotif(ExperimentContext context)
    {
        if (!context.Confident("structure.recurrences")
            || context.Source.Structure.Recurrences.FirstOrDefault(recurrence => recurrence.IsExact && recurrence.Span.Onset >= context.Source.Selection.Span.End) is not { } repeat)
        {
            return null;
        }

        var passage = new Passage(repeat.Span, repeat.FirstBar, repeat.LastBar);
        bool diatonic = context.Key is not null;
        Words w = context.Source.Words;
        return context.Build(
            AnswerId,
            ExperimentTopic.Motif,
            w.T("Answer the motif differently", "Ответить на мотив иначе"),
            w.T(
                $"The motif stays as written; when it comes back in {Bars(repeat.FirstBar, repeat.LastBar)}, its melody changes and its rhythm stays.",
                $"Мотив остаётся как написано; когда он возвращается {RussianInBars(repeat.FirstBar, repeat.LastBar)}, его мелодия меняется, а ритм остаётся."),
            w.T("Which changes keep the motif recognisable, and which turn it into a new idea?", "Какие изменения оставляют мотив узнаваемым, а какие превращают его в новую мысль?"),
            w.T(
                "Play the first statement as written, then the new answer. Then make up an answer of your own.",
                "Сыграйте первое проведение как написано, затем новый ответ. Потом придумайте свой."),
            ["structure.recurrences"],
            "keep-rhythm",
            [
                Alternative(w.T("Wider steps", "Шире интервалы"), w.T("the rhythm and the contour", "ритм и контур"), new ExperimentStep(IntervalReshape.OperationId, new IntervalReshapeParameters(1))),
                Alternative(w.T("Narrower steps", "Уже интервалы"), w.T("the rhythm and the contour", "ритм и контур"), new ExperimentStep(IntervalReshape.OperationId, new IntervalReshapeParameters(-1))),
                Alternative(w.T("Upside down", "Зеркально"), w.T("the rhythm", "ритм"), new ExperimentStep(ChangeDirection.OperationId, new ChangeDirectionParameters(diatonic))),
            ],
            passage,
            SelectionFilter.All);
    }

    private static ExperimentAlternative Alternative(string label, string keeps, params ExperimentStep[] steps) => new(label, keeps, steps);

    /// <summary>The chain of the steps over the passage, or null with the reason when a step cannot run. <see cref="TryChips"/> runs its chips the same way.</summary>
    internal static VariationChain? TryStart(
        TickSpan span,
        SelectionFilter filter,
        string presetId,
        IReadOnlyList<ExperimentStep> steps,
        IReadOnlyList<Note> notes,
        MeterMap meter,
        MusicalKey? key,
        Words words,
        out string? reason)
    {
        // The passage came from a selection the app made, so it may be a structure selected whole (0013).
        MotifSelection selection = MotifSelector.Select(notes, meter, span, filter, MotifSelection.MaxStructureBars);
        reason = selection.NoteIds.Count == 0 ? words.T("The passage has no notes.", "В отрывке нет нот.") : null;
        if (reason is not null)
        {
            return null;
        }

        IReadOnlySet<KeepProperty> keep = KeepTable.FindPreset(presetId)!.Keep;
        VariationChain chain = VariationChain.Start(VariationFacts.MotifNotes(notes, selection), VariationContext.For(selection, keep, key: key, meter: meter, words: words));
        foreach (ExperimentStep step in steps)
        {
            reason = chain.UnavailableReason(step.Operation, step.Parameters);
            if (reason is not null)
            {
                return null;
            }

            chain = chain.Propose(step.Operation, step.Parameters).Accept();
        }

        return chain;
    }

    /// <summary>The share of the line's notes, all but the last, that sound until the next one starts.</summary>
    private static decimal ConnectedShare(IReadOnlyList<Note> line)
    {
        int held = line.Zip(line.Skip(1), static (note, next) => note.Span.End >= next.Span.Onset).Count(static held => held);
        return (decimal)held / (line.Count - 1);
    }

    private static MusicalKey? KeyOf(InvitationContext context) =>
        context.Motif.Key is { Certainty: not FactCertainty.Uncertain } reading ? reading.Key : null;

    /// <summary>"bar 5", "bars 5–6", "the pickup", "the pickup and bar 1".</summary>
    private static string Bars(int first, int last) => (first, last) switch
    {
        (0, 0) => "the pickup",
        (0, 1) => "the pickup and bar 1",
        (0, _) => $"the pickup and bars 1–{last}",
        _ when first == last => $"bar {first}",
        _ => $"bars {first}–{last}",
    };

    /// <summary>"в такте 5", "в тактах 5–6", "в затакте", "в затакте и такте 1".</summary>
    private static string RussianInBars(int first, int last) => (first, last) switch
    {
        (0, 0) => "в затакте",
        (0, 1) => "в затакте и такте 1",
        (0, _) => $"в затакте и тактах 1–{last}",
        _ when first == last => $"в такте {first}",
        _ => $"в тактах {first}–{last}",
    };

    /// <summary>The invitation context with its key, and the helpers every rule shares.</summary>
    private sealed record ExperimentContext(InvitationContext Source, MusicalKey? Key)
    {
        /// <summary>The selected notes.</summary>
        public IReadOnlyList<Note> Notes { get; } = VariationFacts.MotifNotes(Source.Notes, Source.Selection);

        /// <summary>Whether every fact is in the analysis and none is uncertain.</summary>
        public bool Confident(params string[] ids) =>
            ids.All(id => Source.Motif.Facts.Concat(Source.Structure.Facts).Concat(Source.Character)
                .FirstOrDefault(fact => fact.Id == id) is { Certainty: not FactCertainty.Uncertain });

        /// <summary>
        /// The experiment with only the alternatives that can run on the passage (the selection unless
        /// another is given), or null when none can.
        /// </summary>
        public Experiment? Build(
            string id,
            ExperimentTopic topic,
            string title,
            string change,
            string question,
            string listeningFocus,
            string[] factIds,
            string presetId,
            ExperimentAlternative[] alternatives,
            Passage? passage = null,
            SelectionFilter? filter = null)
        {
            MotifSelection selection = Source.Selection;
            Passage where = passage ?? new Passage(selection.Span, selection.FirstBar, selection.LastBar);
            SelectionFilter which = filter ?? selection.Filter;
            var runnable = new List<ExperimentAlternative>();
            var heard = new List<IReadOnlyList<Note>>();
            foreach (ExperimentAlternative alternative in alternatives)
            {
                // An alternative that sounds like an earlier one, such as Short and Detached on 16ths, is left out.
                if (TryStart(where.Span, which, presetId, alternative.Steps, Source.Notes, Source.Meter, Key, Source.Words, out _) is { Result: { } result }
                    && !heard.Any(notes => notes.SequenceEqual(result.Notes)))
                {
                    runnable.Add(alternative);
                    heard.Add(result.Notes);
                }
            }

            return runnable.Count == 0
                ? null
                : new Experiment(id, topic, title, change, question, listeningFocus, factIds, where, which, presetId, runnable);
        }
    }
}