using MotiffLab.MusicCore.Analysis;
using MotiffLab.MusicCore.Invitations;
using MotiffLab.MusicCore.Motifs;
using MotiffLab.MusicCore.Variations;

namespace MotiffLab.MusicCore.Experiments;

/// <summary>
/// One Try chip (decision record 0018, section 7): a catalogue operation with fixed parameters that a fact invites
/// the player to hear. A tap sounds it on the selected passage with <see cref="KeepPresetId"/>; Keep files the
/// result as an idea with the fact that produced it.
/// </summary>
/// <param name="Id">Stable once shipped, for example "v1.dotted"; ideas and analytics may store it.</param>
/// <param name="OperationId">The catalogue id of the operation, which this version may not have yet.</param>
/// <param name="Parameters">The step's parameters; null while the operation is not built, so the chip is in the table but never shown.</param>
/// <param name="KeepPresetId">The Keep preset the step runs with (<see cref="KeepTable.Presets"/>).</param>
/// <param name="Label">The chip as the player reads it, for example "Long–short".</param>
/// <param name="RussianLabel">The chip in Russian (#134).</param>
public sealed record TryChip(string Id, string OperationId, OperationParameters? Parameters, string KeepPresetId, string Label, string RussianLabel)
{
    /// <summary>Whether this version has the operation, so the chip can be shown at all (preparation item 8).</summary>
    public bool IsBuilt => Parameters is not null && VariationOperations.Find(OperationId) is not null;

    /// <summary>The chip in the player's language.</summary>
    public string LabelIn(Words words) => words.IsRussian ? RussianLabel : Label;
}

/// <summary>One row of the fact-to-chips table: which chips a kind of fact offers, surest fit first.</summary>
/// <param name="FactKind">A fact id without its trailing number, for example "rhythm.durations" or "sentence.close".</param>
public sealed record TryChipRow(string FactKind, IReadOnlyList<string> ChipIds);

/// <summary>
/// The table from fact kind to Try chips (decision record 0018, section 7; preparation item 8), as data: every chip
/// is a catalogue operation with parameters, and a fact offers the chips of its row. Only operations this version
/// has, with a step that can run on the selection, are offered; a chip whose operation is not built is in the table
/// for the record and is never shown.
/// </summary>
/// <remarks>
/// <para>
/// Pure and deterministic, with no AI: the same fact and selection give the same chips in the table's order. A chip
/// runs the way an experiment's alternative does (<see cref="ExperimentCatalogue"/>): one accepted step over the
/// selection with the preset's Keep choices, so what the player hears is a variation like any other.
/// </para>
/// </remarks>
public static class TryChips
{
    private const string PitchOrder = "keep-pitch-order";
    private const string Rhythm = "keep-rhythm";
    private const string Melody = "keep-melody";
    private const string Bass = "keep-bass";
    private const string Contour = "keep-contour";

    /// <summary>Every chip, by id. An operation this version does not have would be a row without parameters; today every chip's operation is built.</summary>
    public static IReadOnlyList<TryChip> Chips { get; } =
    [
        new("v1.dotted", RhythmReshape.OperationId, new RhythmReshapeParameters(RhythmReshapeKind.Dotted) { Scope = OperationScope.Melody }, PitchOrder, "Long–short", "Долгая–короткая"),
        new("v1.syncopate", RhythmReshape.OperationId, new RhythmReshapeParameters(RhythmReshapeKind.Syncopate) { Scope = OperationScope.Melody }, PitchOrder, "Ahead of the beat", "С опережением доли"),
        new("v1.halfTime", RhythmReshape.OperationId, new RhythmReshapeParameters(RhythmReshapeKind.HalfTime), PitchOrder, "Half time", "Вдвое медленнее"),
        new("v1.doubleTime", RhythmReshape.OperationId, new RhythmReshapeParameters(RhythmReshapeKind.DoubleTime), PitchOrder, "Double time", "Вдвое быстрее"),
        new("v1.displace", RhythmReshape.OperationId, new RhythmReshapeParameters(RhythmReshapeKind.Displace, 1) { Scope = OperationScope.Melody }, PitchOrder, "An eighth later", "На восьмую позже"),
        new("v13.detached", Articulation.OperationId, new ArticulationParameters(ArticulationKind.Detached) { Scope = OperationScope.Melody }, PitchOrder, "Detached", "Отрывисто"),
        new("v13.short", Articulation.OperationId, new ArticulationParameters(ArticulationKind.Short) { Scope = OperationScope.Melody }, PitchOrder, "Short", "Коротко"),
        new("v13.connected", Articulation.OperationId, new ArticulationParameters(ArticulationKind.Connected) { Scope = OperationScope.Melody }, PitchOrder, "Connected", "Связно"),
        new("v9.wider", IntervalReshape.OperationId, new IntervalReshapeParameters(1), Rhythm, "Wider steps", "Шире интервалы"),
        new("v9.narrower", IntervalReshape.OperationId, new IntervalReshapeParameters(-1), Rhythm, "Narrower steps", "Уже интервалы"),
        new("v10.mirror", ChangeDirection.OperationId, new ChangeDirectionParameters(Diatonic: true), Rhythm, "Upside down", "Зеркально"),
        new("v4.up", MelodicSequence.OperationId, new MelodicSequenceParameters(1, 1, Diatonic: true), Rhythm, "Sequence up a step", "Секвенция на ступень выше"),
        new("v4.down", MelodicSequence.OperationId, new MelodicSequenceParameters(-1, 1, Diatonic: true), Rhythm, "Sequence down a step", "Секвенция на ступень ниже"),
        new("v2.held", AccompanimentPattern.OperationId, new AccompanimentPatternParameters(AccompanimentPatternKind.HeldChord, PatternUnit.Eighths), Melody, "Held chord", "Выдержанный аккорд"),
        new("v2.repeated", AccompanimentPattern.OperationId, new AccompanimentPatternParameters(AccompanimentPatternKind.RepeatedChord, PatternUnit.Eighths), Melody, "Repeated chord in 8ths", "Повторяющийся аккорд восьмыми"),
        new("v2.arpeggioDown", AccompanimentPattern.OperationId, new AccompanimentPatternParameters(AccompanimentPatternKind.ArpeggioDown, PatternUnit.Sixteenths), Melody, "Falling arpeggio", "Нисходящее арпеджио"),
        new("v7.air", DensityAndRests.OperationId, new DensityParameters(DensityKind.MoreAir) { Scope = OperationScope.Melody }, PitchOrder, "More air", "Больше воздуха"),
        new("v8.shift", AccentShift.OperationId, new AccentShiftParameters(AccentPattern.OffBeat), PitchOrder, "Shift the accents", "Сдвинуть акценты"),
        new("v11.passing", PassingNotes.OperationId, new PassingNotesParameters(PassingNoteKind.Passing) { Scope = OperationScope.Melody }, Bass, "Passing notes", "Проходящие ноты"),
        new("v12.pedal", BassPattern.OperationId, new BassPatternParameters(BassPatternKind.Pedal), Melody, "Pedal bass", "Бас на педали"),
        new("v12.roots", BassPattern.OperationId, new BassPatternParameters(BassPatternKind.RootsOnly), Melody, "Roots in the bass", "Основные тоны в басу"),
        new("v3.voicing", VoicingAndRegister.OperationId, new VoicingParameters(VoicingKind.Close), Melody, "Another voicing", "Другое расположение"),
        new("v6.simplify", Simplify.OperationId, new SimplifyParameters(1), Contour, "Simplify", "Упростить"),
        new("v14.ornament", Ornament.OperationId, new OrnamentParameters(OrnamentKind.Mordent) { Scope = OperationScope.Melody }, Bass, "Ornament", "Орнамент"),
        new("v15.reharmonise", Reharmonise.OperationId, new ReharmoniseParameters(ReharmoniseKind.Relative), Melody, "Reharmonise", "Перегармонизовать"),
    ];

    /// <summary>
    /// The chips the interface offers on a fact's Try button (Andrei, 2026-11-04): what helps a playing musician, that is
    /// simplifying, another voicing, reharmonising, a pedal bass, an ornament, passing notes and accompaniment textures.
    /// The rhythm and contour reshapes stay in the table and the engine, for the assistant, and <see cref="Start"/> runs any chip.
    /// </summary>
    public static IReadOnlyList<string> Offered { get; } =
        ["v6.simplify", "v3.voicing", "v15.reharmonise", "v12.pedal", "v14.ornament", "v11.passing", "v2.held", "v2.repeated", "v2.arpeggioDown"];

    /// <summary>The most chips a fact's Try button opens.</summary>
    public const int MaxOffered = 3;

    /// <summary>The prefix of a Try button's experiment id, followed by its fact's kind: "try.melody.line".</summary>
    public const string ExperimentPrefix = "try.";

    /// <summary>The ids a Try button's experiment can have, one per fact kind of the table; analytics may send only these.</summary>
    public static IReadOnlyList<string> ExperimentIds => [.. Table.Select(row => ExperimentPrefix + row.FactKind)];

    /// <summary>The table, by fact kind, in the order the facts are listed; a kind not here offers no chips.</summary>
    public static IReadOnlyList<TryChipRow> Table { get; } =
    [
        new("rhythm.durations", ["v1.dotted", "v1.syncopate", "v1.halfTime", "v7.air", "v6.simplify"]),
        new("rhythm.patterns", ["v1.dotted", "v1.displace", "v8.shift", "v6.simplify"]),
        new("rhythm.grouping", ["v1.dotted", "v1.syncopate", "v8.shift"]),
        new("rhythm.syncopation", ["v1.syncopate", "v1.displace", "v8.shift"]),
        new("rhythm.silences", ["v13.detached", "v13.connected", "v7.air"]),
        new("rhythm.accents", ["v8.shift", "v13.detached"]),
        new("rhythm.grid", ["v1.halfTime", "v1.doubleTime"]),
        new("melody.intervals", ["v9.wider", "v9.narrower", "v11.passing", "v14.ornament", "v6.simplify"]),
        new("melody.contour", ["v10.mirror", "v9.wider", "v9.narrower", "v11.passing", "v14.ornament"]),
        new("melody.line", ["v4.up", "v4.down", "v11.passing", "v14.ornament", "v6.simplify"]),
        new("melody.degrees", ["v4.up", "v4.down", "v10.mirror", "v11.passing"]),
        new("melody.range", ["v4.up", "v4.down", "v3.voicing"]),
        new("melody.phrases", ["v13.detached", "v13.connected"]),
        new("character.articulation", ["v13.detached", "v13.short", "v13.connected"]),
        new("character.density", ["v2.held", "v2.repeated", "v7.air", "v6.simplify"]),
        new("character.register", ["v3.voicing", "v4.up", "v4.down"]),
        new("character.syncopation", ["v1.syncopate", "v1.displace"]),
        new("character.tempo", ["v1.halfTime", "v1.doubleTime"]),
        new("character.velocity", ["v8.shift", "v13.detached"]),
        new("structure.recurrences", ["v10.mirror", "v9.wider", "v9.narrower", "v4.up", "v14.ornament", "v11.passing"]),
        new("structure.phrases", ["v13.connected", "v7.air"]),
        new("sentence.phrases", ["v12.pedal", "v2.held"]),
        new("sentence.close", ["v12.pedal", "v15.reharmonise"]),
        new("sentence.repeat", ["v10.mirror", "v9.wider", "v4.up", "v14.ornament", "v11.passing"]),
        new("sentence.answer", ["v10.mirror", "v9.narrower", "v14.ornament"]),
        new("harmony.chords", ["v2.repeated", "v2.arpeggioDown", "v12.pedal", "v15.reharmonise"]),
        new("harmony.bass", ["v12.pedal", "v12.roots", "v2.held"]),
        new("harmony.rhythm", ["v15.reharmonise", "v2.held", "v12.roots"]),
        new("harmony.voicing", ["v3.voicing", "v2.arpeggioDown", "v2.repeated"]),
        new("harmony.shared", ["v15.reharmonise", "v12.pedal"]),
    ];

    private static readonly Dictionary<string, TryChip> s_chips = Chips.ToDictionary(chip => chip.Id, StringComparer.Ordinal);

    private static readonly HashSet<string> s_offered = new(Offered, StringComparer.Ordinal);

    /// <summary>The chip with this id, or null.</summary>
    public static TryChip? Find(string id) => s_chips.GetValueOrDefault(id);

    /// <summary>A fact id's kind: the id without a trailing number, so "sentence.close.3" is "sentence.close".</summary>
    public static string KindOf(string factId)
    {
        ArgumentNullException.ThrowIfNull(factId);
        int dot = factId.LastIndexOf('.');
        return dot > 0 && int.TryParse(factId.AsSpan(dot + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _) ? factId[..dot] : factId;
    }

    /// <summary>The chips a fact's row names whose operation this version has, in the table's order, before any check against the selection.</summary>
    public static IReadOnlyList<TryChip> For(string factId)
    {
        string kind = KindOf(factId);
        return Table.FirstOrDefault(row => row.FactKind == kind) is { } found
            ? [.. found.ChipIds.Select(id => s_chips[id]).Where(chip => chip.IsBuilt)]
            : [];
    }

    /// <summary>
    /// The chips to show on a fact for the selection in <paramref name="context"/>: those of <see cref="For"/> the
    /// interface offers (<see cref="Offered"/>) whose step can run on the selected notes with its preset, so a chip
    /// never leads to a disabled step; at most <see cref="MaxOffered"/>, in the table's order.
    /// </summary>
    public static IReadOnlyList<TryChip> Offer(string factId, InvitationContext context) => Offer(factId, context, except: null);

    private static IReadOnlyList<TryChip> Offer(string factId, InvitationContext context, IReadOnlySet<string>? except)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [.. For(factId)
            .Where(chip => s_offered.Contains(chip.Id) && except?.Contains(chip.Id) != true)
            .Where(chip => Start(chip, context, out _) is not null)
            .Take(MaxOffered)];
    }

    /// <summary>
    /// The facts' Try buttons, one per fact kind that offers chips, in the facts' order: an experiment whose ways are the
    /// chips <see cref="Offer"/> gives, each with its own Keep preset, resting on every fact of that kind, so the page
    /// tries, keeps and adjusts them as it does any experiment. Facts of one kind share it, since their chips are the same,
    /// and a chip is offered once, on the first kind that offers it, so the stream does not repeat the same way.
    /// </summary>
    public static IReadOnlyList<Experiment> Buttons(IEnumerable<MotifFact> facts, InvitationContext context)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(context);
        if (context.Selection.NoteIds.Count == 0)
        {
            return [];
        }

        Words w = context.Words;
        MotifSelection selection = context.Selection;
        var buttons = new List<Experiment>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (IGrouping<string, MotifFact> kind in facts.GroupBy(fact => KindOf(fact.Id), StringComparer.Ordinal))
        {
            IReadOnlyList<TryChip> chips = Offer(kind.Key, context, used);
            if (chips.Count == 0)
            {
                continue;
            }

            used.UnionWith(chips.Select(chip => chip.Id));

            buttons.Add(new Experiment(
                ExperimentPrefix + kind.Key,
                ExperimentTopic.Character,
                w.T("Try a variation", "Попробовать вариацию"),
                w.T(
                    "Each way changes the passage with one operation and keeps what is named with it.",
                    "Каждый вариант меняет отрывок одной операцией и сохраняет то, что названо рядом с ним."),
                w.T("What does this change, and what stays?", "Что это меняет, а что остаётся?"),
                w.T(
                    "Hear the original, then the variation, then play the variation at the piano.",
                    "Послушайте оригинал, затем вариацию, потом сыграйте вариацию на фортепиано."),
                [.. kind.Select(fact => fact.Id).Distinct(StringComparer.Ordinal)],
                new Passage(selection.Span, selection.FirstBar, selection.LastBar),
                selection.Filter,
                chips[0].KeepPresetId,
                [.. chips.Select(chip => new ExperimentAlternative(chip.LabelIn(w), Keeps(chip.KeepPresetId, w), [new ExperimentStep(chip.OperationId, chip.Parameters!)]) { KeepPresetId = chip.KeepPresetId })]));
        }

        return buttons;
    }

    /// <summary>What a preset keeps, completing "Keeps …".</summary>
    private static string Keeps(string presetId, Words w) => presetId switch
    {
        PitchOrder => w.T("the pitches and their order", "высоты и их порядок"),
        Rhythm => w.T("the rhythm", "ритм"),
        Melody => w.T("the melody", "мелодия"),
        Bass => w.T("the bass", "бас"),
        Contour => w.T("the contour", "контур"),
        _ => throw new ArgumentOutOfRangeException(nameof(presetId), presetId, "Unknown preset."),
    };

    /// <summary>The variation a chip sounds: one accepted step over the selection, or null with the reason when it cannot run.</summary>
    public static VariationChain? Start(TryChip chip, InvitationContext context, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(chip);
        ArgumentNullException.ThrowIfNull(context);
        if (!chip.IsBuilt)
        {
            reason = context.Words.T($"This version has no operation {chip.OperationId}.", $"В этой версии нет операции {chip.OperationId}.");
            return null;
        }

        MusicalKey? key = context.Motif.Key is { Certainty: not FactCertainty.Uncertain } reading ? reading.Key : null;
        return ExperimentCatalogue.TryStart(
            context.Selection.Span,
            context.Selection.Filter,
            chip.KeepPresetId,
            [new ExperimentStep(chip.OperationId, chip.Parameters!)],
            context.Notes,
            context.Meter,
            key,
            context.Words,
            out reason);
    }
}